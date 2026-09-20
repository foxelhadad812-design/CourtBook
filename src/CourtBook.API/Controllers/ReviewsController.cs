using CourtBook.API.Extensions;
using CourtBook.Application.Common;
using CourtBook.Application.DTOs;
using CourtBook.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CourtBook.API.Controllers;

[ApiController]
[Route("api/venues/{venueId}/reviews")]
public class ReviewsController : ControllerBase
{
    private readonly IReviewService _reviewService;

    public ReviewsController(IReviewService reviewService)
    {
        _reviewService = reviewService;
    }

    /// <summary>
    /// Gets paginated reviews for a facility.
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType(typeof(PagedResult<ReviewResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetVenueReviews(Guid venueId, [FromQuery] PagedRequest request)
    {
        var result = await _reviewService.GetVenueReviewsAsync(venueId, request);
        return Ok(result);
    }

    /// <summary>
    /// Submits a verified review for a completed booking.
    /// Only users with a completed booking at this venue can submit.
    /// </summary>
    [HttpPost]
    [Authorize]
    [ProducesResponseType(typeof(ReviewResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateReview(Guid venueId, [FromBody] CreateReviewRequest request)
    {
        var userId = User.GetUserId();
        var result = await _reviewService.CreateAsync(userId, venueId, request);
        return result.ToActionResult();
    }

    /// <summary>
    /// Owner response to a player's review.
    /// </summary>
    [HttpPost("{reviewId}/response")]
    [Authorize(Roles = "Owner")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RespondToReview(Guid venueId, Guid reviewId, [FromBody] OwnerResponseRequest request)
    {
        var ownerId = User.GetUserId();
        var result = await _reviewService.AddOwnerResponseAsync(ownerId, venueId, reviewId, request);
        return result.ToActionResult();
    }
}
