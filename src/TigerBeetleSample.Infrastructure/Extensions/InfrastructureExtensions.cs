using System.Net;
using System.Net.Sockets;
using Marten;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TigerBeetle;
using TigerBeetleSample.Domain.Entities;
using TigerBeetleSample.Domain.Interfaces;
using TigerBeetleSample.Infrastructure.Cdc;
using TigerBeetleSample.Infrastructure.Options;
using TigerBeetleSample.Infrastructure.Repositories;
using TigerBeetleSample.Infrastructure.Services;
using Wolverine.Marten;

namespace TigerBeetleSample.Infrastructure.Extensions;

public static class InfrastructureExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<TigerBeetleOptions>(opts =>
        {
            var section = configuration.GetSection(TigerBeetleOptions.SectionName);
            opts.Addresses = section["Addresses"] ?? opts.Addresses;
            if (uint.TryParse(section["ClusterId"], out var clusterId))
                opts.ClusterId = clusterId;
        });

        services.AddSingleton<Client>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<TigerBeetleOptions>>().Value;
            return new Client(
                clusterID: (System.UInt128)options.ClusterId,
                addresses: NormalizeAddresses(options.Addresses));
        });

        var connectionString = configuration.GetConnectionString("ledgerdb")
            ?? throw new InvalidOperationException("Connection string 'ledgerdb' is required.");

        services.AddMarten(opts =>
        {
            opts.Connection(connectionString);
            opts.Schema.For<AccountProjection>().Identity(x => x.Id);
            opts.Schema.For<TransferProjection>()
                .Identity(x => x.Id)
                .Index(x => x.DebitAccountId)
                .Index(x => x.CreditAccountId);
        })
        .ApplyAllDatabaseChangesOnStartup()
        .IntegrateWithWolverine();

        services.AddScoped<IAccountProjectionRepository, AccountProjectionRepository>();
        services.AddScoped<ITransferProjectionRepository, TransferProjectionRepository>();
        services.AddSingleton<ILedgerService, TigerBeetleLedgerService>();

        // Guard CDC registration so hosts that don't have RabbitMQ available
        // (tests, tooling, perf harnesses) can still use AddInfrastructure without failing.
        if (configuration.GetValue<bool>("Cdc:Enabled"))
        {
            services.AddHostedService<TigerBeetleCdcConsumer>();
        }

        return services;
    }

    private static string[] NormalizeAddresses(string addresses)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(addresses);

        var normalizedAddresses = addresses
            .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeAddress)
            .Where(a => !string.IsNullOrWhiteSpace(a))
            .ToArray();

        if (normalizedAddresses.Length == 0)
            throw new ArgumentException("TigerBeetle address list is empty after normalization.", nameof(addresses));

        return normalizedAddresses;
    }

    private static string NormalizeAddress(string address)
    {
        if (int.TryParse(address, out _))
            return address;

        if (Uri.TryCreate(address, UriKind.Absolute, out var absoluteUri))
            return NormalizeHostAndPort(absoluteUri.Host, absoluteUri.Port, address);

        if (Uri.TryCreate($"tcp://{address}", UriKind.Absolute, out var tcpUri))
            return NormalizeHostAndPort(tcpUri.Host, tcpUri.Port, address);

        return address;
    }

    private static string NormalizeHostAndPort(string host, int port, string originalAddress)
    {
        if (string.IsNullOrWhiteSpace(host))
            return originalAddress;

        if (IPAddress.TryParse(host, out var ipAddress))
            return FormatEndpoint(ipAddress, port);

        var resolvedAddresses = Dns.GetHostAddresses(host);

        var resolvedAddress = resolvedAddresses
            .FirstOrDefault(ip => ip.AddressFamily == AddressFamily.InterNetwork)
            ?? resolvedAddresses.FirstOrDefault();

        if (resolvedAddress is null)
            return originalAddress;

        return FormatEndpoint(resolvedAddress, port);
    }

    private static string FormatEndpoint(IPAddress ipAddress, int port)
    {
        var host = ipAddress.AddressFamily == AddressFamily.InterNetworkV6
            ? $"[{ipAddress}]"
            : ipAddress.ToString();

        return port > 0 ? $"{host}:{port}" : host;
    }
}
