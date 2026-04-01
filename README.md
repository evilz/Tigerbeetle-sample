# TigerBeetle Sample — .NET 10 + Aspire

A complete .NET 10 solution showcasing **TigerBeetle** as a financial ledger and **PostgreSQL** as a read-model projection store, orchestrated locally with **.NET Aspire**.

## Architecture

```
TigerBeetleSample.slnx
└── src/
    ├── TigerBeetleSample.AppHost/       # Aspire orchestration host
    ├── TigerBeetleSample.ServiceDefaults/ # Shared observability defaults
    ├── TigerBeetleSample.Domain/        # Entities & repository interfaces
    ├── TigerBeetleSample.Infrastructure/ # EF Core, TigerBeetle client, repos
    └── TigerBeetleSample.Api/           # ASP.NET Core Minimal API
```

### Infrastructure

| Component | Role |
|-----------|------|
| **TigerBeetle 0.16.78** | Financial ledger — immutable double-entry bookkeeping |
| **PostgreSQL** | Projection / read-model store (account names, transfer history) |
| **EF Core 10** | ORM for PostgreSQL projections |
| **Aspire 13** | Local orchestration, service discovery, dashboards |

## Running locally

### Prerequisites
- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/) (for PostgreSQL & TigerBeetle containers)

### Start the solution

```bash
cd src/TigerBeetleSample.AppHost
dotnet run
```

Aspire will:
1. Build a TigerBeetle container image from `Dockerfile.tigerbeetle` and start it on port 3000
2. Start a PostgreSQL container with a persistent volume
3. Start the API (with automatic connection strings injected)
4. Open the Aspire dashboard at `https://localhost:18888`

## API Endpoints

### Accounts

| Method | Path | Description |
|--------|------|-------------|
| `POST` | `/accounts` | Create a ledger account |
| `GET` | `/accounts` | List all accounts with live balances |
| `GET` | `/accounts/{id}` | Get a single account with live balance |

**Create account request:**
```json
{ "name": "Alice", "ledger": 1, "code": 1 }
```

### Transfers

| Method | Path | Description |
|--------|------|-------------|
| `POST` | `/transfers` | Transfer between two accounts |
| `GET` | `/transfers/account/{accountId}` | Transfer history for an account |

**Create transfer request:**
```json
{
  "debitAccountId": "<guid>",
  "creditAccountId": "<guid>",
  "amount": 1000,
  "ledger": 1,
  "code": 1
}
```

> **Note:** `amount` is an integer in the smallest denomination (e.g. cents).

### Health checks

| Path | Description |
|------|-------------|
| `GET /health` | Full health check |
| `GET /alive` | Liveness probe |

### OpenAPI

Available at `/openapi/v1.json` in development. Open `http://localhost:<port>/openapi/v1.json` or use the Aspire dashboard.

## Key design decisions

- **TigerBeetle is the source of truth** for all balances. Account balances are always fetched live from TigerBeetle.
- **PostgreSQL stores projections** — account names, metadata, and transfer history that TigerBeetle doesn't store.
- **Write ordering**: TigerBeetle operation succeeds first, then the PostgreSQL projection is saved. If the projection write fails, the ledger entry is still committed (TigerBeetle is append-only).
- **IDs**: TigerBeetle uses monotonic `UInt128` IDs (ULID-based). These are converted to `Guid` for the API and PostgreSQL.
- **Singleton TigerBeetle client**: The `Client` is registered as a singleton and is thread-safe.
- **Options pattern**: TigerBeetle address is injected via `IOptions<TigerBeetleOptions>` from environment variable `TigerBeetle__Addresses` (set automatically by Aspire).

## Package versions

| Package | Version |
|---------|---------|
| `Aspire.Hosting.AppHost` | 13.2.1 |
| `Aspire.Hosting.PostgreSQL` | 13.2.1 |
| `Aspire.Npgsql.EntityFrameworkCore.PostgreSQL` | 13.2.1 |
| `tigerbeetle` (.NET client) | 0.16.78 |
| `TigerBeetle` (server image) | 0.16.78 |
| `Npgsql.EntityFrameworkCore.PostgreSQL` | 10.0.1 |