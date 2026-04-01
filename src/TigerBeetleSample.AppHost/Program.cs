using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .WithDataVolume()
    .WithPgAdmin();

var postgresDb = postgres.AddDatabase("ledgerdb");

var tigerbeetle = builder
    .AddDockerfile("tigerbeetle", ".", "Dockerfile.tigerbeetle")
    .WithEndpoint(targetPort: 3000, port: 3000, scheme: "tcp", name: "tcp");

builder.AddProject<Projects.TigerBeetleSample_Api>("api")
    .WithReference(postgresDb)
    .WaitFor(postgresDb)
    .WaitFor(tigerbeetle)
    .WithEnvironment("TigerBeetle__Addresses",
        tigerbeetle.GetEndpoint("tcp").Property(EndpointProperty.HostAndPort));

builder.Build().Run();
