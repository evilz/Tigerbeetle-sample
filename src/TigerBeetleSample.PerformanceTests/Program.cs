using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

// ---------------------------------------------------------------------------
// TigerBeetle Load Test Runner
//
// Usage:
//   dotnet run -- [baseUrl] [accountCount] [transferCount] [concurrency]
//
// Examples:
//   dotnet run -- http://localhost:5000 1000 5000 50
//   dotnet run                          (uses defaults below)
//
// Runs a load test against the real production API endpoints:
//   POST /accounts  — creates accounts (writes to TigerBeetle + PostgreSQL)
//   POST /transfers — creates transfers (writes to TigerBeetle + PostgreSQL)
//
// The results show whether PostgreSQL or TigerBeetle is the bottleneck.
// ---------------------------------------------------------------------------

var baseUrl = "http://localhost:5253";
var accountCount = 1_000;
var transferCount = 100_000;
var concurrency = 50;

if (args.Length > 0 && !string.IsNullOrWhiteSpace(args[0]))
    baseUrl = args[0].TrimEnd('/');

if (args.Length > 1)
{
    if (!int.TryParse(args[1], out accountCount) || accountCount <= 0)
    {
        Console.Error.WriteLine("ERROR: Invalid accountCount. Expected a positive integer.");
        PrintUsage();
        return 1;
    }
}

if (args.Length > 2)
{
    if (!int.TryParse(args[2], out transferCount) || transferCount <= 0)
    {
        Console.Error.WriteLine("ERROR: Invalid transferCount. Expected a positive integer.");
        PrintUsage();
        return 1;
    }
}

if (args.Length > 3)
{
    if (!int.TryParse(args[3], out concurrency) || concurrency <= 0)
    {
        Console.Error.WriteLine("ERROR: Invalid concurrency. Expected a positive integer.");
        PrintUsage();
        return 1;
    }
}

Console.WriteLine("═══════════════════════════════════════════════════════════════");
Console.WriteLine("  TigerBeetle Load Test (real API — includes PostgreSQL)");
Console.WriteLine("═══════════════════════════════════════════════════════════════");
Console.WriteLine($"  API URL        : {baseUrl}");
Console.WriteLine($"  Account count  : {accountCount:N0}");
Console.WriteLine($"  Transfer count : {transferCount:N0}");
Console.WriteLine($"  Concurrency    : {concurrency}");
Console.WriteLine("═══════════════════════════════════════════════════════════════");
Console.WriteLine();

var jsonOptions = new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true,
    Converters = { new JsonStringEnumConverter() },
};

// Unique prefix per run so multiple test runs do not produce duplicate account names
var runId = Guid.NewGuid().ToString("N")[..8];

// Shared HttpClient — thread-safe; reuse connections across concurrent requests
using var http = new HttpClient
{
    BaseAddress = new Uri(baseUrl),
    Timeout = TimeSpan.FromSeconds(60),
};

// ── Step 1: Account creation ─────────────────────────────────────────────────
Console.WriteLine("▶ Step 1 — Account creation via POST /accounts (TigerBeetle + PostgreSQL)");
Console.WriteLine($"  Sending {accountCount:N0} requests with concurrency={concurrency} …");

var accountResults = new ConcurrentBag<RequestResult>();
var accountSw = Stopwatch.StartNew();

await Parallel.ForEachAsync(
    Enumerable.Range(0, accountCount),
    new ParallelOptions { MaxDegreeOfParallelism = concurrency },
    async (i, ct) =>
    {
        var reqSw = Stopwatch.StartNew();
        try
        {
            var body = new { Name = $"perf-{runId}-{i}", Ledger = 1, Code = 1 };
            using var resp = await http.PostAsJsonAsync("/accounts", body, jsonOptions, ct);
            reqSw.Stop();
            accountResults.Add(new RequestResult(resp.IsSuccessStatusCode, reqSw.ElapsedMilliseconds, null));
        }
        catch (Exception ex)
        {
            reqSw.Stop();
            accountResults.Add(new RequestResult(false, reqSw.ElapsedMilliseconds, ex.Message));
        }
    });

accountSw.Stop();
PrintPhaseResult("Account creation", accountResults, accountSw);

// Retrieve the first two accounts created for the transfer phase
Console.Write("  Fetching account list for transfer phase … ");
var allAccounts = await FetchAccountsAsync(http, jsonOptions);
Console.WriteLine(allAccounts is null ? "FAILED" : $"{allAccounts.Count:N0} found");

if (allAccounts is null || allAccounts.Count < 2)
{
    Console.WriteLine("  Cannot continue — need at least 2 accounts in the system.");
    return 1;
}

var debitId = allAccounts[0].Id;
var creditId = allAccounts[1].Id;

// ── Step 2: Transfer creation ─────────────────────────────────────────────────
Console.WriteLine();
Console.WriteLine("▶ Step 2 — Transfer creation via POST /transfers (TigerBeetle + PostgreSQL)");
Console.WriteLine($"  Sending {transferCount:N0} requests with concurrency={concurrency} …");

