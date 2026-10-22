using FSCM.Application.Features.Maps;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace FSCM.API.Errors;

public sealed class MapExceptionHandler(IProblemDetailsService problemDetailsService) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not MapOperationException operationException) return false;
        var statusCode = operationException.Code switch
        {
            "validation_error" => StatusCodes.Status400BadRequest,
            "not_found" => StatusCodes.Status404NotFound,
            "conflict" => StatusCodes.Status409Conflict,
            "map_provider_error" => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status500InternalServerError
        };
        httpContext.Response.StatusCode = statusCode;
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = new ProblemDetails
            {
                Status = statusCode,
                Title = operationException.Message,
                Type = $"https://fscm.dev/problems/{operationException.Code}",
                Extensions = { ["code"] = operationException.Code }
            },
            Exception = operationException
        });
    }
}
