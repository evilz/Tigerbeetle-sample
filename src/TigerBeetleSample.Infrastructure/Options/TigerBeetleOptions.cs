namespace TigerBeetleSample.Infrastructure.Options;

public sealed class TigerBeetleOptions
{
    public const string SectionName = "TigerBeetle";

    public string Addresses { get; set; } = "localhost:3000";
    public uint ClusterId { get; set; } = 0;
}
