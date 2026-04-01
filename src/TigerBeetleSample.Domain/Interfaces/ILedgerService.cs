namespace TigerBeetleSample.Domain.Interfaces;

public interface ILedgerService
{
    Task<Guid> CreateAccountAsync(string name, uint ledger = 1, ushort code = 1, CancellationToken cancellationToken = default);
    Task<Guid> CreateTransferAsync(Guid debitAccountId, Guid creditAccountId, ulong amount, uint ledger = 1, ushort code = 1, CancellationToken cancellationToken = default);
    Task<(ulong CreditsPosted, ulong DebitsPosted)> GetBalanceAsync(Guid accountId, CancellationToken cancellationToken = default);
}
