using HexFund.Core.Entities;
using HexFund.Core.Enums;
using HexFund.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace HexFund.IntegrationTests;

/// <summary>
/// Behaviour that only a real PostgreSQL can prove: migrations, cascading deletes, the atomic
/// feedback claim, and column types. Skipped unless HEXFUND_TEST_DB is set.
/// </summary>
[Collection(PostgresCollection.Name)]
public class DatabaseTests
{
    private readonly PostgresFixture _db;

    public DatabaseTests(PostgresFixture db) => _db = db;

    private static DateTimeOffset Now => DateTimeOffset.UtcNow;

    [DatabaseFact]
    public async Task Migrations_CreateEveryTable()
    {
        await using var context = _db.CreateContext();

        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        Assert.False(await context.Users.AnyAsync(u => u.Email == "nobody"));
        Assert.False(await context.Accounts.AnyAsync(a => a.AccountId == Guid.Empty));
        Assert.False(await context.Transactions.AnyAsync(t => t.Description == "nobody"));
        Assert.False(await context.UserCategories.AnyAsync(c => c.Name == "nobody"));
        Assert.False(await context.Feedback.AnyAsync(f => f.Body == "nobody"));
    }

    [DatabaseFact]
    public async Task DeletingAUser_RemovesEverythingTheyOwn_IncludingFeedback()
    {
        var user = await _db.AddUserAsync();
        var other = await _db.AddUserAsync();
        var accountId = await SeedAmendedTransactionsAsync(user.UserId);
        await SeedAccountAsync(other.UserId);

        await using (var seed = _db.CreateContext())
        {
            seed.UserCategories.Add(new UserCategory { CategoryId = Guid.NewGuid(), UserId = user.UserId, Name = "Rent" });
            seed.Feedback.Add(NewFeedback(user.UserId));
            seed.Feedback.Add(NewFeedback(other.UserId));
            await seed.SaveChangesAsync();
        }

        await using var context = _db.CreateContext();
        var deleted = await new UserRepository(context).DeleteAsync(user.UserId);

        Assert.True(deleted);
        await using var check = _db.CreateContext();
        Assert.False(await check.Users.AnyAsync(u => u.UserId == user.UserId));
        Assert.False(await check.Accounts.AnyAsync(a => a.UserId == user.UserId));
        Assert.False(await check.Transactions.AnyAsync(t => t.AccountId == accountId));
        Assert.False(await check.UserCategories.AnyAsync(c => c.UserId == user.UserId));
        Assert.False(await check.Feedback.AnyAsync(f => f.UserId == user.UserId));

        // Another user's data is untouched.
        Assert.True(await check.Users.AnyAsync(u => u.UserId == other.UserId));
        Assert.True(await check.Feedback.AnyAsync(f => f.UserId == other.UserId));
    }

    [DatabaseFact]
    public async Task DeletingAnAccount_WithAnAmendmentChain_Succeeds()
    {
        var user = await _db.AddUserAsync();
        var accountId = await SeedAmendedTransactionsAsync(user.UserId);

        await using var context = _db.CreateContext();
        Assert.True(await new AccountRepository(context).DeleteForUserAsync(user.UserId, accountId));

        await using var check = _db.CreateContext();
        Assert.False(await check.Transactions.AnyAsync(t => t.AccountId == accountId));
    }

    [DatabaseFact]
    public async Task DeletingAPredecessor_LeavesTheSuccessorWithANullLink()
    {
        var user = await _db.AddUserAsync();
        var accountId = await SeedAccountAsync(user.UserId);
        var (predecessor, successor) = await AddAmendmentPairAsync(accountId);

        await using var context = _db.CreateContext();
        await context.Transactions.Where(t => t.TransactionId == predecessor).ExecuteDeleteAsync();

        await using var check = _db.CreateContext();
        var remaining = await check.Transactions.SingleAsync(t => t.TransactionId == successor);
        Assert.Null(remaining.PredecessorTransactionId);
    }

    [DatabaseFact]
    public async Task DeletingASuccessor_ReturnsItsEndDateToThePredecessor_Atomically()
    {
        var user = await _db.AddUserAsync();
        var accountId = await SeedAccountAsync(user.UserId);
        var (predecessor, successor) = await AddAmendmentPairAsync(accountId, successorEnd: Now.AddYears(1));

        await using var context = _db.CreateContext();
        Assert.True(await new TransactionRepository(context).DeleteAsync(successor));

        await using var check = _db.CreateContext();
        Assert.False(await check.Transactions.AnyAsync(t => t.TransactionId == successor));
        var restored = await check.Transactions.SingleAsync(t => t.TransactionId == predecessor);
        Assert.NotNull(restored.EndDate);
    }

    [DatabaseFact]
    public async Task Amend_SavesTheOriginalAndSuccessorTogether()
    {
        var user = await _db.AddUserAsync();
        var accountId = await SeedAccountAsync(user.UserId);
        var originalId = Guid.NewGuid();
        var original = NewTransaction(accountId, originalId, Now.AddMonths(-3));

        await using (var seed = _db.CreateContext())
        {
            seed.Transactions.Add(original);
            await seed.SaveChangesAsync();
        }

        original.EndDate = Now.AddDays(-1);
        var successor = NewTransaction(accountId, Guid.NewGuid(), Now);
        successor.PredecessorTransactionId = originalId;

        await using var context = _db.CreateContext();
        var repository = new TransactionRepository(context);
        await repository.AmendAsync(original, successor);

        Assert.True(await repository.HasSuccessorAsync(originalId));
        await using var check = _db.CreateContext();
        Assert.NotNull((await check.Transactions.SingleAsync(t => t.TransactionId == originalId)).EndDate);
        Assert.Equal(originalId, (await check.Transactions.SingleAsync(t => t.TransactionId == successor.TransactionId)).PredecessorTransactionId);
    }

