using FSCM.Application.Features.Quotes;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace FSCM.API.Errors;

public sealed class QuoteExceptionHandler(IProblemDetailsService problemDetailsService) : IExceptionHandler
{
    public ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not QuoteOperationException error) return ValueTask.FromResult(false);
        httpContext.Response.StatusCode = error.Code switch { "not_found" => 404, "forbidden" => 403, "conflict" => 409, _ => 400 };
        return problemDetailsService.TryWriteAsync(new ProblemDetailsContext { HttpContext = httpContext, ProblemDetails = new ProblemDetails { Status = httpContext.Response.StatusCode, Title = error.Message, Type = $"https://fscm.dev/problems/{error.Code}", Extensions = { ["code"] = error.Code } }, Exception = error });
    }
}
