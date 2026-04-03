using System.Text.Json.Serialization;

namespace TigerBeetleSample.Infrastructure.Cdc;

/// <summary>
/// Represents the JSON message body published by TigerBeetle's native CDC job
/// (<c>tigerbeetle amqp</c>). Only the fields needed for building projections are included.
/// Per the TigerBeetle CDC spec, <c>u128</c> and <c>u64</c> values are encoded as JSON strings
/// for large values but may arrive as JSON numbers for small values; both are handled.
/// </summary>
public sealed record TigerBeetleCdcMessage
{
    [JsonPropertyName("timestamp")]
    [JsonConverter(typeof(UInt64JsonConverter))]
    public ulong Timestamp { get; init; }

    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonPropertyName("ledger")]
    public uint Ledger { get; init; }

    [JsonPropertyName("transfer")]
    public required TigerBeetleCdcTransfer Transfer { get; init; }

    [JsonPropertyName("debit_account")]
    public required TigerBeetleCdcAccount DebitAccount { get; init; }

    [JsonPropertyName("credit_account")]
    public required TigerBeetleCdcAccount CreditAccount { get; init; }
}

public sealed record TigerBeetleCdcTransfer
{
    [JsonPropertyName("id")]
    [JsonConverter(typeof(UInt128JsonConverter))]
    public System.UInt128 Id { get; init; }

    [JsonPropertyName("amount")]
    [JsonConverter(typeof(UInt128JsonConverter))]
    public System.UInt128 Amount { get; init; }

    [JsonPropertyName("code")]
    public ushort Code { get; init; }

    [JsonPropertyName("timestamp")]
    [JsonConverter(typeof(UInt64JsonConverter))]
    public ulong Timestamp { get; init; }
}

public sealed record TigerBeetleCdcAccount
{
    [JsonPropertyName("id")]
    [JsonConverter(typeof(UInt128JsonConverter))]
    public System.UInt128 Id { get; init; }
}