    [DatabaseFact]
    public async Task Timestamps_AreStoredAndReturnedInUtc_AndAuditColumnsAreStamped()
    {
        var user = await _db.AddUserAsync();
        var accountId = await SeedAccountAsync(user.UserId);
        var local = new DateTimeOffset(2026, 3, 5, 9, 30, 0, TimeSpan.FromHours(-5));

        await using (var context = _db.CreateContext())
        {
            var transaction = NewTransaction(accountId, Guid.NewGuid(), local);
            transaction.CreatedAt = default;
            context.Transactions.Add(transaction);
            await context.SaveChangesAsync();

            await using var check = _db.CreateContext();
            var stored = await check.Transactions.SingleAsync(t => t.TransactionId == transaction.TransactionId);

            Assert.Equal(local.UtcDateTime, stored.StartDate.UtcDateTime);
            Assert.Equal(TimeSpan.Zero, stored.StartDate.Offset);
            Assert.True(stored.CreatedAt > Now.AddMinutes(-5));
        }
    }

    [DatabaseFact]
    public async Task Categories_RejectDuplicateNamesForTheSameUser_ButNotForAnotherUser()
    {
        var user = await _db.AddUserAsync();
        var other = await _db.AddUserAsync();

        async Task<bool> TryAdd(Guid userId, string name)
        {
            await using var context = _db.CreateContext();
            return await new CategoryRepository(context).TryCreateAsync(
                new UserCategory { CategoryId = Guid.NewGuid(), UserId = userId, Name = name });
        }

        Assert.True(await TryAdd(user.UserId, "Groceries"));
        Assert.False(await TryAdd(user.UserId, "Groceries"));
        Assert.True(await TryAdd(other.UserId, "Groceries"));

        await using var check = _db.CreateContext();
        Assert.True(await new CategoryRepository(check).ExistsAsync(user.UserId, "GROCERIES"));
    }

    [DatabaseFact]
    public async Task FeedbackDiagnostics_RoundTripThroughJsonb()
    {
        var user = await _db.AddUserAsync();
        var feedback = NewFeedback(user.UserId);
        feedback.DiagnosticsJson = """{"appVersion":"1.9","recentErrors":[{"status":500}]}""";

        await using (var context = _db.CreateContext())
            await new FeedbackRepository(context).AddAsync(feedback);

        await using var check = _db.CreateContext();
        var stored = await new FeedbackRepository(check).GetByIdAsync(feedback.Id);

        Assert.NotNull(stored);
        using var json = System.Text.Json.JsonDocument.Parse(stored!.DiagnosticsJson!);
        Assert.Equal("1.9", json.RootElement.GetProperty("appVersion").GetString());
        Assert.Equal(500, json.RootElement.GetProperty("recentErrors")[0].GetProperty("status").GetInt32());
    }

    [DatabaseFact]
    public async Task Feedback_RequiresAnExistingUser()
    {
        await using var context = _db.CreateContext();
        context.Feedback.Add(NewFeedback(Guid.NewGuid()));

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    // ---- helpers ----

    private async Task<Guid> SeedAccountAsync(Guid userId)
    {
        await using var context = _db.CreateContext();
        var account = new Account { AccountId = Guid.NewGuid(), UserId = userId, AccountName = "Main", Currency = "USD" };
        context.Accounts.Add(account);
        await context.SaveChangesAsync();
        return account.AccountId;
    }

    private async Task<Guid> SeedAmendedTransactionsAsync(Guid userId)
    {
        var accountId = await SeedAccountAsync(userId);
        await AddAmendmentPairAsync(accountId);
        return accountId;
    }

    private async Task<(Guid Predecessor, Guid Successor)> AddAmendmentPairAsync(Guid accountId, DateTimeOffset? successorEnd = null)
    {
        var predecessor = NewTransaction(accountId, Guid.NewGuid(), Now.AddMonths(-3));
        predecessor.EndDate = Now.AddDays(-1);

        var successor = NewTransaction(accountId, Guid.NewGuid(), Now);
        successor.PredecessorTransactionId = predecessor.TransactionId;
        successor.EndDate = successorEnd;

        await using var context = _db.CreateContext();
        context.Transactions.Add(predecessor);
        await context.SaveChangesAsync();
        context.Transactions.Add(successor);
        await context.SaveChangesAsync();
        return (predecessor.TransactionId, successor.TransactionId);
    }

    internal static Transaction NewTransaction(Guid accountId, Guid id, DateTimeOffset start) => new()
    {
        TransactionId = id,
        AccountId = accountId,
        Description = "Rent",
        Amount = -1000m,
        Frequency = FrequencyType.Monthly,
        StartDate = start,
        IsActive = true,
        CreatedAt = Now
    };

    internal static Feedback NewFeedback(Guid userId) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        Type = FeedbackType.Bug,
        Body = "The calendar shows rent on the wrong day.",
        OutboxStatus = FeedbackOutboxStatus.Pending,
        NextAttemptAt = Now,
        CreatedAt = Now
    };
}
