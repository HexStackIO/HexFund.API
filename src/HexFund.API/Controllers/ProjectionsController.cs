using System.ComponentModel.DataAnnotations;
using HexFund.Application.DTOs;
using HexFund.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace HexFund.API.Controllers;

[Route("api/accounts/{accountId:guid}")]
public class ProjectionsController : ApiControllerBase
{
    private const int MaxRangeDays = 365;

    private readonly ICashFlowService _cashFlowService;

    public ProjectionsController(ICashFlowService cashFlowService)
    {
        _cashFlowService = cashFlowService;
    }

    /// <summary>Balance projection for a date range (defaults to the current UTC month; max one year).</summary>
    [HttpGet("cashflow")]
    [ProducesResponseType(typeof(EnhancedCashFlowProjection), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCashFlowProjection(
        Guid accountId, [FromQuery] DateTimeOffset? startDate, [FromQuery] DateTimeOffset? endDate)
    {
        var today = DateTimeOffset.UtcNow;
        var start = startDate ?? new DateTimeOffset(today.Year, today.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var end = endDate ?? start.AddMonths(1).AddTicks(-1);

        if (start > end)
            return ValidationProblem("Start date must be before end date.");

        if ((end - start).TotalDays > MaxRangeDays)
            return ValidationProblem("Date range cannot exceed 1 year.");

        var projection = await _cashFlowService.GetCashFlowProjectionAsync(accountId, CurrentUserId, start, end);
        return projection == null ? NotFound() : Ok(projection);
    }

    [HttpGet("monthly-overview")]
    [ProducesResponseType(typeof(EnhancedMonthlyOverview), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMonthlyOverview(
        Guid accountId,
        [FromQuery, Range(1900, 2200)] int? year,
        [FromQuery, Range(1, 12)] int? month)
    {
        var today = DateTimeOffset.UtcNow;
        var overview = await _cashFlowService.GetMonthlyOverviewAsync(
            accountId, CurrentUserId, year ?? today.Year, month ?? today.Month);

        return overview == null ? NotFound() : Ok(overview);
    }

    private IActionResult ValidationProblem(string message) =>
        ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>
        {
            ["dateRange"] = new[] { message }
        }));
}
