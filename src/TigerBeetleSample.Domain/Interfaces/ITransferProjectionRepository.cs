using TigerBeetleSample.Domain.Entities;

namespace TigerBeetleSample.Domain.Interfaces;

public interface ITransferProjectionRepository
{
    Task<IReadOnlyList<TransferProjection>> GetByAccountIdAsync(Guid accountId, CancellationToken cancellationToken = default);
}
