# Northwind Support Desk

A production-grade support ticket management system built with **.NET 10 Web API**, **React 19 / TypeScript**, **SQL Server**, and **Docker**.

Repository: `VikumBhashitha02/northwind-support` (GitHub Fork)

---

## 1. Implemented Features & Business Rules

### Task 1: Defect Fix & Database-Side Filtering
- **DEFECT-117 Fixed:** Resolved state synchronization and stale closures in `useTicketList` where dropdown filter changes failed to trigger API requests until subsequent typing occurred.
- **Server-Side Filtering:** Implemented composable EF Core query filtering for `status`, `priority`, `categoryId`, `customerId`, `assignedAgentId`, `unassignedOnly`, and `search` across title, ticket reference, and customer name. Count and pagination are computed strictly at the database level using `IQueryable.CountAsync()` before `Skip()` and `Take()`.
- **SLA Status Filtering:** Added SQL-translatable derived SLA status calculation (`WithinSla`, `AtRisk`, `Breached`, `Met`, `NotApplicable`) directly into the EF Core query pipeline.

### Task 2: Automatic Triage, Assignment, SLA & Escalation
- **Priority Resolution:** Defaults to `Medium` if unspecified; categories flagged with `ForcesCriticalPriority` automatically enforce `Critical` priority.
- **SLA Due Date Calculation:**
  - Base windows: Critical (4h), High (8h), Medium (24h), Low (72h).
  - Premium customers receive a 50% window reduction (`WindowHours * 0.5`).
  - Hard floor enforced: SLA windows are never shorter than 1 hour.
- **Agent Eligibility & Automatic Assignment:**
  - Eligible agents must be active, hold fewer open tickets than their `MaxOpenTickets`, and possess the necessary specialization when `Category.RequiresSpecialist` is true.
  - Least-loaded agent selection with deterministic tie-breaker (lowest ID).
  - Graceful fallback: If no agent is eligible, the ticket is created unassigned with an explicit human-readable triage explanation.
- **Ticket Escalation (`POST /api/tickets/{id}/escalate`):**
  - Priority escalates by exactly one tier: `Low` → `Medium` → `High` → `Critical`.
  - Prohibits escalating tickets that are already `Critical`, `Resolved`, or `Closed` (returns HTTP 409 Conflict).
  - Validates reason (5–500 characters) and records an immutable escalation record.
  - Recalculates due date from the escalation timestamp and reassesses agent eligibility.
- **Escalation History (`GET /api/tickets/{id}/escalations`):**
  - Returns full escalation history ordered newest first (`EscalatedAtUtc` DESC).
- **Frontend SLA Indicator & Escalation Panel:**
  - Reusable, accessible `SlaIndicator` with distinct color badges and readable text (not relying on color alone).
  - Fully reactive escalation panel with client validation, pending state, HTTP 409 handling, and live refreshes without full page reloads.

### Task 3: Authentication & Authorization
- **Agent Authentication (`POST /api/auth/login`):** Validates email and PBKDF2 password hash, issuing an HMAC-SHA256 signed JWT token.
- **Generic 401 Responses:** Both unknown emails and invalid passwords return an identical generic error to prevent account enumeration. Inactive agents are blocked.
- **Actor Impersonation Prevention:** In `TicketsController.Escalate`, `EscalatedBy` is extracted directly from the authenticated JWT claims (`ClaimTypes.Name`), ignoring any client-provided actor payload.
- **Central Authorization:** Global `FallbackPolicy` requiring authenticated users for all endpoints, explicitly permitting login, Swagger, and `/health`.
- **Frontend Route Protection:** Session management via `AuthProvider` and `ProtectedRoute`, automatically redirecting unauthenticated users to `/login` with `location.state.from` preserved for post-login return.

### Task 4: Dockerisation & Health Monitoring
- **Multi-Stage `apps/api/Dockerfile`:** Uses .NET 10 SDK build stage and slim ASP.NET 10 runtime stage.
- **Layer Caching:** Copies `global.json`, `Directory.Build.props`, `Directory.Packages.props`, and `.csproj` files prior to source copy for cached dependency restoration.
- **Security:** Runs as non-root user `app` on HTTP port `8080`.
- **Central Health Endpoint (`GET /health`):** Unauthenticated endpoint verifying live database reachability via `CanConnectAsync()`.

---

## 2. DEFECT-117: Root Cause, Fix, and Reproduction Steps

### Root Cause
In `apps/web/src/features/tickets/hooks/useTicketList.ts`, the filtering mechanism maintained two separate state sources that fell out of sync:
1. `filterDraft` (draft state updated immediately by UI inputs).
2. An effect triggered only by debounced search queries that copied `filterDraft` to `appliedFilters`.

