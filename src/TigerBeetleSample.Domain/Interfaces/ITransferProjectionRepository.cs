using TigerBeetleSample.Domain.Entities;

namespace TigerBeetleSample.Domain.Interfaces;

public interface ITransferProjectionRepository
{
    Task AddAsync(TransferProjection transfer, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TransferProjection>> GetByAccountIdAsync(Guid accountId, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
