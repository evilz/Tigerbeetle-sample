using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

// ---------------------------------------------------------------------------
// TigerBeetle Performance Test Suite
//
// Usage:
//   dotnet run -- [baseUrl] [accountCount] [transferCount] [concurrency] [batchSize]
//
// Examples:
//   dotnet run -- http://localhost:5253 200 10000 50 500
//   dotnet run                          (uses defaults below)
//
// Scenarios:
//   1. Baseline    — 1-by-1 creates via POST /accounts + POST /transfers
//                    (TigerBeetle + PostgreSQL) — establishes the bottleneck baseline.
//   2. Batch       — Bulk creates via POST /perf/accounts/batch + POST /perf/transfers/batch
//                    (TigerBeetle-only) — shows the throughput gain from batching.
//   3. Read+Write  — Writes (POST /transfers) and reads (GET /accounts/{id}) run
//                    simultaneously on the SAME accounts — validates consistency under load.
//   4. Fan-in      — Many source accounts all transfer concurrently into a single
//                    destination account — stresses a hot account.
// ---------------------------------------------------------------------------

var baseUrl = "http://localhost:5253";
var accountCount = 200;
var transferCount = 10_000;
var concurrency = 50;
var batchSize = 500;

if (args.Length > 0 && !string.IsNullOrWhiteSpace(args[0])) baseUrl = args[0].TrimEnd('/');
if (args.Length > 1 && int.TryParse(args[1], out var ac) && ac > 0) accountCount = ac;
if (args.Length > 2 && int.TryParse(args[2], out var tc) && tc > 0) transferCount = tc;
if (args.Length > 3 && int.TryParse(args[3], out var cx) && cx > 0) concurrency = cx;
if (args.Length > 4 && int.TryParse(args[4], out var bs) && bs > 0) batchSize = bs;

Console.WriteLine("═══════════════════════════════════════════════════════════════");
Console.WriteLine("  TigerBeetle Performance Test Suite");
Console.WriteLine("═══════════════════════════════════════════════════════════════");
Console.WriteLine($"  API URL        : {baseUrl}");
Console.WriteLine($"  Account count  : {accountCount:N0}");
Console.WriteLine($"  Transfer count : {transferCount:N0}");
Console.WriteLine($"  Concurrency    : {concurrency}");
Console.WriteLine($"  Batch size     : {batchSize:N0}");
Console.WriteLine("═══════════════════════════════════════════════════════════════");
Console.WriteLine();

var jsonOptions = new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true,
    Converters = { new JsonStringEnumConverter() },
};

using var http = new HttpClient
{
    BaseAddress = new Uri(baseUrl),
    Timeout = TimeSpan.FromSeconds(120),
};

var overallSw = Stopwatch.StartNew();

// ── Scenario 1: Baseline (1-by-1 via full stack: TigerBeetle + PostgreSQL) ──
PrintScenarioHeader(1, "Baseline — 1-by-1 creates (TigerBeetle + PostgreSQL)");
Console.WriteLine("  ℹ Each request writes to both TigerBeetle and PostgreSQL.");
Console.WriteLine("    High latency here indicates PostgreSQL is the bottleneck.");
Console.WriteLine();

var runId = Guid.NewGuid().ToString("N")[..8];

Console.WriteLine($"  Creating {accountCount:N0} accounts one-by-one (concurrency={concurrency}) …");
var (acctResults, acctSw) = await RunParallelRequestsAsync(
    http, jsonOptions, "/accounts",
    i => (object)new { Name = $"baseline-{runId}-{i}", Ledger = 1, Code = 1 },
    accountCount, concurrency);
PrintPhaseResult("Account creation (baseline)", acctResults, acctSw);

var allAccounts = await FetchAccountsAsync(http, jsonOptions);
if (allAccounts is null || allAccounts.Count < 2)
{
    Console.WriteLine("  ✗ Cannot continue — need at least 2 accounts in the system.");
    return 1;
}

var debitId = allAccounts[0].Id;
var creditId = allAccounts[1].Id;

Console.WriteLine($"  Creating {transferCount:N0} transfers one-by-one (concurrency={concurrency}) …");
Console.WriteLine($"    Debit account : {debitId}");
Console.WriteLine($"    Credit account: {creditId}");
var (xferResults, xferSw) = await RunParallelRequestsAsync(
    http, jsonOptions, "/transfers",
    _ => (object)new { DebitAccountId = debitId, CreditAccountId = creditId, Amount = 1UL },
    transferCount, concurrency);
