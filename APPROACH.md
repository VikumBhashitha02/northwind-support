# Engineering Approach & Design Rationale

This document details the engineering thought process, architectural decisions, and trade-offs made while completing the Northwind Support Desk technical assessment. It is written to clearly explain *why* specific solutions were selected, what alternatives were considered, and how the system satisfies the requirements in [`ASSIGNMENT_CANDIDATE.md`](ASSIGNMENT_CANDIDATE.md).

---

## Task 1: Defect Fix & Database-Side Filtering

### 1.1 DEFECT-117 (Stale Filter State)

#### Problem & Investigation
When testing ticket filtering in the browser, changing the "Status" dropdown to "In progress" caused no network request. However, typing into the search input subsequently triggered a request containing both the search query and the previously selected status. Furthermore, clearing the search could leave filters in a stale or unsynced state.

Inspection of `apps/web/src/features/tickets/hooks/useTicketList.ts` revealed that state was split between `filterDraft` (raw user input) and `appliedFilters` (the state driving the API request). A single `useEffect` was listening only to the debounced search term:
```ts
// Flawed original logic:
useEffect(() => {
  setAppliedFilters(draft => ({ ...draft, search: debouncedSearch }));
}, [debouncedSearch]);
```
Because dropdown changes updated `filterDraft` without changing `debouncedSearch`, the effect never triggered. When the user eventually typed in the search box, `debouncedSearch` changed, triggering the effect which read whatever `filterDraft` currently held and finally dispatched the request.

#### Approach & Implementation
Rather than using workarounds like forced remount keys or in-memory list filtering, the hook was refactored:
1. Created an isolated debounced state for the search input string (`useDebounce(search, 300)`).
2. All non-text filters (`status`, `priority`, `categoryId`, `customerId`, `assignedAgentId`, `unassignedOnly`, `slaStatus`) update the active filter state immediately.
3. Composed the final `TicketQuery` combining the immediate filter state with the debounced search term in a single memoized query object.
4. Added an automated regression test in `useTicketList.test.ts` to assert that changing the status filter alone triggers a fetch request immediately.

---

### 1.2 Server-Side Database Filtering

#### Problem & Requirements
The API endpoint `GET /api/tickets` accepted `TicketQuery` parameters, but `TicketQueries.cs` ignored most parameters or performed incomplete filtering. Requirements dictated that:
- Filters must be evaluated by SQL Server before counting and paging.
- Multiple filters must combine with `AND` semantics.
- Searching by customer name must translate to SQL through the `Customer` relationship without N+1 queries.
- Pagination metadata (`totalCount`, `totalPages`) must reflect the filtered dataset.

#### Approach & Implementation
In `SupportDesk.Infrastructure/Queries/TicketQueries.cs`:
1. Started with an `IQueryable<Ticket>` using `AsNoTracking()` to avoid EF change tracker overhead.
2. Composed conditional filters:
   ```csharp
   if (query.Status.HasValue) queryable = queryable.Where(t => t.Status == query.Status.Value);
   if (query.Priority.HasValue) queryable = queryable.Where(t => t.Priority == query.Priority.Value);
   if (query.CategoryId.HasValue) queryable = queryable.Where(t => t.CategoryId == query.CategoryId.Value);
   if (query.CustomerId.HasValue) queryable = queryable.Where(t => t.CustomerId == query.CustomerId.Value);
   if (query.AssignedAgentId.HasValue) queryable = queryable.Where(t => t.AssignedAgentId == query.AssignedAgentId.Value);
   if (query.UnassignedOnly == true) queryable = queryable.Where(t => t.AssignedAgentId == null);
   ```
3. Handled search across multiple fields at the database level:
   ```csharp
   if (!string.IsNullOrWhiteSpace(query.Search))
   {
       var term = query.Search.Trim();
       queryable = queryable.Where(t =>
           t.Title.Contains(term) ||
           t.Reference.Contains(term) ||
           t.Customer.Name.Contains(term));
   }
   ```
4. Computed total count on the filtered query: `var total = await queryable.CountAsync(ct)`.
5. Applied sorting and paging (`Skip((page - 1) * pageSize).Take(pageSize)`) before projecting to DTOs.
6. Verified with unit tests covering exact match priority, multiple AND filters, customer name search, and unassigned filtering.

---

## Task 2: Ticket Escalation, Automatic Triage & SLA Tracking

### 2.1 Triage & Business Rules Architecture

