# HexFund API

A .NET 10 REST API for managing personal finances — accounts, recurring transactions, and cash flow projections. Built with a clean layered architecture (Core / Application / Infrastructure / API) and backed by PostgreSQL.

---

## Table of Contents

- [Architecture](#architecture)
- [Prerequisites](#prerequisites)
- [Getting Started](#getting-started)
- [Configuration](#configuration)
- [Database Migrations](#database-migrations)
- [Running the API](#running-the-api)
- [API Overview](#api-overview)
- [Project Structure](#project-structure)
- [Key Design Decisions](#key-design-decisions)

---

## Architecture

```
HexFund.Core           — Entities, enums, repository interfaces (no dependencies)
HexFund.Application    — Services, DTOs, validation, business logic
HexFund.Infrastructure — EF Core DbContext, repositories, caching
HexFund.API            — Controllers, middleware, DI registration, Program.cs
```

Dependencies flow inward only: API → Infrastructure → Application → Core.

---

## Prerequisites

| Tool | Version |
|------|---------|
| .NET SDK | 10.0+ |
| PostgreSQL | 14+ |
| EF Core CLI | `dotnet tool install --global dotnet-ef` |

---

## Getting Started

```bash
# 1. Clone the repository
git clone <repo-url>
cd HexFund_API

# 2. Restore dependencies
dotnet restore

# 3. Apply configuration (see Configuration section below)

# 4. Apply database migrations
dotnet ef database update --project HexFund.API

# 5. Run the API
dotnet run --project HexFund.API
```

Swagger UI is available at `http://localhost:5000` in Development.

---

## Configuration

Copy `appsettings.json` values to `appsettings.Development.json` and fill in your local values. At minimum, update:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Database=HexFund_Dev;Username=postgres;Password=yourpassword"
  },
  "JwtSettings": {
    "SecretKey": "your-secret-key-must-be-at-least-32-characters",
    "Issuer": "https://localhost:5001",
    "Audience": "https://localhost:5001",
    "ExpirationMinutes": 60
  }
}
```

**Production:**

| Setting | Notes |
|---------|-------|
| `JwtSettings:SecretKey` | Must be ≥ 32 characters. Use a securely generated random string — never commit this. |
| `ConnectionStrings:DefaultConnection` | Point to your production PostgreSQL instance. |
| `AllowedOrigins` | Set to your actual frontend origin(s). |

---

## Database Migrations

```bash
# Apply all pending migrations
dotnet ef database update --project HexFund.API

# Add a new migration
dotnet ef migrations add <MigrationName> --project HexFund.API

# Roll back to a specific migration
dotnet ef database update <MigrationName> --project HexFund.API
```

Migrations are applied automatically on startup if the database is reachable. This can be disabled for environments where you prefer manual migration control.

---

## Running the API

```bash
# Development (hot reload)
dotnet watch run --project HexFund.API

# Production build
dotnet publish HexFund.API -c Release -o ./publish
dotnet ./publish/HexFund.API.dll
```

Logs are written to both the console and `logs/hexfund-<date>.log`, retained for 30 days.

---

## API Overview

All endpoints except `/api/auth/register` and `/api/auth/login` require a `Bearer` JWT token.

### Authentication — `/api/auth`

| Method | Endpoint | Description |
|--------|----------|-------------|
| `POST` | `/register` | Create a new user account |
| `POST` | `/login` | Authenticate and receive a JWT |
| `POST` | `/logout` | Signal the client to discard its token |
| `GET` | `/me` | Get the currently authenticated user |

### Accounts — `/api/accounts`

| Method | Endpoint | Description |
|--------|----------|-------------|
| `GET` | `/` | List all accounts for the current user |
| `GET` | `/{id}` | Get a single account |
| `POST` | `/` | Create a new account |
| `PUT` | `/{id}` | Update an account |
| `DELETE` | `/{id}` | Delete an account and its transactions |

### Transactions — `/api/accounts/{accountId}/transactions`

| Method | Endpoint | Description |
|--------|----------|-------------|
| `GET` | `/` | List transactions (`?includeHistory=true` for amended predecessors) |
| `GET` | `/{id}` | Get a single transaction |
| `POST` | `/` | Create a transaction |
| `PUT` | `/{id}` | Update a transaction |
| `DELETE` | `/{id}` | Delete a transaction |
| `POST` | `/{id}/amend` | Amend a recurring transaction from an effective date forward |
| `GET` | `/transactions-for-date` | Occurrences on a specific date (`?date=yyyy-MM-dd`) |
| `GET` | `/transactions-for-month` | All occurrences in a month, grouped by date (`?year=&month=`) |

### Cash Flow — `/api/accounts/{accountId}`

| Method | Endpoint | Description |
|--------|----------|-------------|
| `GET` | `/cashflow` | Projection with daily snapshots for a date range |
| `GET` | `/monthly-overview` | Income, expense totals, and category breakdown for a month |
| `GET` | `/daily-balance` | Balance on a specific date |
| `GET` | `/rolling-balance` | Daily balance snapshots for a date range |
| `GET` | `/balance-at-date` | Balance as of a specific point in time |
| `GET` | `/transactions/with-balances` | Transactions with their running balances |

### Health — `/api/health`

| Method | Endpoint | Description |
|--------|----------|-------------|
| `GET` | `/` | Health check for container/load-balancer probes |

---

## Project Structure

```
HexFund.API/
├── Controllers/          # HTTP endpoints
├── Extensions/           # IServiceCollection extension methods (DI registration)
├── Middleware/           # Global exception handler, request logging
├── Migrations/           # EF Core migrations
└── Program.cs            # App bootstrap

HexFund.Application/
├── DTOs/                 # Request/response models
├── Interfaces/           # Service and cache interfaces
├── Models/               # Configuration models (e.g. JwtSettings)
├── Services/             # Business logic (Auth, Account, Transaction, CashFlow)
└── Validation/           # Custom validation attributes

HexFund.Core/
├── Entities/             # Account, Transaction, User
├── Enums/                # FrequencyType
└── Interfaces/           # Repository contracts

HexFund.Infrastructure/
├── Caching/              # IMemoryCache wrapper (CacheService)
├── Data/                 # DbContext, value converters
└── Repositories/         # EF Core repository implementations
```

---

## Key Design Decisions

**Transaction amendment** — Rather than editing a recurring transaction in-place (which would silently alter its history), the `Amend` endpoint end-dates the original row and creates a successor with `PredecessorTransactionId` set. The UI can use this to show or hide the history chain. Deleting a successor restores the predecessor to open-ended.

**Recurrence expansion** — Recurring transactions are stored as templates (a single DB row with a `FrequencyType`). `TransactionRecurrenceService` expands them into virtual occurrences at query time — nothing extra is persisted. This keeps storage lean and makes it straightforward to change a transaction's future behavior via an amendment.

**Balance calculation** — Balances are calculated on the fly from `InitialBalance` + all transaction occurrences up to a given date. There is no stored `CurrentBalance` that can drift out of sync.

**Caching** — Frequently read data (accounts, transactions, monthly overviews) is held in `IMemoryCache` with per-category TTLs. The cache is invalidated explicitly on any mutation, so reads are always consistent after a write.

**Two-query account load** — `GetByUserIdWithTransactionsUpToDateAsync` fetches accounts and their transactions in two separate queries rather than a JOIN, avoiding the Cartesian row explosion that occurs when accounts have many transactions.
