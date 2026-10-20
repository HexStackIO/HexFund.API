# HexFund API

Backend for **HexFund**, a personal finance planner built around one idea: know what your balance
will be on any future day. Users record one-time and recurring income and expenses across their
accounts, and the API turns that into balances, a calendar of upcoming activity, and monthly
summaries.

This document is meant to be a complete reference for the backend: what the product does, how the
system is built, how to run and deploy it, and where it could go next. It is written so it can be
pasted into a conversation as background for product, marketing or engineering discussions.

| | |
|---|---|
| **Repository** | `HexStackIO/HexFund.API` (this repo). The mobile client lives in `HexStackIO/HexFund.UI`. |
| **Status** | Closed beta. Not yet in general production use. |
| **Stack** | .NET 10, ASP.NET Core controllers, EF Core 8, PostgreSQL, Microsoft Entra External ID |
| **Hosting** | Azure App Service, deployed from GitHub Actions |
| **Tests** | 46 xUnit tests (`dotnet test`) |

## Contents

1. [Product overview](#1-product-overview)
2. [Core concepts and domain rules](#2-core-concepts-and-domain-rules)
3. [Architecture](#3-architecture)
4. [API reference](#4-api-reference)
5. [Data model](#5-data-model)
6. [Authentication and security](#6-authentication-and-security)
7. [Configuration](#7-configuration)
8. [Getting started](#8-getting-started)
9. [Testing](#9-testing)
10. [Build, deploy and operations](#10-build-deploy-and-operations)
11. [Design decisions](#11-design-decisions)
12. [Known limitations](#12-known-limitations)
13. [Enhancement ideas](#13-enhancement-ideas)
14. [Glossary](#14-glossary)

---

## 1. Product overview

### What HexFund is

HexFund is a cash-flow planner for individuals. Instead of only recording what already happened,
it lets people describe their money as it will keep happening (rent every month, paychecks every
other Friday, a subscription that ends in June) and shows the resulting balance day by day.

### The problem it addresses

Most budgeting tools report the past. Someone living paycheck to paycheck usually wants the
opposite: "Will I be short before the 15th?" and "What does next month look like if I change this
bill?". HexFund answers that by expanding recurring items into dated occurrences and
accumulating them against an account balance.

### What the backend provides today

| Capability | What it means for the user |
|---|---|
| **Multiple accounts** | Track checking, savings and similar accounts separately, each with an opening balance and a currency label. |
| **One-time and recurring transactions** | Seven frequencies: once, daily, weekly, every two weeks, first and third Friday of the month, monthly, every two months. Income is positive, expenses negative. |
| **Accurate future balances** | The balance on any date is the opening balance plus every occurrence up to that date. Nothing is stored that could drift out of sync. |
| **Calendar data** | What happens on a single day, or across a whole month, including recurring items projected forward. |
| **Monthly overview** | Opening and closing balance, total income and expenses, average, highest and lowest balance, number of days in the red, a daily breakdown, and a spend breakdown by category. |
| **Change a recurring bill from a date forward ("amend")** | Raise the rent from June without rewriting the history before June. The old version is kept and linked to the new one. |
| **Categories** | A per-user list that works across all of the user's accounts. |
| **Colours** | Optional colour per transaction for the calendar. |
| **Profile and sign-in** | Sign-in is handled by Microsoft Entra External ID. The user can edit their display name. |
| **Account deletion** | One call removes the user's sign-in identity and all of their data. This is what app-store policies require. |

### What the user would notice (selling points grounded in the implementation)

- Forward-looking by design: balances and "days below zero" are first-class results, not an afterthought.
- History-preserving edits: amending a recurring item never rewrites the past.
- Privacy-minded data model: the backend stores a name, an email address, and the user's own financial entries. Passwords are never stored (sign-in is delegated to Entra), and a full deletion endpoint exists.
- Fast to read: calendar and overview responses are cached per user and refreshed automatically after any change.

### Audience and positioning

HexFund targets individuals who plan around recurring income and bills. Nothing in the code
connects to banks, so all data is entered by the user. That is a deliberate simplification (no
bank credentials, no aggregator fees) and also a limitation (see sections 12 and 13).

---

## 2. Core concepts and domain rules

### Money and balances

- Amounts are `decimal` with two decimal places. Positive is income, negative is an expense. Zero is not allowed. Limit is +/- 1,000,000,000.
- An account stores only its **opening balance**. The current balance is computed on request: opening balance plus every occurrence on or before the chosen moment.
- Only **active** transactions count. Setting a transaction inactive removes it from balances and the calendar without deleting it.
- The currency is a 3-letter code stored on the account. There is no conversion between currencies.

### Transactions and recurrence

A transaction is stored once, as a **template**: a start date, an optional end date, an amount, and
a frequency. Occurrences are computed when needed and never saved.

| Frequency | Value | Rule |
|---|---|---|
| Once | 0 | Only the start date. |
| Daily | 1 | Every day from the start date. |
| Weekly | 2 | Every 7 days. |
| Bi-weekly | 3 | Every 14 days. |
| First and third Friday | 4 | The start date itself, then the 1st and 3rd Friday of each month after it. |
| Monthly | 5 | Same day of the month. A day that does not exist is moved to the month's last day (31 Jan, 28 Feb, 31 Mar). |
| Bi-monthly | 6 | Every second month, same day rule as monthly. |

Rules that apply to all frequencies:

- The start date is always the first occurrence, and every occurrence keeps the start date's **time of day**.
- The end date is inclusive. After it, nothing is generated.
- All calculation is done in UTC. A "day" in snapshots and calendar grouping means a UTC calendar day.
- The numeric values above are persisted and sent to clients. They must never be renumbered.

### Amending a recurring transaction

"Amend" changes a recurring transaction from an **effective date** forward without touching the past:

1. The original is end-dated to the day before the effective date.
2. A successor is created starting on the effective date. It inherits everything except the fields the request overrides (amount is always supplied; description, category and colour are optional).
3. The successor stores the original's id as its **predecessor**.

Both changes are saved atomically. Constraints: only recurring transactions can be amended, the
effective date must be after the start date (and not after an existing end date), and a transaction
can only be amended once (amend its latest version instead). Deleting a successor gives the
predecessor its end date back. By default the transaction list hides rows that have been
superseded; `?includeHistory=true` returns everything.

### Categories

Categories are a per-user list with unique names (compared case-insensitively). Transactions hold
their category as **plain text**, so the list is a convenience for pickers, not a foreign key.
Deleting a category never alters existing transactions.

### Validation rules (summary)

| Field | Rule |
|---|---|
| Text fields | Trimmed, control characters removed. No HTML or SQL scrubbing (queries are parameterised and the API returns JSON). |
| Description | Required, up to 255 characters. |
| Category / category name | Up to 100 characters. Blank category becomes null. |
| Account name | Required, up to 100 characters. |
| Currency | Exactly three letters, normalised to upper case. |
| Colour | `#RGB` or `#RRGGBB`, stored upper case. |
| Amount | Not zero, at most two decimals, within +/- 1,000,000,000. |
| Dates | Start date within 50 years of today; end date after start and at most 100 years later; amend effective date within 50 years of today. |
| Required fields | `Amount`, `Frequency`, `StartDate`, `InitialBalance`, `IsActive` (on updates) and `EffectiveDate` must be present. A missing value is a 400, never a silent default. |

---

## 3. Architecture

Layered solution with dependencies pointing inward:

```
HexFund.API  ->  HexFund.Infrastructure  ->  HexFund.Application  ->  HexFund.Core
(HTTP)           (EF Core, cache, Graph)      (services, rules)        (entities, contracts)
```

| Project | Responsibility | Notable contents |
|---|---|---|
| `HexFund.Core` | Entities, enums and repository interfaces. No dependencies. | `User`, `Account`, `Transaction`, `UserCategory`, `FrequencyType`, `I*Repository` |
| `HexFund.Application` | Business rules. Depends only on Core. | `Services/` (Account, Auth, Category, Transaction, CashFlow), `Calculation/` (pure recurrence and balance maths), `DTOs/`, `Validation/`, `Interfaces/` |
| `HexFund.Infrastructure` | Everything that touches the outside world. | `Data/HexFundDbContext`, `Repositories/`, `Caching/CacheService`, `Services/EntraUserService` (Microsoft Graph), EF `Migrations/` |
| `HexFund.API` | HTTP surface and composition root. | `Controllers/`, `Middleware/ApiExceptionHandler`, `Extensions/` (DI, auth, rate limiting, CORS, Swagger), `Program.cs` |
| `HexFund.Tests` | xUnit tests with in-memory fakes. | `Calculation/`, `Services/`, `Validation/` |

### Request flow

`Request -> response compression -> exception handler -> request logging -> CORS -> authentication
-> authorization (fallback policy) -> rate limiter -> controller -> service -> repository -> PostgreSQL`

Controllers are thin: they read the user id from the token, call one service method, and map the
result to a status code. Services return `null` or `false` for "not found or not yours" and throw
`ArgumentException` for invalid operations on something that exists; the exception handler turns
that into a 400.

### Key components

- **`RecurrenceCalculator`** (pure, no I/O) lists the occurrence dates of a template inside a window. Daily and weekly series jump straight to the window instead of walking from the start date, so long-running series are cheap.
- **`BalanceCalculator`** sums occurrences up to a moment.
- **`CashFlowService`** builds projections, the monthly overview and the per-day and per-month occurrence lists, and owns the caching of those reads.
- **`CacheService`** wraps `IMemoryCache` and tags entries by account. Each account owns a cancellation token; cancelling it expires every entry for that account at once. It is registered as a singleton.
- **`ApiExceptionHandler`** converts exceptions to RFC 7807 problem details (400 for `ArgumentException`, 401 for missing identity, 500 with a generic message otherwise).
- **`HexFundDbContext`** applies UTC conversion to all timestamps and stamps `CreatedAt` / `ModifiedAt` in one place.

### Caching

Only expensive calendar reads are cached, inside `CashFlowService`:

| Data | Lifetime |
|---|---|
| Monthly overview | 3 minutes |
| Occurrences for a day, or for a month | 2 minutes |

Keys include the **user id**, so one user can never be served another user's cached entry. Any
transaction create, update, delete or amend, and any account delete, evicts everything tagged with
that account. Account lists and balances are not cached. The cache lives in process memory, so each
server instance has its own (see limitations).

---

## 4. API reference

Base path `/api`. All endpoints require a bearer token except `GET /api/health`. In Development,
Swagger UI is served at the site root. JSON uses camelCase and omits null properties.

### Auth: `/api/auth`

| Method | Path | Description |
|---|---|---|
| POST | `/sync` | Create the local user on first sign-in, or refresh login time, email and (unless customised) name. Call once after each sign-in. |
| POST | `/logout` | Server-side hook only; the client clears its own token cache. |
| GET | `/me` | The current user. |
| PUT | `/profile` | Update first and last name. |
| DELETE | `/user` | Permanently delete the Entra identity and all data. Rate limited to 3 per 10 minutes. 204 on success. |

### Accounts: `/api/accounts`

| Method | Path | Description |
|---|---|---|
| GET | `/` | All accounts with computed `currentBalance`. |
| GET | `/{id}` | One account. |
| POST | `/` | Create. Body: `accountName`, `initialBalance`, `currency`. |
| PUT | `/{id}` | Update `accountName` and `isActive`. |
| DELETE | `/{id}` | Delete the account and its transactions. |

### Transactions

| Method | Path | Description |
|---|---|---|
| GET | `/api/accounts/{accountId}/transactions` | List. `?includeHistory=true` includes superseded rows. 404 if the account is not the caller's. |
| GET | `/api/transactions/{id}` | One transaction. |
| POST | `/api/accounts/{accountId}/transactions` | Create. |
| PUT | `/api/accounts/{accountId}/transactions/{id}` | Replace all editable fields. The account in the URL must match the transaction's account. |
| DELETE | `/api/accounts/{accountId}/transactions/{id}` | Delete. If it is an amendment successor, the predecessor gets its end date back. |
| POST | `/api/accounts/{accountId}/transactions/{id}/amend` | Amend from `effectiveDate` forward. Returns 201 with the successor. |
| GET | `/api/accounts/{accountId}/transactions-for-date?date=yyyy-MM-dd` | Occurrences on one day. |
| GET | `/api/accounts/{accountId}/transactions-for-month?year=&month=` | Occurrences for a month as an object keyed by `yyyy-MM-dd`. |

Transaction body (create / update): `description`, `amount`, `frequency` (0 to 6), `startDate`,
optional `endDate`, optional `category`, optional `color`; update also needs `isActive`. Amend
body: `effectiveDate`, `amount`, optional `description`, `category`, `color`.

Occurrence objects carry the **template's** `transactionId`, the `occurrenceDate`, amount,
category, frequency and colour, so a client can open the source transaction from any occurrence.

### Projections: `/api/accounts/{accountId}`

| Method | Path | Description |
|---|---|---|
| GET | `/monthly-overview?year=&month=` | Opening and closing balance, income, expenses, average, highest and lowest balance, days below zero, `dailyBreakdown`, `categoryBreakdowns`. Defaults to the current UTC month. |
| GET | `/cashflow?startDate=&endDate=` | Full projection with one snapshot per day and every occurrence with its running balance. Defaults to the current month; range limited to one year. |

### Categories: `/api/categories`

| Method | Path | Description |
|---|---|---|
| GET | `/` | The user's categories, ordered by name. |
| POST | `/` | Create. 409 if the name already exists. |
| DELETE | `/{id}` | Delete. Transactions keep their category text. |

### Health: `/api/health`

Anonymous. Returns 200 with `status`, `database`, `timestamp`, `version`, or 503 when the database
cannot be reached.

### Errors

Errors are RFC 7807 problem details. Invalid input is a 400 `ValidationProblemDetails` with an
`errors` object keyed by field. Other statuses: 401 (no or bad token, or missing identity claim),
404 (not found or not owned by the caller), 409 (duplicate category), 429 (rate limit), 503 (health
check only), 500 (generic message; details are logged).

---

## 5. Data model

PostgreSQL, managed by EF Core migrations. All timestamps are `timestamptz` in UTC.

```
User 1 ---- * Account 1 ---- * Transaction
 |                               ^
 +---- * UserCategory            +-- PredecessorTransactionId (self reference, nullable)
```

| Table | Key columns | Notes |
|---|---|---|
| `Users` | `UserId` (= Entra object id), `EntraObjectId` (unique), `Email`, `FirstName`, `LastName`, `HasCustomName`, `CreatedAt`, `LastLoginAt` | No password column. Email is informational and not unique. |
| `Accounts` | `AccountId`, `UserId`, `AccountName`, `InitialBalance`, `Currency`, `IsActive`, `CreatedAt`, `ModifiedAt` | Cascade delete from the user. |
| `Transactions` | `TransactionId`, `AccountId`, `Description`, `Amount`, `Category`, `Frequency`, `StartDate`, `EndDate`, `IsActive`, `Color`, `PredecessorTransactionId`, `CreatedAt`, `ModifiedAt` | Cascade from the account. Self reference is `SET NULL`. Index on `(AccountId, StartDate)`. |
| `UserCategories` | `CategoryId`, `UserId`, `Name`, `CreatedAt` | Unique on `(UserId, Name)`. |

Deleting a user is a single `DELETE` and the database cascades through accounts, transactions and
categories in dependency order.

Migrations live in `src/HexFund.Infrastructure/Migrations` (the DbContext's assembly).

---

## 6. Authentication and security

### Sign-in

Users sign in on the client with Microsoft Entra External ID (MSAL). The API validates the
resulting JWT (`Microsoft.Identity.Web`, authority built from `AzureAd:Instance` and
`AzureAd:TenantId`, audience `AzureAd:ClientId`).

- The Entra **object id is the user id**. Controllers read it from the `oid` claim, so no database lookup is needed per request.
- `POST /api/auth/sync` creates the local record on first sign-in. Other endpoints assume it exists.
- Users are never matched by email, because the email claim is not guaranteed to be verified.

### Controls in place

| Control | Detail |
|---|---|
| Authenticated by default | A fallback authorization policy requires a signed-in user on every endpoint; only `/api/health` opts out. |
| Optional scope check | Set `AzureAd:RequiredScope` to also require that value in the token's `scp` claim. Empty means off. |
| Ownership checks | Every query that takes an id also filters by the caller's user id (accounts, transactions, categories, cached data). |
| Rate limiting | 300 requests per minute per user (IP address if unauthenticated); 3 deletions per 10 minutes. Counters are per server instance. |
| Input handling | Strict validation; EF Core parameterised queries. |
| CORS | Development allows any origin. Production allows only `AllowedOrigins`. No credentialed CORS (tokens travel in the `Authorization` header). |
| Errors | Internal details are logged, not returned. |
| Secrets | Connection string and Entra client secret come from environment variables or user secrets, never from source. |
| Logging | Serilog to console and rolling files; EF query parameters are logged only in Development. |

### Account deletion

`DELETE /api/auth/user` first deletes the user in Entra through Microsoft Graph (app-only, needs the
`User.ReadWrite.All` application permission with admin consent). If Entra reports the user is already
gone, deletion continues. Any other Entra failure aborts before touching the database so the call can
be retried. Then one database `DELETE` removes everything the user owns.

---

## 7. Configuration

Set via `appsettings.json` (committed placeholders), `appsettings.Development.json` or user secrets
locally, and environment variables in Azure (use `__` for nesting, for example `AzureAd__ClientId`).

| Key | Purpose |
|---|---|
| `ConnectionStrings:DefaultConnection` | PostgreSQL connection string. |
| `AzureAd:Instance` | External ID authority base, for example `https://<tenant>.ciamlogin.com/`. |
| `AzureAd:TenantId`, `AzureAd:ClientId` | Tenant and API app registration. Used for token validation and Graph. |
| `AzureAd:ClientSecret` | Used only by the Graph client for account deletion. Environment variable only. |
| `AzureAd:RequiredScope` | Optional delegated scope to require. Empty disables the check. |
| `AllowedOrigins` | Array of allowed browser origins in production. |
| `Serilog:MinimumLevel` | Log levels. EF command logging is set to Warning by default. |

---

## 8. Getting started

### Prerequisites

| Tool | Version |
|---|---|
| .NET SDK | 10.0 |
| PostgreSQL | 14 or newer |
| EF Core CLI | Pinned in `.config/dotnet-tools.json`; run `dotnet tool restore` |

### Run locally

```bash
git clone https://github.com/HexStackIO/HexFund.API.git
cd HexFund.API
dotnet restore
dotnet tool restore

# Configure secrets (from src/HexFund.API)
cd src/HexFund.API
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Database=HexFund_Dev;Username=postgres;Password=<password>"
dotnet user-secrets set "AzureAd:TenantId" "<tenant-id>"
dotnet user-secrets set "AzureAd:ClientId" "<api-client-id>"
dotnet user-secrets set "AzureAd:ClientSecret" "<client-secret>"

dotnet run
```

In Development the app applies pending migrations at startup and serves Swagger at
`https://localhost:5001`. In production it never migrates itself.

### Database migrations

```bash
# from src/HexFund.API
dotnet tool run dotnet-ef migrations add <Name> --project ../HexFund.Infrastructure --startup-project .
dotnet tool run dotnet-ef database update --project ../HexFund.Infrastructure --startup-project .
```

### Repository layout

```
HexFund.API/
├── .config/dotnet-tools.json        pinned dotnet-ef
├── .github/workflows/               CI/CD
├── HexFund.sln
└── src/
    ├── HexFund.API/                 controllers, middleware, DI, Program.cs
    ├── HexFund.Application/         services, calculation, DTOs, validation
    ├── HexFund.Core/                entities, enums, repository interfaces
    ├── HexFund.Infrastructure/      EF Core, repositories, cache, Graph, migrations
    └── HexFund.Tests/               xUnit tests
```

---

## 9. Testing

```bash
dotnet test
```

46 unit tests, no database or network required:

| Area | What is covered |
|---|---|
| Recurrence | Every frequency, month-end clamping, leap years, time of day, end dates, windows that start mid-series, first/third Friday edge cases. |
| Balances | Accumulation over mixed series, inactive and future items, ended series, a 26-year daily series. |
| Transactions | Atomic amend, rejected amends, history filtering, ownership, route/account mismatch, cache invalidation on every write. |
| Cash flow | Monthly overview figures, grouping by day, no cross-user cache hits. |
| Cache | Hits, null results, per-account eviction, invalidation racing a build. |
| Validation | Normalisation, amounts, colours, currencies, date ranges. |

Not covered yet: controller and HTTP behaviour, repository queries against a real PostgreSQL, the
Graph client, and migrations. See enhancement ideas.

---

## 10. Build, deploy and operations

### CI/CD

`.github/workflows/master_hexfundapi.yml` runs on every push to `master` (and manually):

1. **build**: restore, build (Release), run tests, publish, upload the artifact.
2. **migrate** (environment `production`): restore the pinned `dotnet-ef` tool and run `database update` against the production database.
3. **deploy**: sign in to Azure with the OIDC secrets and deploy the artifact to the Azure Web App.

Migrations run before the new code is deployed, so a migration must stay compatible with the version
that is still running.

### Operations

- Logs go to the console (Azure log stream) and `logs/hexfund-<date>.log`, kept for 30 days.
- `GET /api/health` is suitable for uptime probes and reports database connectivity.
- Each request logs method, path, status and duration.

---

## 11. Design decisions

| Decision | Reason |
|---|---|
| Recurring items stored as templates, expanded on demand | Small storage, and changing the future is an amendment rather than editing thousands of rows. |
| Balances computed, not stored | A stored balance can drift out of sync with transactions. |
| Amend creates a linked successor | Past balances stay exactly as they were. |
| Entra object id as the user id | The user is known from the token alone; no lookup per request. |
| Cache keyed by user and tagged by account | Prevents cross-user leaks and makes invalidation one call. |
| Reads untracked by default | Faster queries; writes opt in explicitly. |
| Single `DELETE` for user and account removal | PostgreSQL resolves the self-referencing foreign key and cascades in the right order. |
| Migrations applied by the pipeline, not at startup | Failed migrations stop the deploy instead of leaving a half-started app. |
| Normalise input rather than scrub it | HTML or SQL "cleaning" corrupts legitimate text and adds no protection here. |

---

## 12. Known limitations

- **UTC days.** Calendar grouping and snapshots use UTC days. A user far from UTC can see a late-evening item land on the next calendar day.
- **Per-instance state.** The cache and rate limiter live in process memory. With more than one App Service instance, caches are not shared (a write only clears the instance that handled it) and limits apply per instance.
- **Cost grows with history.** Balances are recalculated from all transactions on each request. Fine at personal scale; very long daily series cost more.
- **No exceptions to a series.** A single occurrence cannot be skipped or changed on its own; the options are amend (forward) or end-date the series.
- **No transfers.** Moving money between two accounts is two unrelated transactions.
- **Category is free text.** Not enforced against the category list, and categories cannot be renamed.
- **Currency is a label.** No conversion or cross-account totals.
- **No pagination.** Transaction lists return everything for the account.
- **Concurrent first sign-in.** Two simultaneous `sync` calls for a brand-new user could collide on the primary key.
- **No API versioning.** Breaking changes require a coordinated client release.
- **Entra dependency for deletion.** Account deletion needs Graph permissions and connectivity; it fails safely and can be retried.
- **Not covered by automated tests:** HTTP layer, repositories on real PostgreSQL, migrations.

---

## 13. Enhancement ideas

Suggestions inferred from the code and its limits. None of these exist yet.

**Product**
- Skip or edit a single occurrence; "end this series today".
- Transfers between accounts and net-worth totals.
- Budgets per category with alerts, and category rename/merge.
- Low-balance alerts (push or email) using the existing "days below zero" data.
- Import (CSV/OFX) and optional bank linking via an aggregator.
- Savings goals and "what if" scenarios built on amendments.
- Multi-currency with exchange rates and a per-user timezone.
- Shared accounts for couples or households.
- Reports and export (CSV, PDF); yearly summaries.

**Engineering**
- Integration tests against PostgreSQL (Testcontainers) and `WebApplicationFactory` tests for the HTTP layer.
- Distributed cache and rate limiting (Redis) before scaling beyond one instance.
- Pagination and filtering on transaction lists; an ETag or `If-Modified-Since` option for calendar data.
- Make first sign-in idempotent under concurrency (upsert).
- API versioning and an OpenAPI contract published for the client.
- Structured metrics and tracing (OpenTelemetry) and Application Insights dashboards.
- Soft delete or export-before-delete for account removal.

---

## 14. Glossary

| Term | Meaning |
|---|---|
| **Account** | A named balance with an opening amount and currency label. |
| **Transaction** | A template describing money in or out, once or repeating. |
| **Occurrence** | One dated instance of a transaction, computed on demand. |
| **Template** | The single stored row behind a recurring transaction. |
| **Frequency** | How a transaction repeats (see section 2). |
| **Amend** | Change a recurring transaction from a date forward while preserving history. |
| **Predecessor / successor** | The older and newer versions linked by an amendment. |
| **Superseded** | A predecessor that has a successor; hidden from the default list. |
| **Effective date** | The first date the amended values apply to. |
| **Snapshot** | The balance summary for one calendar day (start, end, lowest, income, expenses). |
| **Monthly overview** | Aggregates and daily snapshots for one month plus the category breakdown. |
| **Entra External ID** | Microsoft's customer identity service used for sign-in. |
| **Object id (oid)** | The unique id of a user in Entra, reused as the HexFund user id. |
| **Problem details** | The standard (RFC 7807) JSON shape for errors. |
