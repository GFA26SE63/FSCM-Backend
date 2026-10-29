using FSCM.Application.Features.Loyalty;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace FSCM.API.Errors;

public sealed class LoyaltyExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<LoyaltyExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not LoyaltyOperationException loyaltyException) return false;
        var status = loyaltyException.Code switch
        {
            "validation_error" => StatusCodes.Status400BadRequest,
            "not_found" => StatusCodes.Status404NotFound,
            "conflict" => StatusCodes.Status409Conflict,
            "forbidden" => StatusCodes.Status403Forbidden,
            _ => StatusCodes.Status500InternalServerError
        };
        logger.LogWarning("Loyalty request failed with code {Code}: {Message}",
            loyaltyException.Code, loyaltyException.Message);
        context.Response.StatusCode = status;
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Title = loyaltyException.Message,
                Type = $"https://fscm.dev/problems/{loyaltyException.Code}",
                Extensions = { ["code"] = loyaltyException.Code }
            },
            Exception = loyaltyException
        });
    }
}
