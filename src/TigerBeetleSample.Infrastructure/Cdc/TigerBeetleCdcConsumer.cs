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

    /// <summary>
    /// Maximum number of unacknowledged messages delivered to this consumer at once,
    /// providing backpressure and preventing Marten/Postgres from being overwhelmed.
    /// </summary>
    private const ushort PrefetchCount = 100;

    /// <summary>
    /// All transfer event types emitted by TigerBeetle CDC.
    /// Messages with unknown types are acked and skipped to avoid blocking the queue.
    /// </summary>
    private static readonly HashSet<string> KnownTransferTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "single_phase",
        "two_phase_pending",
        "two_phase_posted",
        "two_phase_voided",
        "two_phase_expired",
    };

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
        var (connection, channel) = await CreateRabbitMqChannelAsync(stoppingToken);
        using var rabbitConnection = connection;
        using var rabbitChannel = channel;

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, ea) =>
        {
            // Use CancellationToken.None for ack/nack so messages are properly
            // acknowledged even when shutdown is requested.
            var json = Encoding.UTF8.GetString(ea.Body.Span);
            try
            {
                TigerBeetleCdcMessage? message;
                try
                {
                    message = JsonSerializer.Deserialize<TigerBeetleCdcMessage>(json, JsonOptions);
                }
                catch (JsonException ex)
                {
                    var snippet = json.Length > 200 ? json[..200] + "…" : json;
                    _logger.LogError(ex, "TigerBeetle CDC: failed to deserialize message. Payload: {Snippet}", snippet);
                    await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false, cancellationToken: CancellationToken.None);
                    return;
                }

                if (message is null)
                {
                    var snippet = json.Length > 200 ? json[..200] + "…" : json;
                    _logger.LogError("TigerBeetle CDC: deserialized message was null. Payload: {Snippet}", snippet);
                    await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false, cancellationToken: CancellationToken.None);
                    return;
                }

                if (!KnownTransferTypes.Contains(message.Type))
                {
                    _logger.LogWarning("TigerBeetle CDC: received unknown message type '{Type}'; skipping.", message.Type);
                    await channel.BasicAckAsync(ea.DeliveryTag, multiple: false, cancellationToken: CancellationToken.None);
                    return;
                }

                if (message.Transfer.Amount > (UInt128)ulong.MaxValue)
                {
                    _logger.LogError(
                        "TigerBeetle CDC: transfer amount '{Amount}' exceeds {MaxValue}; cannot project. Discarding.",
                        message.Transfer.Amount, ulong.MaxValue);
                    await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false, cancellationToken: CancellationToken.None);
                    return;
                }

                await ProjectTransferAsync(message, CancellationToken.None);
                await channel.BasicAckAsync(ea.DeliveryTag, multiple: false, cancellationToken: CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "TigerBeetle CDC: transient error processing message; requeueing.");
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

    /// <summary>
    /// Establishes a RabbitMQ connection with exponential backoff retry so that
    /// transient connectivity issues (e.g. broker starting up) do not crash the host.
    /// </summary>
    private async Task<(IConnection Connection, IChannel Channel)> CreateRabbitMqChannelAsync(CancellationToken stoppingToken)
    {
        var rabbitUri = new Uri(
            _configuration.GetConnectionString("rabbitmq")
                ?? "amqp://guest:guest@localhost:5672/");

        var factory = new ConnectionFactory { Uri = rabbitUri };
        var retryDelay = TimeSpan.FromSeconds(1);
        var maxRetryDelay = TimeSpan.FromSeconds(30);

        while (true)
        {
            stoppingToken.ThrowIfCancellationRequested();

            try
            {
                var connection = await factory.CreateConnectionAsync(stoppingToken);
                var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);

                // Limit unacknowledged messages to provide backpressure.
                await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: PrefetchCount, global: false, cancellationToken: stoppingToken);

                // Declare the fanout exchange that tigerbeetle amqp publishes to.
                // Idempotent — safe to call even if the CDC sidecar already created it.
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

                return (connection, channel);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "TigerBeetle CDC: failed to connect to RabbitMQ or declare topology. Retrying in {RetryDelay}.",
                    retryDelay);

                await Task.Delay(retryDelay, stoppingToken);

                retryDelay = TimeSpan.FromTicks(Math.Min(retryDelay.Ticks * 2, maxRetryDelay.Ticks));
            }
        }
    }

    private async Task ProjectTransferAsync(TigerBeetleCdcMessage message, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var session = scope.ServiceProvider.GetRequiredService<IDocumentSession>();

        var transferId = ((UInt128)message.Transfer.Id).ToGuid();
        var debitAccountId = ((UInt128)message.DebitAccount.Id).ToGuid();
        var creditAccountId = ((UInt128)message.CreditAccount.Id).ToGuid();

        // TigerBeetle timestamps are nanoseconds since the Unix epoch.
        var createdAt = DateTimeOffset.UnixEpoch.AddTicks((long)(message.Transfer.Timestamp / 100));

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
