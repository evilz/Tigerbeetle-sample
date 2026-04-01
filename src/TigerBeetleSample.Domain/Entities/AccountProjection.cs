namespace TigerBeetleSample.Domain.Entities;

public class AccountProjection
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public uint Ledger { get; set; } = 1;
    public ushort Code { get; set; } = 1;
    public DateTimeOffset CreatedAt { get; set; }
}