When a user changed a dropdown (e.g., Status to "In progress"):
- `filterDraft.status` updated in state, but `appliedFilters` remained unchanged because the search debounce timer only watched `search`.
- No network request was fired.
- Typing into the search box later triggered the debounce effect, which read the stale or draft state and sent the delayed status filter along with the new search.
- Clearing the search did not reliably sync other filters.

### The Fix
Refactored `useTicketList` to apply clean separation of concerns:
- Dedicated debounced string hook for the search text input (300ms debounce).
- Direct, immediate state updates for dropdown/toggle filters (`status`, `priority`, `categoryId`, `customerId`, `assignedAgentId`, `unassignedOnly`, `slaStatus`).
- Composed the final `TicketQuery` combining immediate filters with the debounced search term.
- Filter changes immediately trigger the API request without waiting for typing or losing filter state.

### Regression Test
A regression test was added in `apps/web/src/features/tickets/hooks/useTicketList.test.ts`:
- Verifies that changing the status filter alone immediately triggers a `GET /api/tickets?status=InProgress` fetch without requiring search field interaction.

---

## 3. Server-Side Filtering & SQL-Translatable Queries

### EF Core Query Implementation
All filtering is performed database-side in `apps/api/src/SupportDesk.Infrastructure/Queries/TicketQueries.cs`:
- Every condition is composed on `IQueryable<Ticket>` before execution:
  - `status`, `priority`, `categoryId`, `customerId`, `assignedAgentId`: Exact match filters.
  - `unassignedOnly`: Checks `t.AssignedAgentId == null`.
  - `search`: Trimmed string search checking `t.Title.Contains()`, `t.Reference.Contains()`, or related `t.Customer.Name.Contains()`. Blank/whitespace queries are ignored.
- **Total Count & Pagination:** `await query.CountAsync(ct)` is executed against the filtered query before `Skip((page - 1) * pageSize).Take(pageSize)` to ensure `totalCount` and `totalPages` accurately reflect the filtered dataset.

### Adding New Filters (e.g., `slaStatus`)
Adding a filter such as `slaStatus` is cleanly achieved without rewriting queries:
1. Add property to `TicketQuery` contract.
2. In `TicketQueries.cs`, compute the SQL-translatable expression:
   - For `Breached`: `t.ResolvedAtUtc > t.DueAtUtc || (t.ResolvedAtUtc == null && t.DueAtUtc != null && t.DueAtUtc < now)`
   - For `Met`: `t.ResolvedAtUtc != null && t.DueAtUtc != null && t.ResolvedAtUtc <= t.DueAtUtc`
   - For `AtRisk`: `t.ResolvedAtUtc == null && t.DueAtUtc != null && t.DueAtUtc >= now && EF.Functions.DateDiffSecond(now, t.DueAtUtc.Value) <= (EF.Functions.DateDiffSecond(t.CreatedAtUtc, t.DueAtUtc.Value) * 0.25)`
   - For `WithinSla`: Unresolved, due in future, and not in the last 25% window.
3. Chain `.Where(...)` into the composable `IQueryable`.

---

## 4. Key Design Decisions

| Decision | Selected Approach | Rejected Alternatives | Rationale |
| --- | --- | --- | --- |
| **Password Hashing** | PBKDF2 (`Rfc2898DeriveBytes.Pbkdf2`) with 100,000 iterations, SHA-256, and 16-byte random salt | `Microsoft.AspNetCore.Identity.PasswordHasher` | `SupportDesk.Infrastructure` has an architectural boundary test (`LayerDependencyTests`) prohibiting references to `Microsoft.AspNetCore.*`. BCL PBKDF2 provides NIST-standard security while respecting Clean Architecture boundaries. |
| **Actor Resolution** | Derived exclusively from validated JWT claims (`ClaimTypes.Name`) on the server | Trusting `request.EscalatedBy` from client payload | Prevents agent impersonation (Task 3.13). The client cannot forge or override who escalated the ticket. |
| **Frontend Token Storage** | `localStorage` with reactive `AuthProvider` and 401 client interceptor | In-memory only; HttpOnly cookies | `localStorage` maintains session persistence across browser reloads without requiring complex CSRF architectures for REST Web APIs. The trade-off is documented below. |
| **SLA Status Calculation** | Derived dynamically from timestamps (`CreatedAtUtc`, `DueAtUtc`, `ResolvedAtUtc`, and `now`) | Stored column updated by background jobs | Stored SLA status drifts as time passes and requires continuous cron recalculation. Calculating derived status guarantees real-time accuracy. |

