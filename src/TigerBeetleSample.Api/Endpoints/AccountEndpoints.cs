using TigerBeetleSample.Domain.Events;
using TigerBeetleSample.Domain.Interfaces;
using Wolverine;

namespace TigerBeetleSample.Api.Endpoints;

public static class AccountEndpoints
{
    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/accounts").WithTags("Accounts");

        group.MapPost("/", CreateAccountAsync)
            .WithName("CreateAccount")
            .WithSummary("Create a new ledger account");

        group.MapGet("/", GetAccountsAsync)
            .WithName("GetAccounts")
            .WithSummary("List all accounts");

        group.MapGet("/{id:guid}", GetAccountByIdAsync)
            .WithName("GetAccountById")
            .WithSummary("Get account by ID with live balance");

        return app;
    }

    private static async Task<IResult> CreateAccountAsync(
        CreateAccountRequest request,
        ILedgerService ledgerService,
        IMessageBus bus,
        CancellationToken cancellationToken)
    {
        var accountId = await ledgerService.CreateAccountAsync(
            request.Name, request.Ledger, request.Code, cancellationToken);

        await bus.PublishAsync(new AccountCreatedEvent(
            accountId, request.Name, request.Ledger, request.Code, DateTimeOffset.UtcNow));

        return Results.Accepted($"/accounts/{accountId}", new { Id = accountId });
    }

    private static async Task<IResult> GetAccountsAsync(
        IAccountProjectionRepository repository,
        ILedgerService ledgerService,
        CancellationToken cancellationToken)
    {
        var accounts = await repository.GetAllAsync(cancellationToken);

        var balanceTasks = accounts.Select(a => ledgerService.GetBalanceAsync(a.Id, cancellationToken));
        var balances = await Task.WhenAll(balanceTasks);

        var responses = accounts
            .Zip(balances, (account, balance) => ToResponse(account, balance.CreditsPosted, balance.DebitsPosted))
            .ToList();

        return Results.Ok(responses);
    }

    private static async Task<IResult> GetAccountByIdAsync(
        Guid id,
        IAccountProjectionRepository repository,
        ILedgerService ledgerService,
        CancellationToken cancellationToken)
    {
        var account = await repository.GetByIdAsync(id, cancellationToken);

        if (account is null)
        {
            return Results.NotFound();
        }

        var (credits, debits) = await ledgerService.GetBalanceAsync(id, cancellationToken);
        return Results.Ok(ToResponse(account, credits, debits));
    }

    private static AccountResponse ToResponse(
        Domain.Entities.AccountProjection account,
        ulong creditsPosted,
        ulong debitsPosted) =>
        new(
            account.Id,
            account.Name,
            account.Ledger,
            account.Code,
            creditsPosted,
            debitsPosted,
            creditsPosted >= debitsPosted ? creditsPosted - debitsPosted : 0,
            account.CreatedAt);
}

public record CreateAccountRequest(
    string Name,
    uint Ledger = 1,
    ushort Code = 1);

public record AccountResponse(
    Guid Id,
    string Name,
    uint Ledger,
    ushort Code,
    ulong CreditsPosted,
    ulong DebitsPosted,
    ulong Balance,
    DateTimeOffset CreatedAt);
