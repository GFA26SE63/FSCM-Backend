using FSCM.Application.Features.Territory;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace FSCM.API.Errors;

public sealed class TerritoryExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<TerritoryExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not TerritoryOperationException territoryException) return false;
        var statusCode = territoryException.Code switch
        {
            "validation_error" => StatusCodes.Status400BadRequest,
            "not_found" => StatusCodes.Status404NotFound,
            "conflict" => StatusCodes.Status409Conflict,
            "forbidden" => StatusCodes.Status403Forbidden,
            _ => StatusCodes.Status500InternalServerError
        };
        logger.LogWarning("Territory request failed with code {ErrorCode}: {ErrorMessage}",
            territoryException.Code, territoryException.Message);
        httpContext.Response.StatusCode = statusCode;
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = new ProblemDetails
            {
                Status = statusCode,
                Title = territoryException.Message,
                Type = $"https://fscm.dev/problems/{territoryException.Code}",
                Extensions = { ["code"] = territoryException.Code }
            },
            Exception = territoryException
        });
    }
}