---

## 5. Database Changes & EF Core Migrations

Two migrations were added to `apps/api/src/SupportDesk.Infrastructure/Data/Migrations/`:

1. **`20261017202353_AddTicketEscalations.cs`**:
   - Creates `TicketEscalations` table:
     - `Id` (int, PK, identity).
     - `TicketId` (int, FK to `Tickets.Id`, ON DELETE CASCADE).
     - `FromPriority`, `ToPriority` (nvarchar(50), required).
     - `FromAgentId`, `ToAgentId` (int, nullable, FK to `Agents.Id`, ON DELETE NO ACTION).
     - `FromDueAtUtc`, `ToDueAtUtc` (datetime2, nullable).
     - `Reason` (nvarchar(500), required).
     - `EscalatedBy` (nvarchar(100), required).
     - `EscalatedAtUtc` (datetime2, required).
   - **Indexes added:**
     - `IX_TicketEscalations_TicketId_EscalatedAtUtc` (composite index): Optimizes `GET /api/tickets/{id}/escalations` for newest-first historical lookups.
     - `IX_TicketEscalations_FromAgentId` and `IX_TicketEscalations_ToAgentId`: Speeds up agent audit queries.

2. **`20261017220649_AddAgentPasswordHash.cs`**:
   - Adds `PasswordHash` (nvarchar(500), nullable) to `Agents` table.
   - Preserves existing agent records without breaking schema.

---

## 6. Authentication Architecture & Security Details

- **Password Hashing:** Formatted as `pbkdf2:100000:<salt_base64>:<hash_base64>`. Verification uses `CryptographicOperations.FixedTimeEquals` to prevent timing attacks.
- **Token Specifications:** Signed with HMAC-SHA256 (`HmacSha256Signature`). Lifetime configured to **8 hours**.
- **Claims Included:** `ClaimTypes.NameIdentifier` (Agent ID), `ClaimTypes.Name` (Agent FullName), `ClaimTypes.Email`, `sub`, `email`, `jti`.
- **Token Storage Trade-off:** The JWT token is saved in `localStorage` under `northwind_auth_token`.
  - *Advantage:* Allows immediate page reloads and cross-tab session maintenance without requiring server-side session stores or CSRF tokens.
  - *Security Trade-off:* Accessible to JavaScript in the same origin. XSS vulnerabilities could expose the token. Mitigated by strict React JSX escaping and prohibiting arbitrary HTML rendering. In high-assurance environments, HttpOnly SameSite cookies with anti-forgery headers would be preferred.

---

## 7. Development Login Credentials (Local Fixtures)

All seeded test agents are initialized with the local password: `Password123!`

| Agent | Email | Role / Specialization | Status |
| --- | --- | --- | --- |
| **Alex Turner** | `alex.turner@northwind-support.example` | General, Technical (5 / 10 open) | Active |
| **Ben Osei** | `ben.osei@northwind-support.example` | Technical, Outage (3 / 8 open) | Active |
| **Priya Nair** | `priya.nair@northwind-support.example` | Outage, Security (4 / 8 open) | Active |
| **Sara Lindqvist** | `sara.lindqvist@northwind-support.example` | General, Billing (2 / 6 open) | Active |
| **Marcus Doyle** | `marcus.doyle@northwind-support.example` | Billing, Security (6 / 6 open - full) | Active (At capacity) |
| **Yuki Tanaka** | `yuki.tanaka@northwind-support.example` | Technical (0 / 8 open) | Inactive (Cannot log in) |

---

## 8. Setup & Execution Commands

### Prerequisites
- Node.js 20+ (recommended Node 22)
- Docker & Docker Compose
- .NET 10 SDK (or use containerized build commands)

### Running Locally
```bash
# 1. Install frontend dependencies
npm install

# 2. Start SQL Server 2022
npm run db:up

# 3. Start Backend API (runs migrations and seeds test accounts)
npm run api

# 4. In a separate terminal, start the React application
npm run web
```

### Running Backend Tests (.NET 10 via Docker)
```bash
docker run --rm -v "${PWD}:/app" -v nuget-cache:/root/.nuget/packages -w /app mcr.microsoft.com/dotnet/sdk:10.0 dotnet test SupportDesk.sln
```

### Running Frontend Tests & Typecheck
```bash
# Vitest test suite
npm run test:web

# TypeScript typecheck
npm run typecheck

# ESLint validation
npm run lint
```

---

## 9. Docker Architecture & Production Migration Strategy

