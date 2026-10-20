using System.ComponentModel.DataAnnotations;
using HexFund.Application.DTOs;
using HexFund.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace HexFund.API.Controllers;

[Route("api")]
public class TransactionsController : ApiControllerBase
{
    private readonly ITransactionService _transactionService;
    private readonly ICashFlowService _cashFlowService;

    public TransactionsController(ITransactionService transactionService, ICashFlowService cashFlowService)
    {
        _transactionService = transactionService;
        _cashFlowService = cashFlowService;
    }

    /// <summary>
    /// Transactions for an account. Pass ?includeHistory=true to include superseded
    /// (amended-away) predecessor rows.
    /// </summary>
    [HttpGet("accounts/{accountId:guid}/transactions")]
    [ProducesResponseType(typeof(IEnumerable<TransactionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAccountTransactions(Guid accountId, [FromQuery] bool includeHistory = false)
    {
        var transactions = await _transactionService.GetAccountTransactionsAsync(accountId, CurrentUserId, includeHistory);
        return transactions == null ? NotFound() : Ok(transactions);
    }

    [HttpGet("transactions/{id:guid}")]
    [ProducesResponseType(typeof(TransactionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTransaction(Guid id)
    {
        var transaction = await _transactionService.GetTransactionByIdAsync(id, CurrentUserId);
        return transaction == null ? NotFound() : Ok(transaction);
    }

    [HttpPost("accounts/{accountId:guid}/transactions")]
    [ProducesResponseType(typeof(TransactionDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreateTransaction(Guid accountId, [FromBody] CreateTransactionRequest request)
    {
        var transaction = await _transactionService.CreateTransactionAsync(accountId, request, CurrentUserId);

        return transaction == null
            ? NotFound()
            : CreatedAtAction(nameof(GetTransaction), new { id = transaction.TransactionId }, transaction);
    }

    [HttpPut("accounts/{accountId:guid}/transactions/{id:guid}")]
    [ProducesResponseType(typeof(TransactionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateTransaction(Guid accountId, Guid id, [FromBody] UpdateTransactionRequest request)
    {
        var transaction = await _transactionService.UpdateTransactionAsync(accountId, id, request, CurrentUserId);
        return transaction == null ? NotFound() : Ok(transaction);
    }

    [HttpDelete("accounts/{accountId:guid}/transactions/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteTransaction(Guid accountId, Guid id) =>
        await _transactionService.DeleteTransactionAsync(accountId, id, CurrentUserId) ? NoContent() : NotFound();

    /// <summary>Amend a recurring transaction: apply new values from a specific effective date forward.</summary>
    [HttpPost("accounts/{accountId:guid}/transactions/{id:guid}/amend")]
    [ProducesResponseType(typeof(TransactionDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AmendTransaction(Guid accountId, Guid id, [FromBody] AmendTransactionRequest request)
    {
        var successor = await _transactionService.AmendTransactionAsync(accountId, id, request, CurrentUserId);

        return successor == null
            ? NotFound()
            : CreatedAtAction(nameof(GetTransaction), new { id = successor.TransactionId }, successor);
    }

    [HttpGet("accounts/{accountId:guid}/transactions-for-date")]
    [ProducesResponseType(typeof(IEnumerable<TransactionOccurrence>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTransactionsForDate(Guid accountId, [FromQuery, Required] DateOnly? date)
    {
        var occurrences = await _cashFlowService.GetTransactionsForDateAsync(accountId, CurrentUserId, date!.Value);
        return occurrences == null ? NotFound() : Ok(occurrences);
    }

    /// <summary>Occurrences for a month, keyed by "yyyy-MM-dd".</summary>
    [HttpGet("accounts/{accountId:guid}/transactions-for-month")]
    [ProducesResponseType(typeof(Dictionary<string, List<TransactionOccurrence>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTransactionsForMonth(
        Guid accountId,
        [FromQuery, Range(1900, 2200)] int year,
        [FromQuery, Range(1, 12)] int month)
    {
        var occurrences = await _cashFlowService.GetTransactionsForMonthAsync(accountId, CurrentUserId, year, month);
        return occurrences == null ? NotFound() : Ok(occurrences);
    }
}
