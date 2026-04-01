using Microsoft.EntityFrameworkCore;
using TigerBeetleSample.Domain.Entities;
using TigerBeetleSample.Domain.Interfaces;
using TigerBeetleSample.Infrastructure.Data;

namespace TigerBeetleSample.Infrastructure.Repositories;

public sealed class TransferProjectionRepository : ITransferProjectionRepository
{
    private readonly AppDbContext _context;

    public TransferProjectionRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task AddAsync(TransferProjection transfer, CancellationToken cancellationToken = default)
    {
        await _context.Transfers.AddAsync(transfer, cancellationToken);
    }

    public async Task<IReadOnlyList<TransferProjection>> GetByAccountIdAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        return await _context.Transfers
            .Where(t => t.DebitAccountId == accountId || t.CreditAccountId == accountId)
            .OrderBy(t => t.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return _context.SaveChangesAsync(cancellationToken);
    }
}
