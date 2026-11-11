using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Claims.API.Infrastructure;

// Catches any exception a controller doesn't handle. The client gets a short JSON message
// and a reference id; the details (stack trace, SQL error) only go to the server log.
public class GlobalExceptionHandler : IExceptionHandler
{
  private readonly ILogger<GlobalExceptionHandler> _logger;

  public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) => _logger = logger;

  public async ValueTask<bool> TryHandleAsync(
    HttpContext context, Exception exception, CancellationToken cancellationToken)
  {
    // The caller closed the connection; nobody is waiting for an answer.
    if (exception is OperationCanceledException && context.RequestAborted.IsCancellationRequested)
    {
      _logger.LogDebug("Request cancelled by the client: {Method} {Path}", context.Request.Method, context.Request.Path);
      return true;
    }

    var traceId = Activity.Current?.Id ?? context.TraceIdentifier;
    var (status, message, code) = Describe(exception);

    var userId = context.User.FindFirst("sub")?.Value ?? "anonymous";
    // Path only, never the query string: search terms can contain people's names.
    if (status >= 500)
      _logger.LogError(exception, "Request failed: {Method} {Path} user {UserId} ref {TraceId}",
        context.Request.Method, context.Request.Path, userId, traceId);
    else
      _logger.LogWarning(exception, "Request rejected: {Method} {Path} user {UserId} ref {TraceId}",
        context.Request.Method, context.Request.Path, userId, traceId);

    if (context.Response.HasStarted)
      return false;

    context.Response.Clear();
    context.Response.StatusCode = status;
    await context.Response.WriteAsJsonAsync(new { message, code, traceId }, cancellationToken);
    return true;
  }

  public static (int Status, string Message, string Code) Describe(Exception exception)
  {
    if (exception is BadHttpRequestException bad)
      return (bad.StatusCode, "The request could not be read.", "bad_request");

    if (exception is DbUpdateConcurrencyException)
      return (StatusCodes.Status409Conflict,
        "This record was changed by someone else. Reload it and try again.", "concurrency_conflict");

    if (IsDatabaseUnavailable(exception))
      return (StatusCodes.Status503ServiceUnavailable,
        "The database is not available right now. Try again in a few minutes.", "database_unavailable");

    return (StatusCodes.Status500InternalServerError,
      "Something went wrong. If it keeps happening, give this reference to the system administrator.",
      "server_error");
  }

  // Connection failures and timeouts, possibly wrapped by EF Core.
  private static bool IsDatabaseUnavailable(Exception exception)
  {
    for (var e = exception; e is not null; e = e.InnerException)
    {
      if (e is TimeoutException)
        return true;
      if (e is SqlException sql && (sql.Number is -2 or 53 or 2 or 4060 or 18456 or 1205 or 10054 or 10060 || sql.Class >= 20))
        return true;
    }
    return false;
  }
}
