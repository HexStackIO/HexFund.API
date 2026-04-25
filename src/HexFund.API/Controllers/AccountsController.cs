using HexFund.API.Middleware;
using HexFund.Application.DTOs;
using HexFund.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HexFund.API.Controllers;

[Authorize]
[Route("api/[controller]")]
public class AccountsController : ApiControllerBase
{
    private readonly IAccountService _accountService;
    private readonly ICacheService _cacheService;
    private readonly ILogger<AccountsController> _logger;

    public AccountsController(
        IAccountService accountService,
        ICacheService cacheService,
        ILogger<AccountsController> logger)
    {
        _accountService = accountService;
        _cacheService = cacheService;
        _logger = logger;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<AccountDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAccounts()
    {
        var userId = GetCurrentUserId();
        var accounts = await _accountService.GetUserAccountsAsync(userId);
        return Ok(accounts);
    }

    [HttpGet("{id}")]
    [ProducesResponseType(typeof(AccountDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAccount(Guid id)
    {
        var userId = GetCurrentUserId();
        var account = await _cacheService.GetOrCreateAsync(
            _cacheService.AccountKey(id),
            () => _accountService.GetAccountByIdAsync(id, userId),
            category: "Accounts");

        return account == null ? NotFound() : Ok(account);
    }

    [HttpPost]
    [ProducesResponseType(typeof(AccountDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateAccount([FromBody] CreateAccountRequest request)
    {
        // Validation and sanitization are handled by ValidationActionFilter —
        // if we reach this point the request is guaranteed valid and clean.
        var userId = GetCurrentUserId();
        var account = await _accountService.CreateAccountAsync(request, userId);

        _cacheService.Invalidate(_cacheService.AccountsListKey(userId));

        _logger.LogInformation("Account created: {AccountId} for user {UserId}",
            account.AccountId, userId);

        return CreatedAtAction(nameof(GetAccount), new { id = account.AccountId }, account);
    }

    [HttpPut("{id}")]
    [ProducesResponseType(typeof(AccountDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateAccount(Guid id, [FromBody] UpdateAccountRequest request)
    {
        var userId = GetCurrentUserId();
        var account = await _accountService.UpdateAccountAsync(id, request, userId);

        if (account == null)
            return NotFound();

        _cacheService.Invalidate(_cacheService.AccountsListKey(userId));
        _cacheService.Invalidate(_cacheService.AccountKey(id));

        _logger.LogInformation("Account updated: {AccountId}", id);
        return Ok(account);
    }

    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteAccount(Guid id)
    {
        var userId = GetCurrentUserId();
        var success = await _accountService.DeleteAccountAsync(id, userId);

        if (!success)
            return NotFound();

        _cacheService.Invalidate(_cacheService.AccountsListKey(userId));
        _cacheService.Invalidate(_cacheService.AccountKey(id));
        _cacheService.Invalidate(_cacheService.TransactionsListKey(id));

        _logger.LogInformation("Account deleted: {AccountId}", id);
        return NoContent();
    }
}