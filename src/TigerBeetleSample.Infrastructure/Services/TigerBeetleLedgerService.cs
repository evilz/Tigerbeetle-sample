using TigerBeetle;
using TigerBeetleSample.Domain.Interfaces;

namespace TigerBeetleSample.Infrastructure.Services;

public sealed class TigerBeetleLedgerService : ILedgerService
{
    // TigerBeetle enforces a maximum batch size per request
    private const int MaxBatchSize = 8000;

    private readonly Client _client;

    public TigerBeetleLedgerService(Client client)
    {
        _client = client;
    }

    public async Task<Guid> CreateAccountAsync(
        string name,
        uint ledger = 1,
        ushort code = 1,
        CancellationToken cancellationToken = default)
    {
        var id = ID.Create();

        var account = new Account
        {
            Id = id,
            Ledger = ledger,
            Code = code,
            Flags = AccountFlags.None,
        };

        var result = await _client.CreateAccountAsync(account);

        if (result != CreateAccountResult.Ok)
        {
            throw new InvalidOperationException(
                $"Failed to create TigerBeetle account: {result}");
        }

        return id.ToGuid();
    }

    public async Task<Guid> CreateTransferAsync(
        Guid debitAccountId,
        Guid creditAccountId,
        ulong amount,
        uint ledger = 1,
        ushort code = 1,
        CancellationToken cancellationToken = default)
    {
        var id = ID.Create();

        var transfer = new Transfer
        {
            Id = id,
            DebitAccountId = debitAccountId.ToUInt128(),
            CreditAccountId = creditAccountId.ToUInt128(),
            Amount = amount,
            Ledger = ledger,
            Code = code,
            Flags = TransferFlags.None,
        };

        var result = await _client.CreateTransferAsync(transfer);

        if (result != CreateTransferResult.Ok)
        {
            throw new InvalidOperationException(
                $"Failed to create TigerBeetle transfer: {result}");
        }

        return id.ToGuid();
    }

    public async Task<(ulong CreditsPosted, ulong DebitsPosted)> GetBalanceAsync(
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        var account = await _client.LookupAccountAsync(accountId.ToUInt128());

        if (account is null)
        {
            return (0ul, 0ul);
        }

        return ((ulong)account.Value.CreditsPosted, (ulong)account.Value.DebitsPosted);
    }

    public async Task<IReadOnlyList<Guid>> CreateAccountsBatchAsync(
        int count,
        uint ledger = 1,
        ushort code = 1,
        CancellationToken cancellationToken = default)
    {
        var ids = new List<Guid>(count);

        for (int offset = 0; offset < count; offset += MaxBatchSize)
        {
            var batchSize = Math.Min(MaxBatchSize, count - offset);
            var accounts = new Account[batchSize];
            var batchIds = new UInt128[batchSize];

            for (int i = 0; i < batchSize; i++)
            {
                var id = ID.Create();
                batchIds[i] = id;
                accounts[i] = new Account
                {
                    Id = id,
                    Ledger = ledger,
                    Code = code,
                    Flags = AccountFlags.None,
                };
            }

            var results = await _client.CreateAccountsAsync(accounts);

            if (results.Length > 0)
            {
                throw new InvalidOperationException(
                    $"Failed to create {results.Length} TigerBeetle account(s) in batch. " +
                    $"Batch offset {offset}, batch size {batchSize}. " +
                    $"First error at batch index {results[0].Index}: {results[0].Result}");
            }

            for (int i = 0; i < batchSize; i++)
            {
                ids.Add(batchIds[i].ToGuid());
            }
        }

        return ids;
    }

    public async Task<IReadOnlyList<Guid>> CreateTransfersBatchAsync(
        IReadOnlyList<(Guid DebitAccountId, Guid CreditAccountId, ulong Amount)> transfers,
        uint ledger = 1,
        ushort code = 1,
        CancellationToken cancellationToken = default)
    {
        var ids = new List<Guid>(transfers.Count);

        for (int offset = 0; offset < transfers.Count; offset += MaxBatchSize)
        {
            var batchSize = Math.Min(MaxBatchSize, transfers.Count - offset);
            var batch = new Transfer[batchSize];
            var batchIds = new UInt128[batchSize];

            for (int i = 0; i < batchSize; i++)
            {
                var t = transfers[offset + i];
                var id = ID.Create();
                batchIds[i] = id;
                batch[i] = new Transfer
                {
                    Id = id,
                    DebitAccountId = t.DebitAccountId.ToUInt128(),
                    CreditAccountId = t.CreditAccountId.ToUInt128(),
                    Amount = t.Amount,
                    Ledger = ledger,
                    Code = code,
                    Flags = TransferFlags.None,
                };
            }

            var results = await _client.CreateTransfersAsync(batch);

            if (results.Length > 0)
            {
                throw new InvalidOperationException(
                    $"Failed to create {results.Length} TigerBeetle transfer(s) in batch. " +
                    $"Batch offset {offset}, batch size {batchSize}. " +
                    $"First error at batch index {results[0].Index}: {results[0].Result}");
            }

            for (int i = 0; i < batchSize; i++)
            {
                ids.Add(batchIds[i].ToGuid());
            }
        }

        return ids;
    }
}