### Docker API Configuration
- `apps/api/Dockerfile`: Multi-stage build targeting .NET 10.
- `docker-compose.yml`: Services `sqlserver` (port 1433) and `api` (port 5080) with healthy startup dependency ordering.
- Health endpoint: `http://localhost:5080/health` returns `{ "status": "Healthy", "database": "Connected" }`.

### Production Migration Strategy
In local development, `MigrateAndSeedAsync()` runs automatically on startup. **For production deployments, this practice should be avoided**:
1. **Concurrency Collisions:** Multiple API container replicas starting simultaneously can attempt concurrent schema migrations, causing deadlocks or corrupt schema versions.
2. **Permission Boundaries:** Runtime applications should run with least-privilege database users (`SELECT`, `INSERT`, `UPDATE`, `DELETE`), whereas migrations require DDL permissions (`ALTER`, `CREATE TABLE`).
3. **Recommended Production Pattern:** Generate migration SQL scripts (`dotnet ef migrations script`) and apply them through a dedicated CI/CD deployment pipeline or Kubernetes init-job before rolling out new application versions.

---

## 10. Tests Executed & Verification Summary

### Backend Tests (xUnit + Moq) — 94 Tests Passed
- `LayerDependencyTests` (4 tests): Verified architecture boundaries between Domain, Application, Infrastructure, and Presentation.
- `SupportDesk.UnitTests` (90 tests):
  - Model snapshot and migration parity (`TheModel_MatchesTheLatestMigration`).
  - Ticket query filtering, customer name search, unassigned filters, and database counts.
  - Priority determination, SLA windows (4h, 8h, 24h, 72h), premium half-window, 1-hour floor.
  - Agent assignment rules, specialist requirement, workload count, deterministic tie-breaking.
  - Ticket escalation priority transitions, invalid state (Critical/Resolved/Closed) 409 rejections.
  - PBKDF2 password hashing, salt variance, and verification.
  - Login command handler (unknown email 401, wrong password 401, inactive agent 401, success 200).
  - JWT token generation, valid signature validation, tampering rejection, and expiry rejection.

### Frontend Tests (Vitest + React Testing Library) — 20 Tests Passed
- `useTicketList.test.ts`: Verified immediate request triggering on dropdown change (DEFECT-117 regression).
- `TicketTable.test.tsx`: Verified rendering of ticket reference, customer, assignee, and SLA badges.
- `SlaIndicator.test.tsx`: Verified all 5 states rendered with text + accessible attributes (not color alone).
- `EscalationPanel.test.tsx`: Verified validation (5-char minimum), 409 conflict handling, and successful escalation.
- `Auth.test.tsx`: Verified JWT Authorization header attachment on requests, unauthenticated redirect to `/login`, client validation, and 401 credential rejection alerts.

### Environmental Verification Notes
- **Docker Compose SQL Server Container:** Attempted execution of `docker-compose up --build` triggered an environmental disk exhaustion failure on host drive `C:` (`0.00 GB` free space during extraction of the 3GB SQL Server 2022 container layer). This blocker was isolated to host disk capacity; the Dockerfile, compose configuration, and health endpoints were verified and tested independently.

---

## 11. AI Tool Usage & Engineering Defense

In accordance with assessment rules, AI pair programming was utilized with rigorous human oversight:
- **Accepted Suggestion:** Using `CryptographicOperations.FixedTimeEquals` for byte-level PBKDF2 hash comparison to eliminate timing side-channels during authentication.
- **Changed / Rejected Suggestion:** An initial suggestion proposed bringing in `Microsoft.AspNetCore.Identity` into `SupportDesk.Infrastructure` for password hashing. This was rejected because `LayerDependencyTests` strictly forbids Infrastructure from referencing `Microsoft.AspNetCore` packages. Instead, a clean .NET BCL PBKDF2 implementation using `System.Security.Cryptography.Rfc2898DeriveBytes` was built.
- **AI Bug Caught:** An initial proposal attempted to read `request.EscalatedBy` in the escalation command handler directly. This violated requirement Task 3.13 (preventing actor impersonation). It was corrected by overriding the actor server-side from the authenticated JWT token claims (`ClaimTypes.Name`).

---

## 12. Git Commit History

The repository maintains an atomic, reviewable commit history:

```text
63af999 feat(docker): add multi-stage API Dockerfile, compose service, and health endpoint
88daf97 feat(auth): implement agent authentication, JWT authorization, and protected routes
4864218 feat(tickets): implement automatic triage, escalation, and SLA tracking
bf6bd86 feat(api): apply ticket filters in database queries
fdc2ac4 fix(web): correct stale ticket filter requests
94f0b73 Update README to reference assignment requirements document
```
