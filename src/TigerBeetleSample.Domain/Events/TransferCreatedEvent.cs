namespace TigerBeetleSample.Domain.Events;

public record TransferCreatedEvent(
    Guid TransferId,
    Guid DebitAccountId,
    Guid CreditAccountId,
    ulong Amount,
    uint Ledger,
    ushort Code,
    DateTimeOffset CreatedAt);
