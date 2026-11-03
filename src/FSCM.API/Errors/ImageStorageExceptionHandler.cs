using FSCM.Application.Features.Storage;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace FSCM.API.Errors;

public sealed class ImageStorageExceptionHandler(
    IProblemDetailsService problemDetailsService, ILogger<ImageStorageExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        if (exception is not ImageStorageException error) return false;
        var status = error.Code switch
        {
            "validation_error" => 400,
            "not_found" => 404,
            "forbidden" => 403,
            "service_unavailable" => 503,
            _ => 500
        };
        logger.LogWarning("Image storage request failed with code {Code}: {Message}", error.Code, error.Message);
        context.Response.StatusCode = status;
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Title = error.Message,
                Type = $"https://fscm.dev/problems/{error.Code}",
                Extensions = { ["code"] = error.Code }
            },
            Exception = error
        });
    }
}
