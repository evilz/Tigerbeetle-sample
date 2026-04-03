using Marten;
using TigerBeetleSample.Domain.Entities;
using TigerBeetleSample.Domain.Events;

namespace TigerBeetleSample.Infrastructure.Handlers;

public class TransferProjectionHandler
{
    public async Task Handle(TransferCreatedEvent message, IDocumentSession session, CancellationToken cancellationToken)
    {
        var projection = new TransferProjection
        {
            Id = message.TransferId,
            DebitAccountId = message.DebitAccountId,
            CreditAccountId = message.CreditAccountId,
            Amount = message.Amount,
            Ledger = message.Ledger,
            Code = message.Code,
            CreatedAt = message.CreatedAt,
        };

        session.Store(projection);
        await session.SaveChangesAsync(cancellationToken);
    }
}
