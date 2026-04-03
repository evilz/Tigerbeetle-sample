using TigerBeetleSample.Api.Endpoints;
using TigerBeetleSample.Infrastructure.Data;
using TigerBeetleSample.Infrastructure.Extensions;
using TigerBeetleSample.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.AddNpgsqlDbContext<AppDbContext>("ledgerdb");

builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddOpenApi();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.EnsureCreatedAsync();
}

app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapAccountEndpoints();
app.MapTransferEndpoints();
app.MapPerformanceEndpoints();

app.Run();
