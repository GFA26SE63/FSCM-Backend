using FSCM.Application.Features.Complaints;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace FSCM.API.Errors;

public sealed class ComplaintExceptionHandler(IProblemDetailsService problemDetailsService, ILogger<ComplaintExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        if (exception is not ComplaintOperationException error) return false;
        var status = error.Code switch { "validation_error" => 400, "not_found" => 404, "forbidden" => 403, "conflict" => 409, _ => 500 };
        logger.LogWarning("Complaint request failed with code {Code}: {Message}", error.Code, error.Message);
        context.Response.StatusCode = status;
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext {
            HttpContext = context,
            ProblemDetails = new ProblemDetails { Status = status, Title = error.Message, Type = $"https://fscm.dev/problems/{error.Code}", Extensions = { ["code"] = error.Code } },
            Exception = error
        });
    }
}
