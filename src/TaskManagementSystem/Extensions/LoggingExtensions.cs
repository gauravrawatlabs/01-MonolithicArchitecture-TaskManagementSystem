using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;

namespace TaskManagementSystem.Extensions;

/// <summary>Serilog setup: structured logging with correlation ids, and one summary line per request.</summary>
public static class LoggingExtensions
{
    // Development: one readable line per event, with the correlation id up front.
    private const string DevelopmentOutputTemplate =
        "[{Timestamp:HH:mm:ss} {Level:u3}] {CorrelationId} {SourceContext}{NewLine}      {Message:lj}{NewLine}{Exception}";

    /// <summary>
    /// Replaces the default Microsoft logger with Serilog.
    /// - Minimum levels / per-namespace overrides come from the "Serilog" section in appsettings.
    /// - Sinks are chosen in code by environment:
    ///     Development → human-readable console text
    ///     Other       → JSON (one object per line, all properties preserved) for log aggregators
    ///                   such as Seq, Elasticsearch, Azure Monitor, Datadog.
    /// - Enrich.FromLogContext() makes properties pushed via LogContext (CorrelationId) appear on every event.
    /// </summary>
    public static WebApplicationBuilder AddSerilogLogging(this WebApplicationBuilder builder)
    {
        var isDevelopment = builder.Environment.IsDevelopment();

        builder.Services.AddSerilog((services, logger) =>
        {
            logger
                .ReadFrom.Configuration(builder.Configuration)
                .ReadFrom.Services(services)
                .Enrich.FromLogContext()
                .Enrich.WithProperty("Application", builder.Environment.ApplicationName)
                .Enrich.WithProperty("Environment", builder.Environment.EnvironmentName);

            if (isDevelopment)
            {
                logger.WriteTo.Console(outputTemplate: DevelopmentOutputTemplate);
            }
            else
            {
                logger.WriteTo.Console(new RenderedCompactJsonFormatter());
            }
        });

        return builder;
    }

    /// <summary>
    /// One structured summary event per HTTP request (replaces ASP.NET Core's several noisy lines):
    ///   "HTTP POST /api/auth/login responded 200 in 35.2 ms"  + UserId, ClientIp, CorrelationId.
    ///
    /// Deliberately NOT logged: request/response bodies, headers (Authorization!), query strings.
    /// That keeps passwords, JWTs, refresh tokens and reset tokens out of the logs by construction.
    /// </summary>
    public static WebApplication UseSerilogRequestSummary(this WebApplication app)
    {
        app.UseSerilogRequestLogging(options =>
        {
            options.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0} ms";

            options.GetLevel = (httpContext, _, exception) =>
                exception is not null || httpContext.Response.StatusCode >= StatusCodes.Status500InternalServerError
                    ? LogEventLevel.Error
                    : httpContext.Request.Path.StartsWithSegments("/health")
                        ? LogEventLevel.Verbose      // load balancers poll this constantly → below the minimum level
                        : LogEventLevel.Information;

            options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
            {
                diagnosticContext.Set("ClientIp", httpContext.GetClientIpAddress());
                // Runs after the pipeline, so authentication has already populated HttpContext.User.
                if (httpContext.User.TryGetUserId(out var userId))
                {
                    diagnosticContext.Set("UserId", userId);
                }
            };
        });

        return app;
    }

    /// <summary>Adds the correlation id to EVERY ProblemDetails response (exceptions, 401/403/404, validation).</summary>
    public static IServiceCollection AddProblemDetailsWithCorrelationId(this IServiceCollection services) =>
        services.AddProblemDetails(options =>
            options.CustomizeProblemDetails = context =>
            {
                if (context.HttpContext.GetCorrelationId() is { } correlationId)
                {
                    context.ProblemDetails.Extensions["correlationId"] = correlationId;
                }
            });
}