PrintPhaseResult("Transfer creation (baseline)", xferResults, xferSw);

// ── Scenario 2: Batch API (TigerBeetle-only, no PostgreSQL) ─────────────────
PrintScenarioHeader(2, "Batch — bulk creates via /perf endpoints (TigerBeetle-only, no PostgreSQL)");
Console.WriteLine("  ℹ Each HTTP request creates an entire batch in one TigerBeetle call.");
Console.WriteLine("    Compare throughput with Scenario 1 to see the PostgreSQL overhead.");
Console.WriteLine();

Console.WriteLine($"  Creating {accountCount:N0} accounts in batches of {batchSize} …");
var batchAccountSw = Stopwatch.StartNew();
var batchIds = await CreateAccountsBatchAsync(http, jsonOptions, accountCount, batchSize);
batchAccountSw.Stop();

if (batchIds is null || batchIds.Count < 2)
{
    Console.WriteLine("  ✗ Batch account creation failed — skipping Scenario 2.");
}
else
{
    var batchCount = (int)Math.Ceiling((double)accountCount / batchSize);
    Console.WriteLine($"  ✓ Created {batchIds.Count:N0} accounts in {batchCount} batch request(s) in {batchAccountSw.ElapsedMilliseconds:N0} ms");

    var batchDebitId = batchIds[0];
    var batchCreditId = batchIds[1];

    Console.WriteLine($"  Creating {transferCount:N0} transfers in batches of {batchSize} (concurrency={concurrency}) …");
    Console.WriteLine($"    Debit account : {batchDebitId}");
    Console.WriteLine($"    Credit account: {batchCreditId}");
    var (batchXferResults, batchXferSw, totalBatchXfers) = await RunBatchTransfersAsync(
        http, jsonOptions, batchDebitId, batchCreditId, transferCount, batchSize, concurrency);
    PrintBatchPhaseResult("Transfer creation (batch)", batchXferResults, batchXferSw, totalBatchXfers, batchSize);
}

// ── Scenario 3: Concurrent Read+Write on Same Accounts ───────────────────────
PrintScenarioHeader(3, "Concurrent Read+Write — reads and writes simultaneously on the same accounts");
Console.WriteLine("  ℹ Writer: POST /transfers at full concurrency.");
Console.WriteLine("    Reader: GET /accounts/{id} polling both accounts concurrently.");
Console.WriteLine("    Validates TigerBeetle's consistent balance reads under write load.");
Console.WriteLine();

var rwIds = await CreateAccountsBatchAsync(http, jsonOptions, 2, 2);
if (rwIds is null || rwIds.Count < 2)
{
    Console.WriteLine("  ✗ Failed to create accounts — skipping Scenario 3.");
}
else
{
    var rwDebitId = rwIds[0];
    var rwCreditId = rwIds[1];

    Console.WriteLine($"  Running {transferCount:N0} writes (concurrency={concurrency}) + continuous reads …");
    Console.WriteLine($"    Debit account : {rwDebitId}");
    Console.WriteLine($"    Credit account: {rwCreditId}");

    var writeResults = new ConcurrentBag<RequestResult>();
    var readResults = new ConcurrentBag<RequestResult>();
    var rwSw = Stopwatch.StartNew();

    // Writer: sends all transfers concurrently
    var writeTask = RunParallelRequestsIntoAsync(
        http, jsonOptions, "/transfers",
        _ => (object)new { DebitAccountId = rwDebitId, CreditAccountId = rwCreditId, Amount = 1UL },
        transferCount, concurrency, writeResults);

    // Reader: alternates reading both accounts until writes finish
    using var readCts = new CancellationTokenSource();
    var readTask = Task.Run(async () =>
    {
        var accounts = new[] { rwDebitId, rwCreditId };
        var index = 0;
        while (!readCts.Token.IsCancellationRequested)
        {
            var id = accounts[index % accounts.Length];
            index++;
            var reqSw = Stopwatch.StartNew();
            try
            {
                using var resp = await http.GetAsync($"/accounts/{id}", readCts.Token);
                reqSw.Stop();
                readResults.Add(new RequestResult(resp.IsSuccessStatusCode, reqSw.ElapsedMilliseconds, null));
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                reqSw.Stop();
                readResults.Add(new RequestResult(false, reqSw.ElapsedMilliseconds, ex.Message));
            }
        }
    });

    await writeTask;
    await readCts.CancelAsync();
    try { await readTask; } catch (OperationCanceledException) { }
    rwSw.Stop();

    PrintPhaseResult("Writes (concurrent with reads)", writeResults, rwSw);
    PrintPhaseResult("Reads (concurrent with writes)", readResults, rwSw);

    // Balance consistency check
    Console.WriteLine();
    Console.Write("  Balance consistency check … ");
    var balance = await FetchAccountBalanceAsync(http, jsonOptions, rwDebitId);
    var expectedDebits = (ulong)writeResults.Count(r => r.Success);
    if (balance is null)
    {
        Console.WriteLine("FAILED — could not fetch balance.");
    }
    else if (balance.DebitsPosted == expectedDebits)
    {
        Console.WriteLine($"✓ PASS — debits posted = {balance.DebitsPosted:N0} (matches {expectedDebits:N0} successful writes)");
    }
    else
    {
        Console.WriteLine($"⚠ MISMATCH — debits posted = {balance.DebitsPosted:N0}, expected {expectedDebits:N0}");
        Console.WriteLine("    This may indicate write failures or in-flight transfers at read time.");
    }
}

