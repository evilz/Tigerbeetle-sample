namespace TigerBeetleSample.Domain.Interfaces;

public interface ILedgerService
{
    Task<Guid> CreateAccountAsync(string name, uint ledger = 1, ushort code = 1, CancellationToken cancellationToken = default);
    Task<Guid> CreateTransferAsync(Guid debitAccountId, Guid creditAccountId, ulong amount, uint ledger = 1, ushort code = 1, CancellationToken cancellationToken = default);
    Task<(ulong CreditsPosted, ulong DebitsPosted)> GetBalanceAsync(Guid accountId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates <paramref name="count"/> accounts in TigerBeetle using batch operations.
    /// PostgreSQL is not involved — intended for performance benchmarking.
    /// </summary>
    Task<IReadOnlyList<Guid>> CreateAccountsBatchAsync(int count, uint ledger = 1, ushort code = 1, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates transfers in TigerBeetle using batch operations.
    /// PostgreSQL is not involved — intended for performance benchmarking.
    /// </summary>
    Task<IReadOnlyList<Guid>> CreateTransfersBatchAsync(IReadOnlyList<(Guid DebitAccountId, Guid CreditAccountId, ulong Amount)> transfers, uint ledger = 1, ushort code = 1, CancellationToken cancellationToken = default);
}
