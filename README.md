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

In Development the app applies pending migrations on startup. In production the deploy pipeline applies them (the `migrate` job, using the `dotnet-ef` version pinned in `.config/dotnet-tools.json`) before the new build is deployed; the app never migrates itself.

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

Every endpoint except `/api/health` requires an Entra External ID `Bearer` token (enforced by a fallback authorization policy, so new controllers are protected by default). Set `AzureAd:RequiredScope` to also require a specific `scp` scope. Errors are returned as RFC 7807 problem details; invalid input is a `400` validation problem. Requests are rate limited per user (stricter on account deletion).

### Authentication — `/api/auth`

| Method | Endpoint | Description |
|--------|----------|-------------|
| `POST` | `/sync` | Create or refresh the local user after Entra sign-in |
| `POST` | `/logout` | Server-side hook for sign-out (the client clears its token cache) |
| `GET` | `/me` | Get the currently authenticated user |
| `PUT` | `/profile` | Update display name |
| `DELETE` | `/user` | Permanently delete the Entra identity and all data |

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

### Health — `/api/health`

| Method | Endpoint | Description |
|--------|----------|-------------|
| `GET` | `/` | Anonymous probe; reports database connectivity (`503` when down) |

---

## Project Structure

```
HexFund.API/
├── Controllers/          # HTTP endpoints
├── Extensions/           # IServiceCollection extension methods (DI registration)
├── Middleware/           # ApiExceptionHandler (problem details)
├── Migrations/           # EF Core migrations
└── Program.cs            # App bootstrap

HexFund.Application/
├── DTOs/                 # Request/response models
├── Calculation/          # Pure recurrence + balance maths (no I/O)
├── Interfaces/           # Service and cache interfaces
├── Services/             # Business logic (Auth, Account, Transaction, CashFlow)
└── Validation/           # Input normalisation and date-range validation

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

**Transaction amendment** — Rather than editing a recurring transaction in-place (which would silently alter its history), the `Amend` endpoint end-dates the original row and creates a successor with `PredecessorTransactionId` set, in one atomic save. A transaction can only be amended once (amend its latest version instead), and one-time transactions cannot be amended. Deleting a successor restores its predecessor atomically. Superseded rows are hidden from the default list.

**Recurrence expansion** — Recurring transactions are stored as templates (a single DB row with a `FrequencyType`). `RecurrenceCalculator` expands them into occurrences at query time, jumping straight to the requested window, so nothing extra is persisted. Occurrences keep the template time of day, monthly series clamp to the last day of short months, and every series includes its own start date.

**Balance calculation** — Balances are calculated on the fly from `InitialBalance` + all transaction occurrences up to a given date. There is no stored `CurrentBalance` that can drift out of sync.

**Caching** — Calendar reads (monthly overview, per-day and per-month occurrences) are cached in `IMemoryCache` inside `CashFlowService`, under keys that include the user id. Any transaction or account write evicts everything tagged with that account (`ICacheService.InvalidateAccount`), so reads are consistent after a write. Account balances are not cached.

**Identity** — The Entra object id is the local `UserId`, so controllers derive the user straight from the token. `POST /api/auth/sync` creates the user on first sign-in. Users are never matched by email.

**Two-query account load** — `GetByUserIdWithTransactionsUpToDateAsync` fetches accounts and their transactions in two separate queries rather than a JOIN, avoiding the Cartesian row explosion that occurs when accounts have many transactions.
