namespace TigerBeetleSample.Domain.Events;

public record AccountCreatedEvent(
    Guid AccountId,
    string Name,
    uint Ledger,
    ushort Code,
    DateTimeOffset CreatedAt);
