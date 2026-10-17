using LearnSphere.Api.Contracts;
using LearnSphere.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace LearnSphere.Api.Persistence;

public sealed class GenerationStore(AppDbContext db)
{
    public Task<int> CountPendingAsync(CancellationToken ct) =>
        db.Generations.CountAsync(x => x.Status == GenerationStatus.Queued || x.Status == GenerationStatus.Processing, ct);

    public async Task AddAsync(Domain.Generation job, GenerationIntent intent, CancellationToken ct)
    {
        db.Generations.Add(job);
        db.GenerationIntents.Add(intent);
        await db.SaveChangesAsync(ct);
    }

    public Task<Domain.Generation?> FindAsync(Guid id, CancellationToken ct) =>
        db.Generations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);

    public Task<bool> ExistsAsync(Guid id, CancellationToken ct) =>
        db.Generations.AnyAsync(x => x.Id == id, ct);

    public Task<GenerationListItem[]> ListAsync(int limit, CancellationToken ct) =>
        db.Generations.AsNoTracking().OrderByDescending(x => x.CreatedAt).Take(limit)
            .Select(x => new GenerationListItem(x.Id, x.Title, x.Status, x.GenerationMode, x.CreatedAt)).ToArrayAsync(ct);

    public Task<int> DeleteCompletedAsync(Guid id, CancellationToken ct) =>
        db.Generations.Where(x => x.Id == id && x.Status != GenerationStatus.Processing).ExecuteDeleteAsync(ct);
}
