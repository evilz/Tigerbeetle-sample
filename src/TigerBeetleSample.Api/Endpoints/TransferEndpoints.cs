using TigerBeetleSample.Domain.Interfaces;

namespace TigerBeetleSample.Api.Endpoints;

public static class TransferEndpoints
{
    public static IEndpointRouteBuilder MapTransferEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/transfers").WithTags("Transfers");

        group.MapPost("/", CreateTransferAsync)
            .WithName("CreateTransfer")
            .WithSummary("Create a transfer between two accounts");

        group.MapGet("/account/{accountId:guid}", GetTransfersByAccountAsync)
            .WithName("GetTransfersByAccount")
            .WithSummary("Get transfer history for an account");

        return app;
    }

    private static async Task<IResult> CreateTransferAsync(
        CreateTransferRequest request,
        ILedgerService ledgerService,
        CancellationToken cancellationToken)
    {
        var transferId = await ledgerService.CreateTransferAsync(
            request.DebitAccountId,
            request.CreditAccountId,
            request.Amount,
            request.Ledger,
            request.Code,
            cancellationToken);

        // The transfer is recorded in TigerBeetle (source of truth).
        // TigerBeetle's native CDC job streams the event to RabbitMQ, where
        // TigerBeetleCdcConsumer projects it into PostgreSQL asynchronously.
        return Results.Accepted($"/transfers/account/{request.DebitAccountId}", new { Id = transferId });
    }

    private static async Task<IResult> GetTransfersByAccountAsync(
        Guid accountId,
        ITransferProjectionRepository repository,
        CancellationToken cancellationToken)
    {
        var transfers = await repository.GetByAccountIdAsync(accountId, cancellationToken);
        return Results.Ok(transfers.Select(ToResponse));
    }

    private static TransferResponse ToResponse(Domain.Entities.TransferProjection t) =>
        new(t.Id, t.DebitAccountId, t.CreditAccountId, t.Amount, t.Ledger, t.Code, t.CreatedAt);
}

public record CreateTransferRequest(
    Guid DebitAccountId,
    Guid CreditAccountId,
    ulong Amount,
    uint Ledger = 1,
    ushort Code = 1);

public record TransferResponse(
    Guid Id,
    Guid DebitAccountId,
    Guid CreditAccountId,
    ulong Amount,
    uint Ledger,
    ushort Code,
    DateTimeOffset CreatedAt);
