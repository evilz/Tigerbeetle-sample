using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TigerBeetle;
using TigerBeetleSample.Domain.Interfaces;
using TigerBeetleSample.Infrastructure.Options;
using TigerBeetleSample.Infrastructure.Repositories;
using TigerBeetleSample.Infrastructure.Services;

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
                addresses: [options.Addresses]);
        });

        services.AddScoped<IAccountProjectionRepository, AccountProjectionRepository>();
        services.AddScoped<ITransferProjectionRepository, TransferProjectionRepository>();
        services.AddSingleton<ILedgerService, TigerBeetleLedgerService>();

        return services;
    }
}
