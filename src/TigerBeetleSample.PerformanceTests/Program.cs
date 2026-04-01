using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

// ---------------------------------------------------------------------------
// TigerBeetle Performance Test Runner
//
// Usage:
//   dotnet run -- [baseUrl] [accountCount] [transferCount]
//
// Examples:
//   dotnet run -- http://localhost:5000 10000 10000
//   dotnet run                          (uses defaults: localhost:5000, 10000, 10000)
//
// The test creates accounts and transfers directly in TigerBeetle via the
// /perf/* endpoints which bypass PostgreSQL so that only TigerBeetle
// throughput is measured.
// ---------------------------------------------------------------------------

var baseUrl = args.Length > 0 ? args[0].TrimEnd('/') : "http://localhost:5000";
var accountCount = args.Length > 1 ? int.Parse(args[1]) : 10_000;
var transferCount = args.Length > 2 ? int.Parse(args[2]) : 10_000;

Console.WriteLine("═══════════════════════════════════════════════════════════════");
Console.WriteLine("  TigerBeetle Performance Test");
Console.WriteLine("═══════════════════════════════════════════════════════════════");
Console.WriteLine($"  API URL        : {baseUrl}");
Console.WriteLine($"  Account count  : {accountCount:N0}");
Console.WriteLine($"  Transfer count : {transferCount:N0}");
Console.WriteLine("═══════════════════════════════════════════════════════════════");
Console.WriteLine();

var jsonOptions = new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true,
    Converters = { new JsonStringEnumConverter() },
};

using var http = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromMinutes(10) };

// ── 1. ACCOUNT CREATION ─────────────────────────────────────────────────────
Console.WriteLine("▶ Step 1 — Account creation (TigerBeetle only, no PostgreSQL)");
Console.Write($"  Creating {accountCount:N0} accounts in a single batch request … ");

var accountRequest = new { Count = accountCount, Ledger = 1, Code = 1 };
var accountResponse = await PostAsync<PerfBatchResult>("/perf/accounts/batch", accountRequest, http, jsonOptions);

if (accountResponse is null)
{
    Console.WriteLine("FAILED — could not reach the API. Is the application running?");
    return 1;
}

PrintResult("Account creation", accountResponse);

// Store the first two account IDs for the transfer test
var debitId = accountResponse.Ids.FirstOrDefault();
var creditId = accountResponse.Ids.Skip(1).FirstOrDefault();

// ── 2. TRANSFER CREATION ─────────────────────────────────────────────────────
Console.WriteLine();
Console.WriteLine("▶ Step 2 — Transfer creation (TigerBeetle only, no PostgreSQL)");

if (debitId == Guid.Empty || creditId == Guid.Empty)
{
    Console.WriteLine("  SKIPPED — not enough accounts were created (need at least 2).");
    return 0;
}

Console.Write($"  Creating {transferCount:N0} transfers between two accounts … ");

var transferRequest = new
{
    DebitAccountId = debitId,
    CreditAccountId = creditId,
    Amount = (ulong)100,
    Count = transferCount,
    Ledger = 1,
    Code = 1,
};
var transferResponse = await PostAsync<PerfBatchResult>("/perf/transfers/batch", transferRequest, http, jsonOptions);

if (transferResponse is null)
{
    Console.WriteLine("FAILED.");
    return 1;
}

PrintResult("Transfer creation", transferResponse);

// ── 3. SUMMARY ───────────────────────────────────────────────────────────────
Console.WriteLine();
Console.WriteLine("═══════════════════════════════════════════════════════════════");
Console.WriteLine("  Summary");
Console.WriteLine("═══════════════════════════════════════════════════════════════");
PrintSummaryRow("Accounts", accountResponse);
PrintSummaryRow("Transfers", transferResponse);
Console.WriteLine("═══════════════════════════════════════════════════════════════");
Console.WriteLine();
Console.WriteLine("Note: All operations went directly to TigerBeetle.");
Console.WriteLine("      PostgreSQL was not involved and was not the bottleneck.");
Console.WriteLine();

return 0;

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

static async Task<T?> PostAsync<T>(string path, object body, HttpClient http, JsonSerializerOptions opts)
{
    try
    {
        var response = await http.PostAsJsonAsync(path, body, opts);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(opts);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"ERROR: {ex.Message}");
        return default;
    }
}

static void PrintResult(string label, PerfBatchResult result)
{
    Console.WriteLine("done.");
    Console.WriteLine($"  ┌─ {label}");
    Console.WriteLine($"  │  Count            : {result.Count:N0}");
    Console.WriteLine($"  │  Elapsed          : {result.ElapsedMs:N0} ms");
    Console.WriteLine($"  └─ Throughput       : {result.ThroughputPerSecond:N0} ops/sec");
}

static void PrintSummaryRow(string label, PerfBatchResult result) =>
    Console.WriteLine($"  {label,-12} {result.Count,10:N0} items  {result.ElapsedMs,8:N0} ms  {result.ThroughputPerSecond,10:N0} ops/sec");

// ---------------------------------------------------------------------------
// DTOs
// ---------------------------------------------------------------------------

internal sealed record PerfBatchResult(
    int Count,
    long ElapsedMs,
    long ThroughputPerSecond,
    IReadOnlyList<Guid> Ids);
