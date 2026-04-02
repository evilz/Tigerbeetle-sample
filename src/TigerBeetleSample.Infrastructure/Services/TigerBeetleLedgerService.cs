using TigerBeetle;
using TigerBeetleSample.Domain.Interfaces;

namespace TigerBeetleSample.Infrastructure.Services;

public sealed class TigerBeetleLedgerService : ILedgerService
{
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
}