// ── Scenario 4: Fan-in (many sources → single destination) ──────────────────
PrintScenarioHeader(4, "Fan-in — many source accounts all transfer concurrently into a single destination");
Console.WriteLine("  ℹ Tests TigerBeetle's ability to serialize concurrent updates to a hot account.");
Console.WriteLine("    All source accounts transfer simultaneously to the same destination.");
Console.WriteLine();

var fanInSourceCount = Math.Min(accountCount, 500);
var fanInIds = await CreateAccountsBatchAsync(
    http, jsonOptions, fanInSourceCount + 1, Math.Min(batchSize, fanInSourceCount + 1));

if (fanInIds is null || fanInIds.Count < fanInSourceCount + 1)
{
    Console.WriteLine("  ✗ Failed to create accounts — skipping Scenario 4.");
}
else
{
    var destinationId = fanInIds[0];
    var sourceIds = fanInIds.Skip(1).Take(fanInSourceCount).ToList();
    const ulong fanInAmount = 100UL;

    Console.WriteLine($"  {sourceIds.Count:N0} sources each send {fanInAmount} to 1 destination (concurrency={concurrency}) …");
    Console.WriteLine($"    Destination account: {destinationId}");

    var (fanInResults, fanInSw) = await RunParallelRequestsAsync(
        http, jsonOptions, "/transfers",
        i => (object)new { DebitAccountId = sourceIds[i], CreditAccountId = destinationId, Amount = fanInAmount },
        sourceIds.Count, concurrency);
    PrintPhaseResult("Fan-in transfers", fanInResults, fanInSw);

    // Balance consistency check
    Console.WriteLine();
    Console.Write("  Balance consistency check … ");
    var destBalance = await FetchAccountBalanceAsync(http, jsonOptions, destinationId);
    var successCount = (ulong)fanInResults.Count(r => r.Success);
    var expectedCredits = successCount * fanInAmount;
    if (destBalance is null)
    {
        Console.WriteLine("FAILED — could not fetch balance.");
    }
    else if (destBalance.CreditsPosted == expectedCredits)
    {
        Console.WriteLine($"✓ PASS — credits posted = {destBalance.CreditsPosted:N0} ({successCount:N0} transfers × {fanInAmount})");
    }
    else
    {
        Console.WriteLine($"⚠ MISMATCH — credits posted = {destBalance.CreditsPosted:N0}, expected {expectedCredits:N0}");
    }
}

// ── Summary ───────────────────────────────────────────────────────────────────
overallSw.Stop();
Console.WriteLine();
Console.WriteLine("═══════════════════════════════════════════════════════════════");
Console.WriteLine($"  Total test duration: {overallSw.ElapsedMilliseconds:N0} ms");
Console.WriteLine("═══════════════════════════════════════════════════════════════");
Console.WriteLine();
Console.WriteLine("Key observations:");
Console.WriteLine("  • Scenario 1 vs 2 throughput gap reveals PostgreSQL overhead.");
Console.WriteLine("  • Scenario 3 balance check validates TigerBeetle read consistency.");
Console.WriteLine("  • Scenario 4 stress-tests a hot account receiving concurrent transfers.");
Console.WriteLine();
return 0;

// ---------------------------------------------------------------------------
// Helper methods
// ---------------------------------------------------------------------------

