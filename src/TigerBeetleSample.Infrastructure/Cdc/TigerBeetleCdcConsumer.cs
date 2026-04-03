using System.Text;
using System.Text.Json;
using Marten;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using TigerBeetle;
using TigerBeetleSample.Domain.Entities;

namespace TigerBeetleSample.Infrastructure.Cdc;

/// <summary>
/// Background service that consumes TigerBeetle CDC events from RabbitMQ and projects
/// them into PostgreSQL via Marten. TigerBeetle's native <c>tigerbeetle amqp</c> job
/// publishes transfer events to the <c>tigerbeetle</c> fanout exchange.
/// </summary>
public sealed class TigerBeetleCdcConsumer : BackgroundService
{
    private const string ExchangeName = "tigerbeetle";
    private const string QueueName = "tigerbeetle-projections";

    private readonly IConfiguration _configuration;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TigerBeetleCdcConsumer> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public TigerBeetleCdcConsumer(
        IConfiguration configuration,
        IServiceScopeFactory scopeFactory,
        ILogger<TigerBeetleCdcConsumer> logger)
    {
        _configuration = configuration;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var rabbitUri = new Uri(
            _configuration.GetConnectionString("rabbitmq")
                ?? "amqp://guest:guest@localhost:5672/");

        var factory = new ConnectionFactory { Uri = rabbitUri };

        using var connection = await factory.CreateConnectionAsync(stoppingToken);
        using var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);

        // Declare the fanout exchange that tigerbeetle amqp publishes to.
        // This is idempotent — safe to call even if tigerbeetle-cdc already created it.
        await channel.ExchangeDeclareAsync(
            exchange: ExchangeName,
            type: ExchangeType.Fanout,
            durable: true,
            autoDelete: false,
            cancellationToken: stoppingToken);

        // Declare a durable queue and bind it to the TigerBeetle CDC exchange.
        await channel.QueueDeclareAsync(
            queue: QueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            cancellationToken: stoppingToken);

        await channel.QueueBindAsync(
            queue: QueueName,
            exchange: ExchangeName,
            routingKey: string.Empty,
            cancellationToken: stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, ea) =>
        {
            // Use CancellationToken.None for ack/nack so that inflight messages are
            // properly acknowledged even when shutdown is requested.
            try
            {
                var json = Encoding.UTF8.GetString(ea.Body.Span);
                var message = JsonSerializer.Deserialize<TigerBeetleCdcMessage>(json, JsonOptions);

                if (message is not null)
                {
                    await ProjectTransferAsync(message, CancellationToken.None);
                }

                await channel.BasicAckAsync(ea.DeliveryTag, multiple: false, cancellationToken: CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process TigerBeetle CDC message");
                await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: true, cancellationToken: CancellationToken.None);
            }
        };

        await channel.BasicConsumeAsync(
            queue: QueueName,
            autoAck: false,
            consumer: consumer,
            cancellationToken: stoppingToken);

        _logger.LogInformation("TigerBeetle CDC consumer started — listening on queue '{Queue}'", QueueName);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
    }

    private async Task ProjectTransferAsync(TigerBeetleCdcMessage message, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var session = scope.ServiceProvider.GetRequiredService<IDocumentSession>();

        var transferId = ((UInt128)message.Transfer.Id).ToGuid();
        var debitAccountId = ((UInt128)message.DebitAccount.Id).ToGuid();
        var creditAccountId = ((UInt128)message.CreditAccount.Id).ToGuid();

        // TigerBeetle timestamps are nanoseconds since the Unix epoch (u64).
        var nanoseconds = ulong.Parse(message.Transfer.Timestamp);
        var createdAt = DateTimeOffset.UnixEpoch.AddTicks((long)(nanoseconds / 100));

        var projection = new TransferProjection
        {
            Id = transferId,
            DebitAccountId = debitAccountId,
            CreditAccountId = creditAccountId,
            Amount = (ulong)message.Transfer.Amount,
            Ledger = message.Ledger,
            Code = message.Transfer.Code,
            CreatedAt = createdAt,
        };

        session.Store(projection);
        await session.SaveChangesAsync(ct);

        _logger.LogDebug("Projected transfer {TransferId}", transferId);
    }
}
