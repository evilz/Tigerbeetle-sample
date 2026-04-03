namespace TigerBeetleSample.Infrastructure.Options;

public sealed class TigerBeetleOptions
{
    public const string SectionName = "TigerBeetle";

    public string Addresses { get; set; } = "127.0.0.1:3000";
    public uint ClusterId { get; set; } = 0;
}