static void PrintScenarioHeader(int number, string description)
{
    Console.WriteLine();
    Console.WriteLine($"══ Scenario {number}: {description}");
    Console.WriteLine(new string('─', 65));
}

static void PrintPhaseResult(string label, IEnumerable<RequestResult> results, Stopwatch sw)
{
    var list = results.ToList();
    var total = list.Count;
    var succeeded = list.Count(r => r.Success);
    var failed = total - succeeded;
    var latencies = list.Select(r => r.LatencyMs).OrderBy(x => x).ToArray();
    var p50 = Percentile(latencies, 50);
    var p95 = Percentile(latencies, 95);
    var p99 = Percentile(latencies, 99);
    var throughput = total / Math.Max(sw.Elapsed.TotalSeconds, 0.001);

    Console.WriteLine();
    Console.WriteLine($"  ┌─ {label}");
    Console.WriteLine($"  │  Total requests : {total:N0}");
    Console.WriteLine($"  │  Succeeded      : {succeeded:N0}");
    Console.WriteLine($"  │  Failed         : {failed:N0}");
    Console.WriteLine($"  │  Total elapsed  : {sw.ElapsedMilliseconds:N0} ms");
    Console.WriteLine($"  │  Throughput     : {throughput:N0} req/sec");
    Console.WriteLine($"  │  Latency p50    : {p50:N0} ms");
    Console.WriteLine($"  │  Latency p95    : {p95:N0} ms");
    Console.WriteLine($"  └─ Latency p99    : {p99:N0} ms");

    if (failed > 0)
    {
        var sample = list.Where(r => !r.Success && r.Error != null)
                         .Take(3).Select(r => r.Error!).Distinct();
        Console.WriteLine($"  ⚠ Sample errors : {string.Join(" | ", sample)}");
    }
}

static void PrintBatchPhaseResult(
    string label,
    IEnumerable<RequestResult> results,
    Stopwatch sw,
    int totalTransfers,
    int batchSize)
{
    var list = results.ToList();
    var totalRequests = list.Count;
    var succeededRequests = list.Count(r => r.Success);
    var failedRequests = totalRequests - succeededRequests;
    var succeededTransfers = succeededRequests * batchSize;
    var transferThroughput = succeededTransfers / Math.Max(sw.Elapsed.TotalSeconds, 0.001);
    var latencies = list.Select(r => r.LatencyMs).OrderBy(x => x).ToArray();
    var p50 = Percentile(latencies, 50);
    var p95 = Percentile(latencies, 95);
    var p99 = Percentile(latencies, 99);

    Console.WriteLine();
    Console.WriteLine($"  ┌─ {label}");
    Console.WriteLine($"  │  Batch requests : {totalRequests:N0} (≈ {batchSize} transfers each)");
    Console.WriteLine($"  │  Succeeded      : {succeededRequests:N0} requests ({succeededTransfers:N0} transfers)");
    Console.WriteLine($"  │  Failed         : {failedRequests:N0} requests");
    Console.WriteLine($"  │  Total elapsed  : {sw.ElapsedMilliseconds:N0} ms");
    Console.WriteLine($"  │  Throughput     : {transferThroughput:N0} transfers/sec");
    Console.WriteLine($"  │  Latency p50    : {p50:N0} ms / batch");
    Console.WriteLine($"  │  Latency p95    : {p95:N0} ms / batch");
    Console.WriteLine($"  └─ Latency p99    : {p99:N0} ms / batch");

    if (failedRequests > 0)
    {
        var sample = list.Where(r => !r.Success && r.Error != null)
                         .Take(3).Select(r => r.Error!).Distinct();
        Console.WriteLine($"  ⚠ Sample errors : {string.Join(" | ", sample)}");
    }
}

static long Percentile(long[] sortedValues, int percentile)
{
    if (sortedValues.Length == 0) return 0;
    var index = (int)Math.Round(percentile / 100.0 * (sortedValues.Length - 1));
    return sortedValues[Math.Clamp(index, 0, sortedValues.Length - 1)];
}

static async Task<(ConcurrentBag<RequestResult> Results, Stopwatch Sw)> RunParallelRequestsAsync(
    HttpClient http,
    JsonSerializerOptions jsonOptions,
    string path,
    Func<int, object> bodyFactory,
    int count,
    int concurrency)
{
    var results = new ConcurrentBag<RequestResult>();
    var sw = Stopwatch.StartNew();
    await RunParallelRequestsIntoAsync(http, jsonOptions, path, bodyFactory, count, concurrency, results);
    sw.Stop();
    return (results, sw);
}

