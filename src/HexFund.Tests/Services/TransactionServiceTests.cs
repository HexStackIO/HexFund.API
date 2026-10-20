using HexFund.Application.DTOs;
using HexFund.Application.Services;
using HexFund.Core.Entities;
using HexFund.Core.Enums;
using static HexFund.Tests.TestData;

namespace HexFund.Tests.Services;

public class TransactionServiceTests
{
    private readonly FakeAccountRepository _accounts = new();
    private readonly FakeTransactionRepository _transactions;
    private readonly RecordingCache _cache = new();
    private readonly TransactionService _service;

    private readonly Guid _userId = Guid.NewGuid();
    private readonly Account _account;

    public TransactionServiceTests()
    {
        _transactions = new FakeTransactionRepository(_accounts);
        _service = new TransactionService(_transactions, _accounts, _cache);
        _account = new Account { AccountId = Guid.NewGuid(), UserId = _userId, AccountName = "Main" };
        _accounts.Accounts.Add(_account);
    }

    private Transaction AddTx(FrequencyType frequency, DateTimeOffset start, DateTimeOffset? end = null)
    {
        var tx = Tx(frequency, start, end: end, accountId: _account.AccountId);
        _transactions.Transactions.Add(tx);
        return tx;
    }

    [Fact]
    public async Task Amend_EndDatesOriginal_AndCreatesLinkedSuccessor_Atomically()
    {
        var original = AddTx(FrequencyType.Monthly, Utc(2026, 1, 5));
        var request = new AmendTransactionRequest { Amount = -20m, EffectiveDate = Utc(2026, 4, 5) };

        var dto = await _service.AmendTransactionAsync(_account.AccountId, original.TransactionId, request, _userId);

        Assert.NotNull(dto);
        Assert.Equal(original.TransactionId, dto!.PredecessorTransactionId);
        Assert.Equal(Utc(2026, 4, 5), dto.StartDate);
        Assert.Equal(Utc(2026, 4, 4), original.EndDate);
        Assert.Equal(1, _transactions.AmendCalls);
        Assert.Contains(_account.AccountId, _cache.InvalidatedAccounts);
    }

    [Fact]
    public async Task Amend_RejectsOneTimeTransactions()
    {
        var once = AddTx(FrequencyType.Once, Utc(2026, 1, 5));
        var request = new AmendTransactionRequest { Amount = -20m, EffectiveDate = Utc(2026, 4, 5) };

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.AmendTransactionAsync(_account.AccountId, once.TransactionId, request, _userId));
    }

    [Fact]
    public async Task Amend_RejectsEffectiveDateNotAfterStart()
    {
        var original = AddTx(FrequencyType.Monthly, Utc(2026, 1, 5));
        var request = new AmendTransactionRequest { Amount = -20m, EffectiveDate = Utc(2026, 1, 5) };

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.AmendTransactionAsync(_account.AccountId, original.TransactionId, request, _userId));
        Assert.Equal(0, _transactions.AmendCalls);
    }

    [Fact]
    public async Task Amend_TwiceFromTheSameOriginal_IsRejected()
    {
        var original = AddTx(FrequencyType.Monthly, Utc(2026, 1, 5));
        var first = new AmendTransactionRequest { Amount = -20m, EffectiveDate = Utc(2026, 4, 5) };
        var second = new AmendTransactionRequest { Amount = -30m, EffectiveDate = Utc(2026, 6, 5) };

        await _service.AmendTransactionAsync(_account.AccountId, original.TransactionId, first, _userId);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.AmendTransactionAsync(_account.AccountId, original.TransactionId, second, _userId));
    }

    [Fact]
    public async Task History_HidesSupersededRows_UnlessRequested()
    {
        var original = AddTx(FrequencyType.Monthly, Utc(2026, 1, 5));
        var other = AddTx(FrequencyType.Monthly, Utc(2026, 1, 9), end: Utc(2026, 2, 9)); // ended, but not amended
        await _service.AmendTransactionAsync(_account.AccountId, original.TransactionId,
            new AmendTransactionRequest { Amount = -20m, EffectiveDate = Utc(2026, 4, 5) }, _userId);

        var visible = await _service.GetAccountTransactionsAsync(_account.AccountId, _userId);
        var all = await _service.GetAccountTransactionsAsync(_account.AccountId, _userId, includeHistory: true);

        Assert.DoesNotContain(visible!, t => t.TransactionId == original.TransactionId);
        Assert.Contains(visible!, t => t.TransactionId == other.TransactionId);
        Assert.Equal(3, all!.Count);
    }

    [Fact]
    public async Task OtherUsers_CannotSeeOrChangeTransactions()
    {
        var tx = AddTx(FrequencyType.Monthly, Utc(2026, 1, 5));
        var stranger = Guid.NewGuid();

        Assert.Null(await _service.GetAccountTransactionsAsync(_account.AccountId, stranger));
        Assert.Null(await _service.GetTransactionByIdAsync(tx.TransactionId, stranger));
        Assert.False(await _service.DeleteTransactionAsync(_account.AccountId, tx.TransactionId, stranger));
    }

    [Fact]
    public async Task Update_WithMismatchedAccountRoute_IsNotFound()
    {
        var tx = AddTx(FrequencyType.Monthly, Utc(2026, 1, 5));
        var otherAccount = new Account { AccountId = Guid.NewGuid(), UserId = _userId, AccountName = "Other" };
        _accounts.Accounts.Add(otherAccount);

        var request = new UpdateTransactionRequest
        {
            Description = "x", Amount = -5m, Frequency = FrequencyType.Monthly,
            StartDate = Utc(2026, 1, 5), IsActive = true
        };

        Assert.Null(await _service.UpdateTransactionAsync(otherAccount.AccountId, tx.TransactionId, request, _userId));
        Assert.Empty(_cache.InvalidatedAccounts);
    }

    [Fact]
    public async Task Mutations_InvalidateTheAccountCache()
    {
        var created = await _service.CreateTransactionAsync(_account.AccountId, new CreateTransactionRequest
        {
            Description = "x", Amount = -5m, Frequency = FrequencyType.Once, StartDate = Utc(2026, 1, 5)
        }, _userId);
        Assert.NotNull(created);

        await _service.DeleteTransactionAsync(_account.AccountId, created!.TransactionId, _userId);

        Assert.Equal(2, _cache.InvalidatedAccounts.Count(id => id == _account.AccountId));
    }
}
