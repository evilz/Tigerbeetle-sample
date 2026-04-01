using Microsoft.EntityFrameworkCore;
using TigerBeetleSample.Domain.Entities;
using TigerBeetleSample.Domain.Interfaces;
using TigerBeetleSample.Infrastructure.Data;

namespace TigerBeetleSample.Infrastructure.Repositories;

public sealed class AccountProjectionRepository : IAccountProjectionRepository
{
    private readonly AppDbContext _context;

    public AccountProjectionRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<AccountProjection>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Accounts
            .OrderBy(a => a.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<AccountProjection?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Accounts.FindAsync([id], cancellationToken);
    }

    public async Task AddAsync(AccountProjection account, CancellationToken cancellationToken = default)
    {
        await _context.Accounts.AddAsync(account, cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return _context.SaveChangesAsync(cancellationToken);
    }
}