static async Task RunParallelRequestsIntoAsync(
    HttpClient http,
    JsonSerializerOptions jsonOptions,
    string path,
    Func<int, object> bodyFactory,
    int count,
    int concurrency,
    ConcurrentBag<RequestResult> results)
{
    await Parallel.ForEachAsync(
        Enumerable.Range(0, count),
        new ParallelOptions { MaxDegreeOfParallelism = concurrency },
        async (i, ct) =>
        {
            var reqSw = Stopwatch.StartNew();
            try
            {
                var body = bodyFactory(i);
                using var resp = await http.PostAsJsonAsync(path, body, jsonOptions, ct);
                reqSw.Stop();
                results.Add(new RequestResult(resp.IsSuccessStatusCode, reqSw.ElapsedMilliseconds, null));
            }
            catch (Exception ex)
            {
                reqSw.Stop();
                results.Add(new RequestResult(false, reqSw.ElapsedMilliseconds, ex.Message));
            }
        });
}

static async Task<(ConcurrentBag<RequestResult> Results, Stopwatch Sw, int TotalTransfers)> RunBatchTransfersAsync(
    HttpClient http,
    JsonSerializerOptions jsonOptions,
    Guid debitId,
    Guid creditId,
    int totalTransfers,
    int batchSize,
    int concurrency)
{
    var results = new ConcurrentBag<RequestResult>();
    var batchCount = (int)Math.Ceiling((double)totalTransfers / batchSize);
    var sw = Stopwatch.StartNew();

    await Parallel.ForEachAsync(
        Enumerable.Range(0, batchCount),
        new ParallelOptions { MaxDegreeOfParallelism = concurrency },
        async (batchIndex, ct) =>
        {
            var start = batchIndex * batchSize;
            var thisSize = Math.Min(batchSize, totalTransfers - start);

            var transfers = Enumerable.Range(0, thisSize)
                .Select(_ => new
                {
                    DebitAccountId = debitId,
                    CreditAccountId = creditId,
                    Amount = 1UL,
                })
                .ToList();

            var body = new { Transfers = transfers };
            var reqSw = Stopwatch.StartNew();
            try
            {
                using var resp = await http.PostAsJsonAsync("/perf/transfers/batch", body, jsonOptions, ct);
                reqSw.Stop();
                results.Add(new RequestResult(resp.IsSuccessStatusCode, reqSw.ElapsedMilliseconds, null));
            }
            catch (Exception ex)
            {
                reqSw.Stop();
                results.Add(new RequestResult(false, reqSw.ElapsedMilliseconds, ex.Message));
            }
        });

    sw.Stop();
    return (results, sw, totalTransfers);
}

static async Task<List<Guid>?> CreateAccountsBatchAsync(
    HttpClient http,
    JsonSerializerOptions jsonOptions,
    int count,
    int batchSize)
{
    var allIds = new List<Guid>(count);
    var remaining = count;

    while (remaining > 0)
    {
        var thisBatch = Math.Min(remaining, batchSize);
        try
        {
            var body = new { Count = thisBatch, Ledger = 1, Code = 1 };
            using var resp = await http.PostAsJsonAsync("/perf/accounts/batch", body, jsonOptions);
            if (!resp.IsSuccessStatusCode) return null;

            var result = await resp.Content.ReadFromJsonAsync<CreateBatchResponse>(jsonOptions);
            if (result?.Ids is null) return null;

            allIds.AddRange(result.Ids);
            remaining -= thisBatch;
        }
        catch
        {
            return null;
        }
    }

    return allIds;
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

static async Task<AccountResponse?> FetchAccountBalanceAsync(HttpClient http, JsonSerializerOptions opts, Guid id)
{
    try
    {
        return await http.GetFromJsonAsync<AccountResponse>($"/accounts/{id}", opts);
    }
    catch
    {
        return null;
    }
}

// ---------------------------------------------------------------------------
// DTOs
// ---------------------------------------------------------------------------

internal sealed record RequestResult(bool Success, long LatencyMs, string? Error);

internal sealed record AccountSummary(Guid Id, string Name);

internal sealed record AccountResponse(
    Guid Id,
    string Name,
    uint Ledger,
    ushort Code,
    ulong CreditsPosted,
    ulong DebitsPosted,
    ulong Balance,
    DateTimeOffset CreatedAt);

internal sealed record CreateBatchResponse(int Count, List<Guid> Ids);


