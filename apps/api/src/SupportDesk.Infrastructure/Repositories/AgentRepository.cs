using Microsoft.EntityFrameworkCore;
using SupportDesk.Domain.Aggregates.Agents;
using SupportDesk.Domain.Repositories;
using SupportDesk.Infrastructure.Data;

namespace SupportDesk.Infrastructure.Repositories;

public sealed class AgentRepository(SupportDbContext db) : IAgentRepository
{
    /// <summary>Loads the whole aggregate, specializations included.</summary>
    public Task<Agent?> GetByIdAsync(int id, CancellationToken ct) =>
        db.Set<Agent>()
            .Include(a => a.Specializations)
            .FirstOrDefaultAsync(a => a.Id == id, ct);

    public Task<Agent?> GetByEmailAsync(string email, CancellationToken ct) =>
        db.Set<Agent>()
            .Include(a => a.Specializations)
            .FirstOrDefaultAsync(a => a.Email == email, ct);

    public async Task<IReadOnlyList<Agent>> GetAllActiveWithSpecializationsAsync(CancellationToken ct) =>
        await db.Set<Agent>()
            .Include(a => a.Specializations)
            .Where(a => a.IsActive)
            .OrderBy(a => a.Id)
            .ToListAsync(ct);
}
