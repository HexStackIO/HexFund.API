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
| **Tests** | 158 xUnit tests (`dotnet test`) |

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
| **In-app feedback** | Users send a bug, idea or question (with optional device diagnostics). It is stored immediately and forwarded in the background as a private issue for triage, without any user identifier. |
| **Account deletion** | One call removes the user's sign-in identity and all of their data. This is what app-store policies require. |

### What the user would notice (selling points grounded in the implementation)

- Forward-looking by design: balances and "days below zero" are first-class results, not an afterthought.
- History-preserving edits: amending a recurring item never rewrites the past.
- Privacy-minded data model: the backend stores a name, an email address, the user's own financial entries, and any feedback they choose to send. Passwords are never stored (sign-in is delegated to Entra), feedback issues carry no user identifier, and a full deletion endpoint exists.
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
- **Feedback pipeline** (see the next section): `FeedbackService` stores a report and wakes `FeedbackDispatcherHost`, a background service that runs `FeedbackDispatchProcessor` to create issues through `IIssueTracker` (`GitHubIssueTracker`, or `NoOpIssueTracker` when GitHub is disabled). `IssueDraftBuilder` is the pure class that builds the issue text, and `FeedbackRetentionProcessor` removes old delivered reports.

### Feedback pipeline

The API is the only component that talks to GitHub; the mobile app never holds a GitHub credential.

```
App --POST /api/feedback--> API --stores a Pending row--> PostgreSQL (the outbox)
                                          |
                          FeedbackDispatcherHost (background)
                                          |  GitHub App installation token
                                          v
                  private repo (issue labelled needs-triage, source-app, bug|feature|question)
```

1. **Submit.** `POST /api/feedback` validates and normalises the report and saves it with status `Pending`. It returns `202 Accepted` right away; creating the issue is never done inline. Saving the row also wakes the dispatcher.
2. **Deliver.** The dispatcher claims due rows in batches of 10 with one atomic `UPDATE ... FOR UPDATE SKIP LOCKED` that also reserves each row for a 5 minute lease. It builds the issue with `IssueDraftBuilder`, creates it, and marks the row `Sent` with the issue number. When it runs: once at startup, whenever a report is submitted, and every 15 seconds while the app is awake.
3. **Retry.** Failures are retried after 1 minute, 5 minutes, 30 minutes, 2 hours, then every 6 hours, each spread by up to 20% either way. A longer `Retry-After` from GitHub wins. After 8 attempts, or on an error that can never succeed (for example a rejected payload), the row becomes `Failed` and is logged at Error. Before any retry the dispatcher looks for an issue carrying the report's marker, so an issue that was created but not acknowledged (timeout, crash) is adopted rather than duplicated.
4. **Retain.** Once a day, `Sent` rows older than `Feedback:RetentionMonths` (default 12) are deleted. `Pending` and `Failed` rows are never deleted by age. Deleting a row does not delete its issue.

**What goes into an issue.** A title like `[Bug] first line of the report` (at most 70 characters, with `@` and `#` neutralised so it cannot mention anyone or link to another issue); the labels `needs-triage`, `source-app` and the type label (`bug`, `feature` or `question`); and a body with a hidden `<!-- hexfund-feedback:<id> -->` marker, the submission time, the report text inside a code fence longer than any backtick run in the text, then the diagnostics and a table of recent API errors (every value rendered as an escaped code span). It never contains the user id, email, name, IP address, account names or amounts. The feedback id is the only link back to a person, and it exists only in the database.

**GitHub App.** The API authenticates as a GitHub App: it signs a short-lived RS256 JWT with the app private key, exchanges it for an installation token, and caches that token until about 5 minutes before it expires. The key, JWT and token are never logged. With `GitHub:Enabled=false` (local development, tests) a no-op tracker marks reports sent with a fake issue number, so nothing needs GitHub credentials.

