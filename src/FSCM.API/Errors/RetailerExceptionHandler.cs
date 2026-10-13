using FSCM.Application.Features.Retailers;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace FSCM.API.Errors;

public sealed class RetailerExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<RetailerExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not RetailerOperationException operationException) return false;
        var statusCode = operationException.Code switch
        {
            "validation_error" => StatusCodes.Status400BadRequest,
            "not_found" => StatusCodes.Status404NotFound,
            "conflict" or "invalid_transition" => StatusCodes.Status409Conflict,
            "forbidden" => StatusCodes.Status403Forbidden,
            _ => StatusCodes.Status500InternalServerError
        };
        logger.LogWarning("Retailer request failed with code {ErrorCode}: {ErrorMessage}", operationException.Code, operationException.Message);
        context.Response.StatusCode = statusCode;
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
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
