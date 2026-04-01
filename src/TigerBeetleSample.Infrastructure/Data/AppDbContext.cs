using Microsoft.EntityFrameworkCore;
using TigerBeetleSample.Domain.Entities;

namespace TigerBeetleSample.Infrastructure.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<AccountProjection> Accounts => Set<AccountProjection>();
    public DbSet<TransferProjection> Transfers => Set<TransferProjection>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AccountProjection>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
            entity.Property(e => e.CreatedAt).IsRequired();
        });

        modelBuilder.Entity<TransferProjection>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.CreatedAt).IsRequired();
            entity.HasIndex(e => e.DebitAccountId);
            entity.HasIndex(e => e.CreditAccountId);
        });
    }
}
