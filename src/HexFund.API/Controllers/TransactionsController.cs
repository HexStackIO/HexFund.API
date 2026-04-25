using HexFund.Application.DTOs;
using HexFund.Application.Interfaces;
using HexFund.API.Middleware;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HexFund.API.Controllers;

[Authorize]
[Route("api")]
public class TransactionsController : ApiControllerBase
{
    private readonly ITransactionService _transactionService;
    private readonly ICashFlowService _cashFlowService;
    private readonly ICacheService _cacheService;
    private readonly ILogger<TransactionsController> _logger;

    public TransactionsController(
        ITransactionService transactionService,
        ICashFlowService cashFlowService,
        ICacheService cacheService,
        ILogger<TransactionsController> logger)
    {
        _transactionService = transactionService;
        _cashFlowService = cashFlowService;
        _cacheService = cacheService;
        _logger = logger;
    }

    /// <summary>
    /// Get transactions for a specific account.
    /// Pass ?includeHistory=true to include superseded (end-dated predecessor) rows.
    /// </summary>
    [HttpGet("accounts/{accountId}/transactions")]
    [ProducesResponseType(typeof(IEnumerable<TransactionDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAccountTransactions(
        Guid accountId, [FromQuery] bool includeHistory = false)
    {
        var userId = GetCurrentUserId();
        var transactions = await _transactionService.GetAccountTransactionsAsync(
            accountId, userId, includeHistory);
        return Ok(transactions);
    }

    [HttpGet("transactions/{id}")]
    [ProducesResponseType(typeof(TransactionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTransaction(Guid id)
    {
        var userId = GetCurrentUserId();
        var transaction = await _transactionService.GetTransactionByIdAsync(id, userId);
        return transaction == null ? NotFound() : Ok(transaction);
    }

    [HttpPost("accounts/{accountId}/transactions")]
    [ProducesResponseType(typeof(TransactionDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreateTransaction(
        Guid accountId, [FromBody] CreateTransactionRequest request)
    {
        // Validation and sanitization handled by ValidationActionFilter.
        var userId = GetCurrentUserId();
        var transaction = await _transactionService.CreateTransactionAsync(accountId, request, userId);

        if (transaction == null)
            return NotFound(new { message = "Account not found" });

        InvalidateTransactionCaches(accountId);

        _logger.LogInformation("Transaction created: {TransactionId} for account {AccountId}",
            transaction.TransactionId, accountId);

        return CreatedAtAction(nameof(GetTransaction),
            new { id = transaction.TransactionId }, transaction);
    }

    [HttpPut("accounts/{accountId}/transactions/{id}")]
    [ProducesResponseType(typeof(TransactionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateTransaction(
        Guid accountId, Guid id, [FromBody] UpdateTransactionRequest request)
    {
        var userId = GetCurrentUserId();
        var transaction = await _transactionService.UpdateTransactionAsync(id, request, userId);

        if (transaction == null)
            return NotFound();

        InvalidateTransactionCaches(accountId);

        _logger.LogInformation("Transaction updated: {TransactionId} for account {AccountId}",
            id, accountId);
        return Ok(transaction);
    }

    [HttpDelete("accounts/{accountId}/transactions/{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteTransaction(Guid accountId, Guid id)
    {
        var userId = GetCurrentUserId();
        var success = await _transactionService.DeleteTransactionAsync(id, userId);

        if (!success)
            return NotFound();

        InvalidateTransactionCaches(accountId);

        _logger.LogInformation("Transaction deleted: {TransactionId} for account {AccountId}",
            id, accountId);
        return NoContent();
    }

    [HttpGet("accounts/{accountId}/transactions-for-date")]
    [ProducesResponseType(typeof(IEnumerable<TransactionOccurrence>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTransactionsForDate(
        Guid accountId, [FromQuery] DateTime date)
    {
        var userId = GetCurrentUserId();
        var cacheKey = _cacheService.TransactionsForDateKey(accountId, date);

        var transactions = await _cacheService.GetOrCreateAsync(
            cacheKey,
            () => _cashFlowService.GetTransactionsForDateAsync(accountId, userId, new DateTimeOffset(date)),
            category: "Transactions",
            customDuration: TimeSpan.FromMinutes(1));

        return transactions == null ? NotFound() : Ok(transactions);
    }

    [HttpGet("accounts/{accountId}/transactions-for-month")]
    [ProducesResponseType(typeof(Dictionary<string, List<TransactionOccurrence>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTransactionsForMonth(
        Guid accountId, [FromQuery] int year, [FromQuery] int month)
    {
        if (month < 1 || month > 12)
            return BadRequest(new ValidationErrorResponse
            {
                Message = "One or more validation errors occurred.",
                Errors = new Dictionary<string, string[]>
                {
                    { "month", new[] { "Month must be between 1 and 12." } }
                }
            });

        if (year < 1900 || year > DateTime.UtcNow.Year + 50)
            return BadRequest(new ValidationErrorResponse
            {
                Message = "One or more validation errors occurred.",
                Errors = new Dictionary<string, string[]>
                {
                    { "year", new[] { "Year is outside the allowed range." } }
                }
            });

        var userId = GetCurrentUserId();
        var cacheKey = _cacheService.MonthTransactionsKey(accountId, year, month);

        var transactionsByDate = await _cacheService.GetOrCreateAsync(
            cacheKey,
            () => _cashFlowService.GetTransactionsForMonthAsync(accountId, userId, year, month),
            category: "Transactions",
            customDuration: TimeSpan.FromMinutes(2));

        if (transactionsByDate == null)
            return NotFound(new { message = "Account not found or user does not have access." });

        var result = transactionsByDate.ToDictionary(
            kvp => kvp.Key.ToString("yyyy-MM-dd"),
            kvp => kvp.Value);

        return Ok(result);
    }

    /// <summary>
    /// Amend a recurring transaction — apply new values from a specific effective date forward.
    /// </summary>
    [HttpPost("accounts/{accountId}/transactions/{id}/amend")]
    [ProducesResponseType(typeof(TransactionDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AmendTransaction(
        Guid accountId, Guid id, [FromBody] AmendTransactionRequest request)
    {
        var userId = GetCurrentUserId();
        var successor = await _transactionService.AmendTransactionAsync(id, request, userId);

        if (successor == null)
            return NotFound(new
            {
                message = "Transaction not found, not owned by you, or effective date is not after the transaction start date."
            });

        InvalidateTransactionCaches(accountId);

        _logger.LogInformation(
            "Transaction {OriginalId} amended — successor {SuccessorId} effective {EffectiveDate}",
            id, successor.TransactionId, request.EffectiveDate);

        return CreatedAtAction(nameof(GetTransaction),
            new { id = successor.TransactionId }, successor);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void InvalidateTransactionCaches(Guid accountId)
    {
        _cacheService.Invalidate(_cacheService.TransactionsListKey(accountId));
        _cacheService.InvalidateCalendarData(accountId);
    }
}