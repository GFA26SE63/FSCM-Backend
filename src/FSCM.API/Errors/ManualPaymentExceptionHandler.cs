using FSCM.Application.Features.ManualPayments;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace FSCM.API.Errors;

public sealed class ManualPaymentExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<ManualPaymentExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not ManualPaymentOperationException operationException) return false;
        var statusCode = operationException.Code switch
        {
            "validation_error" => StatusCodes.Status400BadRequest,
            "not_found" => StatusCodes.Status404NotFound,
            "forbidden" => StatusCodes.Status403Forbidden,
            "conflict" => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status500InternalServerError
        };
        logger.LogWarning("Manual payment request failed with code {ErrorCode}: {ErrorMessage}",
            operationException.Code, operationException.Message);
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
