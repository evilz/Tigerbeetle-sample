using Marten;
using TigerBeetleSample.Domain.Entities;
using TigerBeetleSample.Domain.Events;

namespace TigerBeetleSample.Infrastructure.Handlers;

public class AccountProjectionHandler
{
    public async Task Handle(AccountCreatedEvent message, IDocumentSession session, CancellationToken cancellationToken)
    {
        var projection = new AccountProjection
        {
            Id = message.AccountId,
            Name = message.Name,
            Ledger = message.Ledger,
            Code = message.Code,
            CreatedAt = message.CreatedAt,
        };

        session.Store(projection);
        await session.SaveChangesAsync(cancellationToken);
    }
}