**Free-tier behavior.** The app runs on the Azure App Service free tier, which has no Always On, so the process is unloaded when idle and the dispatcher only runs while the app is awake. A report submitted while GitHub is down stays `Pending` in the database and is delivered the next time the app starts or receives a request. Submitting is never blocked by GitHub being unavailable.

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

### Feedback: `/api/feedback`

| Method | Path | Description |
|---|---|---|
| POST | `/` | Submit a bug, idea or question. Limited to 5 per hour per user and 64 KB per request. Returns `202 Accepted` with `{ "id": "<guid>" }`. |

Request body:

```json
{
  "type": 0,
  "body": "Calendar shows the rent on the wrong day...",
  "diagnostics": {
    "appVersion": "1.9", "buildNumber": "23", "platform": "Android", "osVersion": "16",
    "deviceModel": "Google Pixel 8", "screen": "calendar", "theme": "Gold",
    "recentErrors": [
      { "at": "2026-10-20T14:01:22Z", "method": "GET", "path": "accounts/{id}/monthly-overview",
        "status": 500, "error": null, "correlationId": "b1c7..." }
    ]
  }
}
```

| Field | Rule |
|---|---|
| `type` | Required. `0` Bug, `1` Idea, `2` Question. Values are never renumbered. |
| `body` | Required, 10 to 4000 characters. Newlines and tabs are kept; other control characters are removed; CRLF becomes LF. |
| `diagnostics` | Optional. Send null or omit it when the user switched diagnostics off. Limits: `appVersion`, `buildNumber`, `platform` 20; `osVersion`, `theme` 30; `deviceModel` 80; `screen` 50. |
| `diagnostics.recentErrors` | At most 20 entries (more is a 400). `method` 10, `path` 200, `error` 100, `correlationId` 64, `status` 0 to 599. The server cuts `path` at the first `?` and replaces GUID segments with `{id}`, and drops entries whose `at` is not a valid date. |

Responses: `202` accepted; `400` validation problem; `401`; `404` if the user has not called
`POST /api/auth/sync` yet; `413` over the size limit; `429` with a `Retry-After` header in seconds.
A `202` means the report is stored, not that the issue exists yet.

### Correlation ids

Every response carries an `X-Correlation-Id` header. A client may send its own on a request
(8 to 64 letters, digits or hyphens; anything else is replaced with a generated GUID), and may send
`X-App-Version` (up to 20 characters, sanitised). Both appear on every log line for the request, and
error responses repeat the id in a `correlationId` field of the problem details. Quote the id when
reporting a problem.

### Health: `/api/health`

Anonymous. Returns 200 with `status`, `database`, `timestamp`, `version`, or 503 when the database
cannot be reached.

### Errors

Errors are RFC 7807 problem details. Invalid input is a 400 `ValidationProblemDetails` with an
`errors` object keyed by field. Other statuses: 401 (no or bad token, or missing identity claim),
404 (not found or not owned by the caller), 409 (duplicate category), 413 (request body too large),
429 (rate limit, with `Retry-After`), 503 (health check only), 500 (generic message; details are
logged). Every problem-details body includes a `correlationId`.

---

## 5. Data model

PostgreSQL, managed by EF Core migrations. All timestamps are `timestamptz` in UTC.

```
User 1 ---- * Account 1 ---- * Transaction
 |                               ^
 |                               +-- PredecessorTransactionId (self reference, nullable)
 +---- * UserCategory
 |
 +---- * Feedback (outbox rows; one per submitted report)
```