#### Design Decisions
Business rules were encapsulated in `ITicketTriageService` (`SupportDesk.Application.Features.Tickets.Triage.TicketTriageService`) so that both ticket creation (`RaiseTicketCommandHandler`) and ticket escalation (`EscalateTicketCommandHandler`) reuse the identical calculation logic without duplication.

#### Priority Rules
- Uses requested priority or defaults to `Medium`.
- Categories flagged `ForcesCriticalPriority = true` automatically override any requested priority with `Critical`.

#### SLA Due Date Calculation
- Base SLA windows: Critical (4h), High (8h), Medium (24h), Low (72h).
- Premium Customers (`Customer.Tier == CustomerTier.Premium`): Window is cut in half (`windowHours * 0.5`).
- Minimum Window Floor: The final window cannot be shorter than 1 hour (`Math.Max(windowHours, 1)`).
- Time Source: Driven by an injectable `IClock` abstraction (`SystemClock` in production, `FixedClock` in unit tests) ensuring deterministic date/time assertions across year/month/leap boundaries.

#### Agent Eligibility & Tie-Breaking
An agent is eligible if and only if:
1. `agent.IsActive == true`.
2. `openTicketCount < agent.MaxOpenTickets` (where open tickets are tickets not in `Resolved` or `Closed` status).
3. If `category.RequiresSpecialist == true`, the agent must possess an `AgentSpecialization` for that category.

If multiple agents are eligible, the agent with the fewest open tickets is chosen. Deterministic tie-breaker: If open ticket counts are equal, the agent with the lowest `Id` is selected. If no agent is eligible, the ticket remains unassigned and a clear triage explanation is returned without failing the request.

---

### 2.2 Escalation Implementation (`POST /api/tickets/{id}/escalate`)

#### Business Rules & Conflict Handling
- Escalation priority sequence: `Low` → `Medium` → `High` → `Critical`.
- Invariant validation: If a ticket is already `Critical`, or has status `Resolved` or `Closed`, escalation is forbidden. The API returns HTTP 409 Conflict with a clear problem document.
- Due date calculation: Recalculates due date starting from the escalation timestamp using the escalated priority's SLA window.
- Agent assignment: If the currently assigned agent satisfies the requirements of the escalated category/priority (e.g., specialist requirements if applicable and workload limits), they remain assigned; otherwise normal assignment rules evaluate a new eligible agent.
- Audit history: An immutable `TicketEscalation` aggregate record is saved in SQL Server capturing `FromPriority`, `ToPriority`, `FromAgentId`, `ToAgentId`, `FromDueAtUtc`, `ToDueAtUtc`, `Reason`, `EscalatedBy`, and `EscalatedAtUtc`.

---

### 2.3 Derived SLA Status

#### Philosophy
SLA status (`WithinSla`, `AtRisk`, `Breached`, `Met`, `NotApplicable`) is a derived state that changes over time even when no database writes occur. Storing SLA status in a database column leads to stale data and requires cron polling jobs to keep it up to date.

Instead, SLA status is computed dynamically:
- If `DueAtUtc == null`: `NotApplicable`.
- If `ResolvedAtUtc <= DueAtUtc`: `Met`.
- If `ResolvedAtUtc > DueAtUtc`: `Breached`.
- If unresolved and `now > DueAtUtc`: `Breached`.
- If unresolved and `(DueAtUtc - now) <= (TotalWindow * 0.25)`: `AtRisk`.
- Otherwise: `WithinSla`.

In `TicketQueries.cs`, these conditions were expressed using SQL-translatable EF Core constructs (`EF.Functions.DateDiffSecond`), allowing users to filter by `slaStatus` directly within database queries.

---

## Task 3: Agent Authentication & Authorization

### 3.1 Password Hashing & Clean Architecture

#### Problem & Decision
Task 3 requires secure password hashing without storing plaintext credentials.
In ASP.NET Core applications, developers frequently reach for `Microsoft.AspNetCore.Identity`. However, the repository includes `LayerDependencyTests` that strictly asserts:
> *SupportDesk.Infrastructure must not reference Microsoft.AspNetCore.*

To honor the solution's Clean Architecture boundary while adhering to cryptographic best practices, an `IPasswordHasher` was designed using .NET's built-in `System.Security.Cryptography.Rfc2898DeriveBytes.Pbkdf2`:
- 100,000 iterations of HMAC-SHA256.
- 16-byte cryptographically secure random salt generated per password.
- Hash output formatted as `pbkdf2:100000:<salt_base64>:<hash_base64>`.
- Verification performed with `CryptographicOperations.FixedTimeEquals` to prevent timing attacks.

