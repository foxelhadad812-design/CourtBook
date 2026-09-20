using CourtBook.Application.Common;
using Microsoft.AspNetCore.Mvc;

namespace CourtBook.API.Extensions;

public static class ResultExtensions
{
    public static IActionResult ToActionResult<T>(this Result<T> result)
    {
        if (result.IsSuccess)
            return new OkObjectResult(result.Value);

        return ToErrorResult(result.Error);
    }

    public static IActionResult ToActionResult(this Result result)
    {
        if (result.IsSuccess)
            return new NoContentResult();

        return ToErrorResult(result.Error);
    }

    public static IActionResult ToCreatedResult<T>(this Result<T> result, string uri)
    {
        if (result.IsSuccess)
            return new CreatedResult(uri, result.Value);

        return ToErrorResult(result.Error);
    }

    private static IActionResult ToErrorResult(Error error)
    {
        var problem = new ProblemDetails
        {
            Status = error.StatusCode,
            Title = error.Code,
            Detail = error.Message
        };

        return new ObjectResult(problem)
        {
            StatusCode = error.StatusCode
        };
    }
}
