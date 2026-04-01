using TigerBeetleSample.Domain.Entities;

namespace TigerBeetleSample.Domain.Interfaces;

public interface IAccountProjectionRepository
{
    Task<IReadOnlyList<AccountProjection>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<AccountProjection?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task AddAsync(AccountProjection account, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
