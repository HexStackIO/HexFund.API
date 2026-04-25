using HexFund.Application.DTOs;
using HexFund.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HexFund.API.Controllers;

[Authorize]
[Route("api/accounts/{accountId}")]
public class ProjectionsController : ApiControllerBase
{
    private readonly ICashFlowService _cashFlowService;
    private readonly ICacheService _cacheService;
    private readonly ILogger<ProjectionsController> _logger;

    public ProjectionsController(
        ICashFlowService cashFlowService,
        ICacheService cacheService,
        ILogger<ProjectionsController> logger)
    {
        _cashFlowService = cashFlowService;
        _cacheService = cacheService;
        _logger = logger;
    }

    [HttpGet("cashflow")]
    [ProducesResponseType(typeof(EnhancedCashFlowProjection), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCashFlowProjection(
        Guid accountId,
        [FromQuery] DateTimeOffset? startDate,
        [FromQuery] DateTimeOffset? endDate,
        [FromQuery] bool includeProjected = true)
    {
        var start = startDate ?? new DateTimeOffset(DateTime.Today.Year, DateTime.Today.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var end = endDate ?? start.AddMonths(1).AddDays(-1);

        if (start > end)
            return BadRequest(new { message = "Start date must be before end date" });

        if ((end - start).TotalDays > 365)
            return BadRequest(new { message = "Date range cannot exceed 1 year" });

        var userId = GetCurrentUserId();

        _logger.LogInformation(
            "Cash flow projection for account {AccountId}, {Start} to {End}", accountId, start, end);

        var projection = await _cashFlowService.GetEnhancedCashFlowProjectionAsync(
            accountId, userId, start, end, includeProjected);

        return projection == null
            ? NotFound(new { message = "Account not found or user does not have access" })
            : Ok(projection);
    }

    [HttpGet("monthly-overview")]
    [ProducesResponseType(typeof(EnhancedMonthlyOverview), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMonthlyOverview(
        Guid accountId, [FromQuery] int? year, [FromQuery] int? month)
    {
        var targetYear = year ?? DateTime.Today.Year;
        var targetMonth = month ?? DateTime.Today.Month;

        if (targetMonth < 1 || targetMonth > 12)
            return BadRequest(new { message = "Month must be between 1 and 12" });

        var userId = GetCurrentUserId();
        var cacheKey = _cacheService.MonthlyOverviewKey(accountId, targetYear, targetMonth);

        var overview = await _cacheService.GetOrCreateAsync(
            cacheKey,
            () => _cashFlowService.GetEnhancedMonthlyOverviewAsync(accountId, userId, targetYear, targetMonth),
            category: "MonthlyOverview");

        if (overview == null)
            return NotFound(new { message = "Account not found or user does not have access" });

        _cacheService.RegisterCachedMonth(accountId, targetYear, targetMonth);
        return Ok(overview);
    }

    [HttpGet("daily-balance")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDailyBalance(Guid accountId, [FromQuery] DateTimeOffset? date)
    {
        var targetDate = date ?? DateTimeOffset.UtcNow;
        var userId = GetCurrentUserId();

        var balance = await _cashFlowService.GetDailyBalanceAsync(accountId, userId, targetDate);

        return balance == null
            ? NotFound()
            : Ok(new { date = targetDate, balance = balance.Value });
    }

    [HttpGet("rolling-balance")]
    [ProducesResponseType(typeof(List<DailyBalanceSnapshot>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetRollingBalance(
        Guid accountId,
        [FromQuery] DateTimeOffset? startDate,
        [FromQuery] DateTimeOffset? endDate)
    {
        var start = startDate ?? DateTimeOffset.UtcNow.AddMonths(-1);
        var end = endDate ?? DateTimeOffset.UtcNow;

        if (start > end)
            return BadRequest(new { message = "Start date must be before end date" });

        if ((end - start).TotalDays > 365)
            return BadRequest(new { message = "Date range cannot exceed 1 year" });

        var userId = GetCurrentUserId();
        var snapshots = await _cashFlowService.GetRollingBalanceSnapshotsAsync(accountId, userId, start, end);

        return snapshots == null
            ? NotFound(new { message = "Account not found or user does not have access" })
            : Ok(snapshots);
    }

    [HttpGet("balance-at-date")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetBalanceAtDate(
        Guid accountId, [FromQuery] DateTimeOffset? asOfDate)
    {
        var targetDate = asOfDate ?? DateTimeOffset.UtcNow;
        var userId = GetCurrentUserId();

        var balance = await _cashFlowService.GetBalanceAtDateAsync(accountId, userId, targetDate);

        return balance == null
            ? NotFound(new { message = "Account not found or user does not have access" })
            : Ok(new
            {
                accountId,
                asOfDate = targetDate,
                balance = balance.Value,
                calculatedAt = DateTimeOffset.UtcNow
            });
    }

    [HttpGet("transactions/with-balances")]
    [ProducesResponseType(typeof(List<TransactionWithBalanceDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetTransactionsWithBalances(
        Guid accountId,
        [FromQuery] DateTimeOffset? startDate,
        [FromQuery] DateTimeOffset? endDate)
    {
        var userId = GetCurrentUserId();
        var transactions = await _cashFlowService.GetTransactionsWithRunningBalancesAsync(
            accountId, userId, startDate, endDate);

        return transactions == null
            ? NotFound(new { message = "Account not found or user does not have access" })
            : Ok(transactions);
    }
}
