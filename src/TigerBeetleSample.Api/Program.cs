using TigerBeetleSample.Api.Endpoints;
using TigerBeetleSample.Domain.Events;
using TigerBeetleSample.Infrastructure.Extensions;
using TigerBeetleSample.Infrastructure.Handlers;
using TigerBeetleSample.ServiceDefaults;
using Wolverine;
using Wolverine.RabbitMQ;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddOpenApi();

var rabbitMqConnectionString = builder.Configuration.GetConnectionString("rabbitmq")
    ?? "amqp://guest:guest@localhost:5672/";

builder.Host.UseWolverine(opts =>
{
    opts.UseRabbitMq(new Uri(rabbitMqConnectionString))
        .AutoProvision();

    opts.PublishMessage<AccountCreatedEvent>().ToRabbitQueue("account-created");
    opts.PublishMessage<TransferCreatedEvent>().ToRabbitQueue("transfer-created");

    opts.ListenToRabbitQueue("account-created");
    opts.ListenToRabbitQueue("transfer-created");

    opts.Discovery.IncludeAssembly(typeof(AccountProjectionHandler).Assembly);
});

var app = builder.Build();

app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapAccountEndpoints();
app.MapTransferEndpoints();
app.MapPerformanceEndpoints();

app.Run();
