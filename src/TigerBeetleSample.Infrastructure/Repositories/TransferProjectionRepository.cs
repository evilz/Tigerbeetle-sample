using Marten;
using TigerBeetleSample.Domain.Entities;
using TigerBeetleSample.Domain.Interfaces;

namespace TigerBeetleSample.Infrastructure.Repositories;

public sealed class TransferProjectionRepository : ITransferProjectionRepository
{
    private readonly IQuerySession _session;

    public TransferProjectionRepository(IQuerySession session)
    {
        _session = session;
    }

    public async Task<IReadOnlyList<TransferProjection>> GetByAccountIdAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        return await _session.Query<TransferProjection>()
            .Where(t => t.DebitAccountId == accountId || t.CreditAccountId == accountId)
            .OrderBy(t => t.CreatedAt)
            .ToListAsync(cancellationToken);
    }
}
