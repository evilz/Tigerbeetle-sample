using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .WithPgAdmin();

var postgresDb = postgres.AddDatabase("ledgerdb");

var tigerbeetle = builder
    .AddDockerfile("tigerbeetle", ".", "Dockerfile.tigerbeetle")
    // TigerBeetle needs io_uring; default Docker seccomp may block it on some hosts.
    .WithContainerRuntimeArgs("--security-opt", "seccomp=unconfined")
    .WithEndpoint(targetPort: 3000, port: 3000, scheme: "tcp", name: "tcp");

var tigerbeetleEndpoint = tigerbeetle.GetEndpoint("tcp");

builder.AddProject<Projects.TigerBeetleSample_Api>("api")
    .WithReference(postgresDb)
    .WaitFor(postgresDb)
    .WaitFor(tigerbeetle)
    .WithEnvironment("TigerBeetle__Addresses",
        $"{tigerbeetleEndpoint.Property(EndpointProperty.IPV4Host)}:{tigerbeetleEndpoint.Property(EndpointProperty.Port)}");

builder.Build().Run();
