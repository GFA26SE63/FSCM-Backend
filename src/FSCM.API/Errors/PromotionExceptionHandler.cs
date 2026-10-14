using FSCM.Application.Features.Promotions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace FSCM.API.Errors;

public sealed class PromotionExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<PromotionExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not PromotionOperationException promotionException) return false;
        var status = promotionException.Code switch
        {
            "validation_error" => StatusCodes.Status400BadRequest,
            "not_found" => StatusCodes.Status404NotFound,
            "conflict" => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status500InternalServerError
        };
        logger.LogWarning("Promotion request failed with code {Code}: {Message}",
            promotionException.Code, promotionException.Message);
        context.Response.StatusCode = status;
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Title = promotionException.Message,
                Type = $"https://fscm.dev/problems/{promotionException.Code}",
                Extensions = { ["code"] = promotionException.Code }
            },
            Exception = promotionException
        });
    }
}
