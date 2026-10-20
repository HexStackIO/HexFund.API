using HexFund.API.Extensions;
using HexFund.Application.DTOs;
using HexFund.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace HexFund.API.Controllers;

[Route("api/[controller]")]
public class FeedbackController : ApiControllerBase
{
    private readonly IFeedbackService _feedbackService;

    public FeedbackController(IFeedbackService feedbackService)
    {
        _feedbackService = feedbackService;
    }

    /// <summary>
    /// Submits a bug report, idea or question. The report is stored immediately and turned into a
    /// private issue in the background, so a 202 means "received", not "issue created".
    /// Limited to 5 reports per hour per user and 64 KB per request.
    /// </summary>
    [HttpPost]
    [RequestSizeLimit(MaxRequestBytes)]
    [EnableRateLimiting(ApiPolicies.FeedbackRateLimit)]
    [ProducesResponseType(typeof(FeedbackReceiptDto), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Submit([FromBody] SubmitFeedbackRequest request)
    {
        var receipt = await _feedbackService.SubmitAsync(CurrentUserId, request);

        if (receipt == null)
            return Problem(
                detail: "Your account has not been set up yet. Call POST /api/auth/sync first.",
                statusCode: StatusCodes.Status404NotFound,
                title: "Not Found");

        return Accepted(receipt);
    }

    private const int MaxRequestBytes = 64 * 1024;
}
