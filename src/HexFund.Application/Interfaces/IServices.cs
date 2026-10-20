using HexFund.Application.DTOs;

namespace HexFund.Application.Interfaces;

public interface IAccountService
{
    Task<IReadOnlyList<AccountDto>> GetUserAccountsAsync(Guid userId);
    Task<AccountDto?> GetAccountByIdAsync(Guid accountId, Guid userId);
    Task<AccountDto> CreateAccountAsync(CreateAccountRequest request, Guid userId);
    Task<AccountDto?> UpdateAccountAsync(Guid accountId, UpdateAccountRequest request, Guid userId);
    Task<bool> DeleteAccountAsync(Guid accountId, Guid userId);
}

public interface IAuthService
{
    Task<UserDto?> GetCurrentUserAsync(Guid userId);

    /// <summary>Creates the local user on first sign-in, otherwise refreshes login time and name.</summary>
    Task<UserDto> SyncEntraUserAsync(string objectId, string? email, string? firstName, string? lastName);

    /// <summary>Updates first/last name. Returns null if the user was not found.</summary>
    Task<UserDto?> UpdateProfileAsync(Guid userId, UpdateProfileRequest request);

    /// <summary>
    /// Permanently deletes the Entra identity and all local data (accounts, transactions,
    /// categories) via DB cascade. Returns false if the user was not found.
    /// </summary>
    Task<bool> DeleteUserAsync(Guid userId);
}

public interface ICategoryService
{
    /// <summary>Returns the user's categories ordered by name.</summary>
    Task<IReadOnlyList<UserCategoryDto>> GetUserCategoriesAsync(Guid userId);

    /// <summary>
    /// Creates a category. Returns null if one with the same name (case-insensitive)
    /// already exists for the user.
    /// </summary>
    Task<UserCategoryDto?> CreateCategoryAsync(CreateCategoryRequest request, Guid userId);

    /// <summary>
    /// Deletes a category. Transactions keep their category string so history is preserved.
    /// Returns false if not found or not owned by the user.
    /// </summary>
    Task<bool> DeleteCategoryAsync(Guid categoryId, Guid userId);
}

/// <remarks>
/// "Not found / not yours" is reported as null or false. Invalid operations on an existing
/// transaction (bad amend date, amending a one-time transaction, ...) throw
/// <see cref="ArgumentException"/>, which the API maps to a 400.
/// </remarks>
public interface ITransactionService
{
    /// <summary>Returns null if the account is not found / not owned. Superseded rows are hidden unless <paramref name="includeHistory"/>.</summary>
    Task<IReadOnlyList<TransactionDto>?> GetAccountTransactionsAsync(Guid accountId, Guid userId, bool includeHistory = false);
    Task<TransactionDto?> GetTransactionByIdAsync(Guid transactionId, Guid userId);
    Task<TransactionDto?> CreateTransactionAsync(Guid accountId, CreateTransactionRequest request, Guid userId);
    Task<TransactionDto?> UpdateTransactionAsync(Guid accountId, Guid transactionId, UpdateTransactionRequest request, Guid userId);
    Task<bool> DeleteTransactionAsync(Guid accountId, Guid transactionId, Guid userId);
    Task<TransactionDto?> AmendTransactionAsync(Guid accountId, Guid transactionId, AmendTransactionRequest request, Guid userId);
}

public interface ICashFlowService
{
    Task<EnhancedCashFlowProjection?> GetCashFlowProjectionAsync(
        Guid accountId, Guid userId, DateTimeOffset startDate, DateTimeOffset endDate);

    Task<EnhancedMonthlyOverview?> GetMonthlyOverviewAsync(
        Guid accountId, Guid userId, int year, int month);

    Task<List<TransactionOccurrence>?> GetTransactionsForDateAsync(
        Guid accountId, Guid userId, DateOnly date);

    /// <summary>Occurrences for a month keyed by "yyyy-MM-dd".</summary>
    Task<Dictionary<string, List<TransactionOccurrence>>?> GetTransactionsForMonthAsync(
        Guid accountId, Guid userId, int year, int month);
}