var transferResults = new ConcurrentBag<RequestResult>();
var transferSw = Stopwatch.StartNew();

// Build the body once — debitId/creditId/amount are constant for every request
var transferBody = new { DebitAccountId = debitId, CreditAccountId = creditId, Amount = (ulong)1 };

await Parallel.ForEachAsync(
    Enumerable.Range(0, transferCount),
    new ParallelOptions { MaxDegreeOfParallelism = concurrency },
    async (_, ct) =>
    {
        var reqSw = Stopwatch.StartNew();
        try
        {
            using var resp = await http.PostAsJsonAsync("/transfers", transferBody, jsonOptions, ct);
            reqSw.Stop();
            transferResults.Add(new RequestResult(resp.IsSuccessStatusCode, reqSw.ElapsedMilliseconds, null));
        }
        catch (Exception ex)
        {
            reqSw.Stop();
            transferResults.Add(new RequestResult(false, reqSw.ElapsedMilliseconds, ex.Message));
        }
    });

transferSw.Stop();
PrintPhaseResult("Transfer creation", transferResults, transferSw);

// ── Summary ───────────────────────────────────────────────────────────────────
Console.WriteLine();
Console.WriteLine("═══════════════════════════════════════════════════════════════");
Console.WriteLine("  Summary");
Console.WriteLine("═══════════════════════════════════════════════════════════════");
PrintSummaryRow("Accounts", accountResults, accountSw);
PrintSummaryRow("Transfers", transferResults, transferSw);
Console.WriteLine("═══════════════════════════════════════════════════════════════");
Console.WriteLine();
Console.WriteLine("Both TigerBeetle and PostgreSQL were exercised for each request.");
Console.WriteLine("High error rates or low throughput indicate a bottleneck in one");
Console.WriteLine("of those layers. Compare with TigerBeetle-only timings to isolate.");
Console.WriteLine();

return 0;

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

static void PrintPhaseResult(string label, ConcurrentBag<RequestResult> results, Stopwatch sw)
{
    var total = results.Count;
    var succeeded = results.Count(r => r.Success);
    var failed = total - succeeded;
    var latencies = results.Select(r => r.LatencyMs).OrderBy(x => x).ToArray();
    var p50 = Percentile(latencies, 50);
    var p95 = Percentile(latencies, 95);
    var p99 = Percentile(latencies, 99);
    var throughput = total / Math.Max(sw.Elapsed.TotalSeconds, 0.001);

    Console.WriteLine($"  ┌─ {label}");
    Console.WriteLine($"  │  Total requests   : {total:N0}");
    Console.WriteLine($"  │  Succeeded        : {succeeded:N0}");
    Console.WriteLine($"  │  Failed           : {failed:N0}");
    Console.WriteLine($"  │  Total elapsed    : {sw.ElapsedMilliseconds:N0} ms");
    Console.WriteLine($"  │  Throughput       : {throughput:N0} req/sec");
    Console.WriteLine($"  │  Latency p50      : {p50:N0} ms");
    Console.WriteLine($"  │  Latency p95      : {p95:N0} ms");
    Console.WriteLine($"  └─ Latency p99      : {p99:N0} ms");

    if (failed > 0)
    {
        var sample = results.Where(r => !r.Success && r.Error != null)
                            .Take(3)
                            .Select(r => r.Error!)
                            .Distinct();
        Console.WriteLine($"  ⚠ Sample errors: {string.Join(" | ", sample)}");
    }
}

static void PrintSummaryRow(string label, ConcurrentBag<RequestResult> results, Stopwatch sw)
{
    var total = results.Count;
    var failed = results.Count(r => !r.Success);
    var throughput = (long)(total / Math.Max(sw.Elapsed.TotalSeconds, 0.001));
    Console.WriteLine($"  {label,-12}  {total,8:N0} req  {sw.ElapsedMilliseconds,8:N0} ms  {throughput,10:N0} req/sec  {failed,6:N0} failed");
}

static long Percentile(long[] sortedValues, int percentile)
{
    if (sortedValues.Length == 0) return 0;
    var index = (int)Math.Round(percentile / 100.0 * (sortedValues.Length - 1));
    return sortedValues[Math.Clamp(index, 0, sortedValues.Length - 1)];
}

static async Task<List<AccountSummary>?> FetchAccountsAsync(HttpClient http, JsonSerializerOptions opts)
{
    try
    {
        return await http.GetFromJsonAsync<List<AccountSummary>>("/accounts", opts);
    }
    catch
    {
        return null;
    }
}

static void PrintUsage()
{
    Console.Error.WriteLine("Usage: dotnet run -- [baseUrl] [accountCount] [transferCount] [concurrency]");
    Console.Error.WriteLine("       Defaults: baseUrl=http://localhost:5000 accountCount=1000 transferCount=5000 concurrency=50");
}

// ---------------------------------------------------------------------------
// DTOs
// ---------------------------------------------------------------------------

internal sealed record RequestResult(bool Success, long LatencyMs, string? Error);

internal sealed record AccountSummary(Guid Id, string Name);

