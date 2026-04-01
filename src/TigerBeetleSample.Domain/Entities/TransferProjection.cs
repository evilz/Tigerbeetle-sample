namespace TigerBeetleSample.Domain.Entities;

public class TransferProjection
{
    public Guid Id { get; set; }
    public Guid DebitAccountId { get; set; }
    public Guid CreditAccountId { get; set; }
    public ulong Amount { get; set; }
    public uint Ledger { get; set; } = 1;
    public ushort Code { get; set; } = 1;
    public DateTimeOffset CreatedAt { get; set; }
}
