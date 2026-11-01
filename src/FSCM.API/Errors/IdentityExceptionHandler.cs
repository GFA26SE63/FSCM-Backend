using FSCM.Application.Features.Identity;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace FSCM.API.Errors;

public sealed class IdentityExceptionHandler(IProblemDetailsService problemDetailsService) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not IdentityOperationException identityException)
        {
            return false;
        }

        var statusCode = identityException.Code switch
        {
            // F08: expired/invalid token vs locked account return distinct HTTP statuses so clients
            // can react differently (re-authenticate vs account locked).
            "invalid_credentials" or "invalid_token" => StatusCodes.Status401Unauthorized,
            "account_locked" => StatusCodes.Status423Locked,
            "rate_limited" => StatusCodes.Status429TooManyRequests,
            "retailer_activation_required" => StatusCodes.Status409Conflict,
            "email_delivery_failed" => StatusCodes.Status503ServiceUnavailable,
            "forbidden" => StatusCodes.Status403Forbidden,
            "not_found" => StatusCodes.Status404NotFound,
            "conflict" => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest
        };
        httpContext.Response.StatusCode = statusCode;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = new ProblemDetails
            {
                Status = statusCode,
                Title = identityException.Message,
                Type = $"https://fscm.dev/problems/{identityException.Code}",
                Extensions = { ["code"] = identityException.Code }
            },
            Exception = exception
        });
    }
}

