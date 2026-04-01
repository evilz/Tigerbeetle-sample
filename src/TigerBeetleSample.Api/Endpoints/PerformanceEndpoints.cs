using System.Diagnostics;
using TigerBeetleSample.Domain.Interfaces;

namespace TigerBeetleSample.Api.Endpoints;

public static class PerformanceEndpoints
{
    public static IEndpointRouteBuilder MapPerformanceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/perf").WithTags("Performance");

        group.MapPost("/accounts/batch", CreateAccountsBatchAsync)
            .WithName("PerfCreateAccountsBatch")
            .WithSummary("Benchmark: create N accounts directly in TigerBeetle (PostgreSQL bypassed)");

        group.MapPost("/transfers/batch", CreateTransfersBatchAsync)
            .WithName("PerfCreateTransfersBatch")
            .WithSummary("Benchmark: create N transfers directly in TigerBeetle (PostgreSQL bypassed)");

        return app;
    }

    private static async Task<IResult> CreateAccountsBatchAsync(
        PerfBatchAccountRequest request,
        ILedgerService ledgerService,
        CancellationToken cancellationToken)
    {
        if (request.Count is <= 0 or > 1_000_000)
            return Results.BadRequest("Count must be between 1 and 1,000,000.");

        var sw = Stopwatch.StartNew();
        var ids = await ledgerService.CreateAccountsBatchAsync(
            request.Count, request.Ledger, request.Code, cancellationToken);
        sw.Stop();

        return Results.Ok(ToBatchResult(request.Count, sw, ids));
    }

    private static async Task<IResult> CreateTransfersBatchAsync(
        PerfBatchTransferRequest request,
        ILedgerService ledgerService,
        CancellationToken cancellationToken)
    {
        if (request.Count is <= 0 or > 1_000_000)
            return Results.BadRequest("Count must be between 1 and 1,000,000.");

        var transfers = Enumerable
            .Range(0, request.Count)
            .Select(_ => (request.DebitAccountId, request.CreditAccountId, request.Amount))
            .ToList();

        var sw = Stopwatch.StartNew();
        var ids = await ledgerService.CreateTransfersBatchAsync(
            transfers, request.Ledger, request.Code, cancellationToken);
        sw.Stop();

        return Results.Ok(ToBatchResult(request.Count, sw, ids));
    }

    private static PerfBatchResult ToBatchResult(int count, Stopwatch sw, IReadOnlyList<Guid> ids)
    {
        // Use at least 1 ms to avoid division by zero for extremely fast operations
        var elapsedSeconds = Math.Max(sw.Elapsed.TotalSeconds, 0.001);
        var throughput = (long)(count / elapsedSeconds);
        return new PerfBatchResult(count, sw.ElapsedMilliseconds, throughput, ids);
    }
}

/// <summary>Request to batch-create accounts directly in TigerBeetle for benchmarking.</summary>
/// <param name="Count">Number of accounts to create (1–1,000,000).</param>
/// <param name="Ledger">Ledger ID (default 1).</param>
/// <param name="Code">Account type code (default 1).</param>
public record PerfBatchAccountRequest(int Count, uint Ledger = 1, ushort Code = 1);

/// <summary>Request to batch-create transfers directly in TigerBeetle for benchmarking.</summary>
/// <param name="DebitAccountId">Account to debit.</param>
/// <param name="CreditAccountId">Account to credit.</param>
/// <param name="Amount">Transfer amount.</param>
/// <param name="Count">Number of transfers to create (1–1,000,000).</param>
/// <param name="Ledger">Ledger ID (default 1).</param>
/// <param name="Code">Transfer type code (default 1).</param>
public record PerfBatchTransferRequest(
    Guid DebitAccountId,
    Guid CreditAccountId,
    ulong Amount,
    int Count,
    uint Ledger = 1,
    ushort Code = 1);

/// <summary>Benchmark result showing throughput for a batch operation.</summary>
/// <param name="Count">Number of items created.</param>
/// <param name="ElapsedMs">Total elapsed time in milliseconds.</param>
/// <param name="ThroughputPerSecond">Items created per second.</param>
/// <param name="Ids">IDs of the created items.</param>
public record PerfBatchResult(
    int Count,
    long ElapsedMs,
    long ThroughputPerSecond,
    IReadOnlyList<Guid> Ids);
