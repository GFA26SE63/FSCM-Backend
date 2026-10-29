using FSCM.Application.Features.Refunds;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace FSCM.API.Errors;

public sealed class RefundExceptionHandler(IProblemDetailsService problemDetailsService) : IExceptionHandler
{
    public ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        if (exception is not RefundOperationException error) return ValueTask.FromResult(false);
        var status = error.Code switch { "validation_error" => 400, "not_found" => 404, "forbidden" => 403, "conflict" => 409, _ => 500 };
        context.Response.StatusCode = status;
        return problemDetailsService.TryWriteAsync(new ProblemDetailsContext
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
