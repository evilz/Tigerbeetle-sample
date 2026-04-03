using System.Diagnostics.Metrics;

namespace TigerBeetleSample.Api.Metrics;

public static class LedgerMetrics
{
    public const string MeterName = "TigerBeetleSample.Api";

    private static readonly Meter Meter = new(MeterName);

    public static readonly Counter<long> AccountsCreated = Meter.CreateCounter<long>(
        name: "tigerbeetle.accounts.created",
        unit: "{account}",
        description: "Number of accounts successfully created.");

    public static readonly Counter<long> TransfersCreated = Meter.CreateCounter<long>(
        name: "tigerbeetle.transfers.created",
        unit: "{transfer}",
        description: "Number of transfers successfully created.");
}

