using FSCM.SharedKernel.Domain;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace FSCM.Api.Infrastructure;

/// <summary>
/// Maps exceptions to ProblemDetails. The Vietnamese message of a BusinessRuleException is shown as-is by the Portal / mobile app;
/// the <c>code</c> extension (e.g. "stock.insufficient") is what the clients switch on.
/// </summary>
public sealed class FscmExceptionHandler(IProblemDetailsService problems, ILogger<FscmExceptionHandler> log) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception ex, CancellationToken ct)
    {
        var (status, title, code) = ex switch
        {
            BusinessRuleException { Code: "concurrency" } b => (StatusCodes.Status409Conflict, b.Message, b.Code),
            BusinessRuleException b when b.Code.EndsWith("not_found", StringComparison.Ordinal) => (StatusCodes.Status404NotFound, b.Message, b.Code),
            BusinessRuleException b => (StatusCodes.Status422UnprocessableEntity, b.Message, b.Code),
            UnauthorizedAccessException u => (StatusCodes.Status403Forbidden, string.IsNullOrEmpty(u.Message) ? "Không có quyền thực hiện thao tác này." : u.Message, "forbidden"),
            ArgumentException a => (StatusCodes.Status400BadRequest, a.Message, "bad_request"),
            _ => (StatusCodes.Status500InternalServerError, "Lỗi hệ thống. Vui lòng thử lại sau.", "server_error"),
        };
        if (status == StatusCodes.Status500InternalServerError) log.LogError(ex, "Unhandled exception");

        http.Response.StatusCode = status;
        var pd = new ProblemDetails { Status = status, Title = title, Instance = http.Request.Path };
        pd.Extensions["code"] = code;
        pd.Extensions["traceId"] = http.TraceIdentifier;
        return await problems.TryWriteAsync(new ProblemDetailsContext { HttpContext = http, ProblemDetails = pd, Exception = ex });
    }
}
