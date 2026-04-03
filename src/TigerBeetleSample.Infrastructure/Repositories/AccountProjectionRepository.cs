using Marten;
using TigerBeetleSample.Domain.Entities;
using TigerBeetleSample.Domain.Interfaces;

namespace TigerBeetleSample.Infrastructure.Repositories;

public sealed class AccountProjectionRepository : IAccountProjectionRepository
{
    private readonly IQuerySession _session;

    public AccountProjectionRepository(IQuerySession session)
    {
        _session = session;
    }

    public async Task<IReadOnlyList<AccountProjection>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _session.Query<AccountProjection>()
            .OrderBy(a => a.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<AccountProjection?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _session.LoadAsync<AccountProjection>(id, cancellationToken);
    }
}