| Table | Key columns | Notes |
|---|---|---|
| `Users` | `UserId` (= Entra object id), `EntraObjectId` (unique), `Email`, `FirstName`, `LastName`, `HasCustomName`, `CreatedAt`, `LastLoginAt` | No password column. Email is informational and not unique. |
| `Accounts` | `AccountId`, `UserId`, `AccountName`, `InitialBalance`, `Currency`, `IsActive`, `CreatedAt`, `ModifiedAt` | Cascade delete from the user. |
| `Transactions` | `TransactionId`, `AccountId`, `Description`, `Amount`, `Category`, `Frequency`, `StartDate`, `EndDate`, `IsActive`, `Color`, `PredecessorTransactionId`, `CreatedAt`, `ModifiedAt` | Cascade from the account. Self reference is `SET NULL`. Index on `(AccountId, StartDate)`. |
| `UserCategories` | `CategoryId`, `UserId`, `Name`, `CreatedAt` | Unique on `(UserId, Name)`. |
| `Feedback` | `Id`, `UserId`, `Type` (0 Bug, 1 Idea, 2 Question), `Body`, `DiagnosticsJson` (`jsonb`, nullable), `OutboxStatus` (0 Pending, 1 Sent, 2 Failed), `Attempts`, `NextAttemptAt`, `LastError`, `GitHubIssueNumber`, `CreatedAt`, `ModifiedAt` | Cascade delete from the user. Index on `(OutboxStatus, NextAttemptAt)` for the dispatcher. While a row is claimed, `NextAttemptAt` holds the lease expiry. |

Deleting a user is a single `DELETE` and the database cascades through accounts, transactions,
categories and feedback rows in dependency order.

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
| Rate limiting | 300 requests per minute per user (IP address if unauthenticated); 3 account deletions per 10 minutes; 5 feedback reports per hour. Rejections are 429 problem details with `Retry-After`. Counters are per server instance. |
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
| `GitHub__Enabled` | `true` to create real issues. When false a no-op tracker is used and no other `GitHub__*` setting is needed. When true, startup fails if any of the next four is missing or malformed. |
| `GitHub__AppId` | The GitHub App id (or client id), used as the JWT issuer. |
| `GitHub__InstallationId` | The id of the app installation on the tracker repository. |
| `GitHub__PrivateKeyBase64` | The app private key PEM, base64-encoded so it fits in an environment variable. Secret. |
| `GitHub__TrackerRepo` | The private repository that receives issues, as `owner/name`. |
| `Feedback__RetentionMonths` | How long delivered reports are kept. Default 12, allowed 1 to 60. |

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

158 unit tests, no database or network required:

| Area | What is covered |
|---|---|
| Recurrence | Every frequency, month-end clamping, leap years, time of day, end dates, windows that start mid-series, first/third Friday edge cases. |
| Balances | Accumulation over mixed series, inactive and future items, ended series, a 26-year daily series. |
| Transactions | Atomic amend, rejected amends, history filtering, ownership, route/account mismatch, cache invalidation on every write. |
| Cash flow | Monthly overview figures, grouping by day, no cross-user cache hits. |
| Cache | Hits, null results, per-account eviction, invalidation racing a build. |
| Validation | Normalisation, amounts, colours, currencies, date ranges, feedback limits and path cleaning. |
| Correlation ids | Valid, malformed and missing ids; version sanitising; the header and problem-details field on error responses; the 413 mapping. |
| Feedback | Storing pending rows, waking the dispatcher, diagnostics serialisation, issue titles/bodies (fencing, escaping, no identifiers). |
| Delivery | Claim and drain, retry schedule and jitter, the attempt cap, permanent failures, `Retry-After`, adopting an already created issue, retention. |
| GitHub client | App JWT shape and signature, token caching, create and find calls, error classification, rate-limit headers, option validation. |
| Rate limiting | The 5-per-hour feedback policy, per-user counting, 429 with `Retry-After`. |

Not covered yet: repository queries against a real PostgreSQL (including the atomic claim and the
user-delete cascade), the Graph client, migrations, and requests through real Kestrel (for example
the 64 KB body limit). See enhancement ideas.

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
- Each request logs method, path, status, duration, the correlation id and the client app version.
- Feedback report text is never logged at Information or above; only ids, statuses and attempt counts are.
- The dispatcher logs a warning when the oldest pending report has been waiting more than an hour, and an error when a report is marked `Failed`. Check the log stream after changing GitHub settings.

