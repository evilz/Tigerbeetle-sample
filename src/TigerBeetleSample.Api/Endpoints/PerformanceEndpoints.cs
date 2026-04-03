using TigerBeetle;

namespace TigerBeetleSample.Api.Endpoints;

/// <summary>
/// Batch performance endpoints that bypass PostgreSQL and call TigerBeetle directly.
/// Use these endpoints to isolate TigerBeetle throughput from PostgreSQL overhead.
/// </summary>
public static class PerformanceEndpoints
{
    /// <summary>Maximum objects per TigerBeetle batch call.</summary>
    private const int MaxBatchSize = 8190;

    public static IEndpointRouteBuilder MapPerformanceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/perf").WithTags("Performance");

        group.MapPost("/accounts/batch", CreateAccountsBatchAsync)
            .WithName("CreateAccountsBatch")
            .WithSummary("Create multiple accounts in a single TigerBeetle batch call (no PostgreSQL)");

        group.MapPost("/transfers/batch", CreateTransfersBatchAsync)
            .WithName("CreateTransfersBatch")
            .WithSummary("Create multiple transfers in a single TigerBeetle batch call (no PostgreSQL)");

        group.MapPost("/balances/batch", GetBalancesBatchAsync)
            .WithName("GetBalancesBatch")
            .WithSummary("Look up balances for multiple accounts in a single TigerBeetle call (no PostgreSQL)");

        return app;
    }

    private static async Task<IResult> CreateAccountsBatchAsync(
        CreateAccountsBatchRequest request,
        Client tbClient,
        CancellationToken cancellationToken)
    {
        if (request.Count <= 0 || request.Count > MaxBatchSize)
            return Results.BadRequest($"Count must be between 1 and {MaxBatchSize}.");

        var accounts = new Account[request.Count];
        for (var i = 0; i < request.Count; i++)
        {
            accounts[i] = new Account
            {
                Id = ID.Create(),
                Ledger = request.Ledger,
                Code = request.Code,
                Flags = AccountFlags.None,
            };
        }

        var errors = await tbClient.CreateAccountsAsync(accounts);

        if (errors.Length > 0)
        {
            return Results.Problem(
                $"Failed to create {errors.Length} account(s). " +
                $"First error at index {errors[0].Index}: {errors[0].Result}.");
        }

        var ids = accounts.Select(a => a.Id.ToGuid()).ToList();
        return Results.Ok(new CreateBatchResponse(ids.Count, ids));
    }

    private static async Task<IResult> CreateTransfersBatchAsync(
        CreateTransfersBatchRequest request,
        Client tbClient,
        CancellationToken cancellationToken)
    {
        if (request.Transfers is null || request.Transfers.Count == 0)
            return Results.BadRequest("Transfers list must not be empty.");

        if (request.Transfers.Count > MaxBatchSize)
            return Results.BadRequest($"Batch size cannot exceed {MaxBatchSize}.");

        var transfers = request.Transfers
            .Select(t => new Transfer
            {
                Id = ID.Create(),
                DebitAccountId = t.DebitAccountId.ToUInt128(),
                CreditAccountId = t.CreditAccountId.ToUInt128(),
                Amount = t.Amount,
                Ledger = t.Ledger,
                Code = t.Code,
                Flags = TransferFlags.None,
            })
            .ToArray();

        var errors = await tbClient.CreateTransfersAsync(transfers);

        if (errors.Length > 0)
        {
            return Results.Problem(
                $"Failed to create {errors.Length} transfer(s). " +
                $"First error at index {errors[0].Index}: {errors[0].Result}.");
        }

        return Results.Ok(new { Count = transfers.Length });
    }

    private static async Task<IResult> GetBalancesBatchAsync(
        GetBalancesBatchRequest request,
        Client tbClient,
        CancellationToken cancellationToken)
    {
        if (request.AccountIds is null || request.AccountIds.Count == 0)
            return Results.BadRequest("AccountIds must not be empty.");

        if (request.AccountIds.Count > MaxBatchSize)
            return Results.BadRequest($"Batch size cannot exceed {MaxBatchSize}.");

        var ids = request.AccountIds.Select(id => id.ToUInt128()).ToArray();
        var accounts = await tbClient.LookupAccountsAsync(ids);

        var balances = accounts
            .Select(a => new PerfBalanceResponse(
                a.Id.ToGuid(),
                (ulong)a.CreditsPosted,
                (ulong)a.DebitsPosted))
            .ToList();

        return Results.Ok(balances);
    }
}

public record CreateAccountsBatchRequest(int Count, uint Ledger = 1, ushort Code = 1);

public record BatchTransferItem(
    Guid DebitAccountId,
    Guid CreditAccountId,
    ulong Amount = 1,
    uint Ledger = 1,
    ushort Code = 1);

public record CreateTransfersBatchRequest(List<BatchTransferItem> Transfers);

public record GetBalancesBatchRequest(List<Guid> AccountIds);

public record CreateBatchResponse(int Count, List<Guid> Ids);

public record PerfBalanceResponse(Guid AccountId, ulong CreditsPosted, ulong DebitsPosted);