Seeded agent accounts in `SupportDbSeeder.cs` are hashed with `Password123!` at seed time.

---

### 3.2 JWT Token Generation & Lifetime

- Tokens are signed with HMAC-SHA256 using a symmetric key configured via environment variable `JWT_SIGNING_KEY` (or fallback development configuration).
- Expiration is set to **8 hours** to balance workday convenience with security hygiene.
- Contains claims: `ClaimTypes.NameIdentifier` (agent ID), `ClaimTypes.Name` (agent full name), `ClaimTypes.Email`, and standard JWT claims (`sub`, `jti`).

---

### 3.3 Authorization & Actor Impersonation Protection

1. **Global Authorization Fallback Policy:**
   ```csharp
   builder.Services.AddAuthorization(options =>
   {
       options.FallbackPolicy = new AuthorizationPolicyBuilder()
           .RequireAuthenticatedUser()
           .Build();
   });
   ```
   All endpoints require authentication by default. Only `POST /api/auth/login`, Swagger, and `/health` are decorated with `[AllowAnonymous]`.

2. **Actor Impersonation Prevention (Task 3.13):**
   In `TicketsController.Escalate`, the escalating agent's identity is derived from `User.FindFirstValue(ClaimTypes.Name)`. Client-supplied actor payloads in `EscalateTicketRequest` cannot override or spoof the identity recorded in `TicketEscalation`.

---

### 3.4 Frontend Authentication Flow

- **State Management:** `AuthProvider` wraps the entire application and exposes `useAuth()`.
- **Token Storage:** Stored in `localStorage` under `northwind_auth_token`.
- **Route Guard:** `ProtectedRoute` checks `isAuthenticated`. If unauthenticated, it redirects to `/login` passing the target URL in `location.state.from`.
- **401 Interception:** When an API call returns 401 Unauthorized (except for failed login attempts), `client.ts` automatically clears the local token and notifies the auth provider to redirect to login.
- **Login UX:** Accessible form with label associations, inline client validation, disabled submit state while pending, accessible error alerts on credential rejection, and automatic redirection to the requested return URL upon success.

---

## Task 4: Dockerisation & Health Monitoring

### 4.1 Multi-Stage Dockerfile (`apps/api/Dockerfile`)

Targeting .NET 10:
- **Build Stage:** `mcr.microsoft.com/dotnet/sdk:10.0`
  1. Copies `global.json`, `Directory.Build.props`, and `Directory.Packages.props`.
  2. Copies each `.csproj` file preserving directory structures.
  3. Executes `dotnet restore apps/api/src/SupportDesk.Presentation/SupportDesk.Presentation.csproj`.
  4. Copies application source code.
  5. Executes `dotnet publish ... --no-restore -c Release -o /app/publish`.
  *This structure ensures that changes to source files do not invalidate the cached NuGet restore layer.*
- **Runtime Stage:** `mcr.microsoft.com/dotnet/aspnet:10.0`
  - Runs as built-in non-root user `USER app`.
  - Configures `ASPNETCORE_HTTP_PORTS=8080`.
  - Exposes port 8080 without bundling HTTPS dev certificates.

### 4.2 Docker Compose & Database Health Dependency
In `docker-compose.yml`, the `api` service defines:
```yaml
depends_on:
  sqlserver:
    condition: service_healthy
```
This guarantees the API does not start until SQL Server is fully ready to accept queries.

### 4.3 Central Health Endpoint (`GET /health`)
An unauthenticated endpoint (`HealthController.cs`) tests database reachability via `await dbContext.Database.CanConnectAsync(ct)`. It returns HTTP 200 with `{ "status": "Healthy", "database": "Connected" }` if SQL Server is accessible, or HTTP 503 if unavailable.

---

## Known Limitations & Future Enhancements

1. **Authentication:** Registration, self-service password reset, MFA, refresh tokens, and fine-grained role-based permissions were intentionally omitted in accordance with the assignment's explicit out-of-scope guidance.
2. **Token Storage:** In a production internet-facing environment, storing JWT tokens in HttpOnly, SameSite cookies with CSRF tokens is recommended over `localStorage` to eliminate XSS token theft vectors.
3. **Automated Retries:** Background worker queues (e.g., Hangfire / MassTransit) could be introduced for asynchronous event notifications upon ticket escalation.
