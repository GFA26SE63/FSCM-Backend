using FSCM.Application.Features.Catalog;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace FSCM.API.Errors;

public sealed class CatalogExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<CatalogExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not CatalogOperationException catalogException)
        {
            return false;
        }

        var statusCode = catalogException.Code switch
        {
            "validation_error" => StatusCodes.Status400BadRequest,
            "not_found" => StatusCodes.Status404NotFound,
            "conflict" or "invalid_transition" => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status500InternalServerError
        };

        logger.LogWarning(
            "Catalog request failed with code {ErrorCode}: {ErrorMessage}",
            catalogException.Code,
            catalogException.Message);

        httpContext.Response.StatusCode = statusCode;
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = new ProblemDetails
            {
                Status = statusCode,
                Title = catalogException.Message,
                Type = $"https://fscm.dev/problems/{catalogException.Code}",
                Extensions = { ["code"] = catalogException.Code }
            },
            Exception = catalogException
        });
    }
}