### Operations note: feedback follow-up

Triage happens on the issues in the private tracker repository, which hold no personal data. To
contact the person behind an issue (for follow-up only), take the id from the issue's hidden marker
line, `<!-- hexfund-feedback:<id> -->`, and look it up in the database:

```sql
SELECT f."Id", f."Type", f."CreatedAt", u."Email", u."FirstName"
FROM "Feedback" f
JOIN "Users" u ON u."UserId" = f."UserId"
WHERE f."Id" = '<id>';
```

If the user has deleted their account the row is gone and the issue is de-identified text that can
no longer be linked to anyone. Issues are not deleted with the account (see limitations). When a
user asks for their reports to be removed, a maintainer redacts and closes the matching issues by
hand. While the account still exists, list its report ids with
`SELECT "Id" FROM "Feedback" WHERE "UserId" = '<user id>'` and search the tracker repository for each
marker. After the account is gone the only way to find an issue is the text the person describes.

To see reports that need attention: `SELECT "Id", "Attempts", "LastError", "NextAttemptAt" FROM "Feedback" WHERE "OutboxStatus" = 2;`
(status 2 is `Failed`). To retry one after fixing the cause, set `OutboxStatus` back to 0 and
`NextAttemptAt` to now.

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
| Feedback goes through a database outbox | The user gets a fast answer and the report survives a GitHub outage or an app restart. The row is the single source of truth until the issue exists. |
| The API alone talks to GitHub, as a GitHub App | The mobile app never holds a credential. An app installation token is short-lived and scoped to one repository, unlike a personal access token. |
| Plain `HttpClient` for GitHub instead of Octokit | Only three calls are needed, so a thin wrapper means no extra dependency and is easy to test with a fake handler. |
| Look up existing issues by listing, not searching | Search is eventually consistent and may not find an issue created seconds ago; listing reads the stored body. |
| Issue text is built by a pure class with fenced, escaped user text | A report can never break out of its block, ping someone, or leak an identifier into the tracker. |

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
- **Issues outlive accounts.** Deleting an account removes the user's feedback rows but not the issues already created from them. The issues carry no identifier, yet the text is free-form and could contain personal details the user typed, so a maintainer redacts and closes them by hand on request.
- **The dispatcher only runs while the app is awake.** The free App Service tier has no Always On. Reports wait in the database and go out at the next startup or request; submitting is unaffected. The dispatcher also assumes a single instance for convenience, though the atomic claim keeps overlapping instances from sending the same report twice.
- **Feedback is write-only for the user.** There is no endpoint to see the status of a submitted report, and no inbound link from GitHub back to the app.
- **Not covered by automated tests:** repositories on real PostgreSQL (the atomic claim, the delete cascade), migrations, and requests through real Kestrel.

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
- A "My feedback" endpoint and status updates (for example a GitHub webhook that marks a report resolved), once there is a reason to tell users what happened to a report.
- Attach a screenshot to a report; automatic redaction of obvious personal data in report text.
- Run the dispatcher on a schedule (a timer-triggered job) or move to a plan with Always On if report latency while idle ever matters.

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
| **Correlation id** | A per-request id (`X-Correlation-Id`) that ties a response, its error body and the server log lines together. |
| **Feedback id** | The id of a stored report. It appears in the issue as a hidden marker and is the only link from an issue back to a person, via the database. |
| **Outbox** | A table of work still to be delivered (here, the `Feedback` rows). The row is saved first and delivered later, so nothing is lost if the destination is down. |
| **Dispatcher** | The background service that claims pending feedback rows and turns them into issues. |
| **Lease** | A time-limited reservation on a claimed row so no other worker takes it; if the worker dies the lease expires and the row becomes due again. |
| **Tracker repo** | The private GitHub repository that receives feedback issues. |
| **GitHub App** | The GitHub integration identity the API uses to create issues, authenticated with a private key and short-lived installation tokens. |
