namespace ResolveAI.Api.Middleware;

/// <summary>
/// Catches unhandled exceptions, logs structured error details with TraceId,
/// and returns standardized RFC 7807 ProblemDetails without leaking sensitive information.
/// </summary>
public class GlobalExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionMiddleware> _logger;
    private readonly IHostEnvironment _env;

    public GlobalExceptionMiddleware(
        RequestDelegate next,
        ILogger<GlobalExceptionMiddleware> logger,
        IHostEnvironment env)
    {
        _next = next;
        _logger = logger;
        _env = env;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "An unhandled exception occurred during request execution. TraceId: {TraceId}",
                context.TraceIdentifier);

            if (context.Response.HasStarted)
            {
                _logger.LogWarning("The response has already started; unable to write ProblemDetails.");
                throw;
            }

            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/problem+json";

            var problemDetails = new
            {
                type = "https://tools.ietf.org/html/rfc9110#section-15.6.1",
                title = "An unexpected error occurred.",
                status = StatusCodes.Status500InternalServerError,
                detail = _env.IsDevelopment()
                    ? ex.Message
                    : "An unexpected internal server error occurred.",
                traceId = context.TraceIdentifier
            };

            await context.Response.WriteAsJsonAsync(problemDetails);
        }
    }
}
