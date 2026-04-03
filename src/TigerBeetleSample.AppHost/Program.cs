using Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .WithPgAdmin();

var postgresDb = postgres.AddDatabase("ledgerdb");

// Use explicit credentials so the CDC sidecar can reference the same values.
var rabbitmqUser = builder.AddParameter("rabbitmq-user", "guest");
var rabbitmqPassword = builder.AddParameter("rabbitmq-password", "guest", secret: true);

var rabbitmq = builder.AddRabbitMQ("rabbitmq", userName: rabbitmqUser, password: rabbitmqPassword)
    .WithManagementPlugin();

var tigerbeetle = builder
    .AddDockerfile("tigerbeetle", ".", "Dockerfile.tigerbeetle")
    // TigerBeetle needs io_uring; default Docker seccomp may block it on some hosts.
    .WithContainerRuntimeArgs("--security-opt", "seccomp=unconfined")
    .WithEndpoint(targetPort: 3000, port: 3000, scheme: "tcp", name: "tcp");

var tigerBeetleAddressForHost = "127.0.0.1:3000";
var tigerBeetleAddressForContainers = "tigerbeetle:3000";
var rabbitMqAddressForContainers = "rabbitmq:5672";

var api = builder.AddProject<Projects.TigerBeetleSample_Api>("api")
    .WithReference(postgresDb)
    .WithReference(rabbitmq)
    .WaitFor(postgresDb)
    .WaitFor(rabbitmq)
    .WaitFor(tigerbeetle)
    // API runs on the host process, so it should target the published host port.
    .WithEnvironment("TigerBeetle__Addresses", tigerBeetleAddressForHost);

// TigerBeetle native CDC sidecar — streams transfer events from TigerBeetle to RabbitMQ
// using the AMQP 0.9.1 protocol. The sidecar waits for the API to be running; the
// exchange is declared by TigerBeetleCdcConsumer at startup, but the sidecar has its
// own retry logic to handle the small window where the exchange may not exist yet.
builder.AddDockerfile("tigerbeetle-cdc", ".", "Dockerfile.tigerbeetle-cdc")
    .WithContainerRuntimeArgs("--security-opt", "seccomp=unconfined")
    .WaitFor(tigerbeetle)
    .WaitFor(rabbitmq)
    .WaitFor(api)
    // CDC runs in Docker, so use Docker network DNS names.
    .WithEnvironment("TB_ADDRESSES", tigerBeetleAddressForContainers)
    .WithEnvironment("RABBITMQ_HOST", rabbitMqAddressForContainers)
    .WithEnvironment("RABBITMQ_USER", rabbitmqUser)
    .WithEnvironment("RABBITMQ_PASSWORD", rabbitmqPassword);

builder.Build().Run();
