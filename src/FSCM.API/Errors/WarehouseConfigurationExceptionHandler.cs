using FSCM.Application.Features.WarehouseConfiguration;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace FSCM.API.Errors;

public sealed class WarehouseConfigurationExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<WarehouseConfigurationExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not WarehouseConfigurationOperationException operationException) return false;
        var statusCode = operationException.Code switch
        {
            "validation_error" => StatusCodes.Status400BadRequest,
            "not_found" => StatusCodes.Status404NotFound,
            "conflict" => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status500InternalServerError
        };
        logger.LogWarning("Warehouse/configuration request failed with code {ErrorCode}: {ErrorMessage}",
            operationException.Code, operationException.Message);
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
