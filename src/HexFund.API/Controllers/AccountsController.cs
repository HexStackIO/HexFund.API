using HexFund.Application.DTOs;
using HexFund.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace HexFund.API.Controllers;

[Route("api/[controller]")]
public class AccountsController : ApiControllerBase
{
    private readonly IAccountService _accountService;

    public AccountsController(IAccountService accountService)
    {
        _accountService = accountService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<AccountDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAccounts() =>
        Ok(await _accountService.GetUserAccountsAsync(CurrentUserId));

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(AccountDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAccount(Guid id)
    {
        var account = await _accountService.GetAccountByIdAsync(id, CurrentUserId);
        return account == null ? NotFound() : Ok(account);
    }

    [HttpPost]
    [ProducesResponseType(typeof(AccountDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateAccount([FromBody] CreateAccountRequest request)
    {
        var account = await _accountService.CreateAccountAsync(request, CurrentUserId);
        return CreatedAtAction(nameof(GetAccount), new { id = account.AccountId }, account);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(typeof(AccountDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateAccount(Guid id, [FromBody] UpdateAccountRequest request)
    {
        var account = await _accountService.UpdateAccountAsync(id, request, CurrentUserId);
        return account == null ? NotFound() : Ok(account);
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteAccount(Guid id) =>
        await _accountService.DeleteAccountAsync(id, CurrentUserId) ? NoContent() : NotFound();
}
