using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using TaskManagementSystem.Authorization;
using TaskManagementSystem.Authorization.Handlers;
using TaskManagementSystem.Configuration;
using TaskManagementSystem.Constants;
using TaskManagementSystem.Data;
using TaskManagementSystem.Data.Seed;
using TaskManagementSystem.Interfaces.Repositories;
using TaskManagementSystem.Interfaces.Services;
using TaskManagementSystem.Models.Entities;
using TaskManagementSystem.Repositories;
using TaskManagementSystem.Services;

namespace TaskManagementSystem.Extensions;

/// <summary>
/// Groups service registrations by concern so Program.cs stays a readable composition root.
/// </summary>
public static class ServiceCollectionExtensions
{
    private const string ConnectionStringName = "DefaultConnection";
    private const string BearerSchemeId = "Bearer";

    public static IServiceCollection AddDatabase(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            // Fail fast at startup instead of on the first request.
            throw new InvalidOperationException(
                $"Connection string '{ConnectionStringName}' is not configured. " +
                "Set it in appsettings.Development.json (local) or the " +
                $"'ConnectionStrings__{ConnectionStringName}' environment variable.");
        }

        // AddDbContext registers ApplicationDbContext with a SCOPED lifetime (one per HTTP request).
        services.AddDbContext<ApplicationDbContext>(options =>
        {
            options.UseSqlServer(connectionString);

            // EF Core logs every failed command / SaveChanges / query / connection at ERROR level — even
            // failures our code handles (e.g. a duplicate name → 409). Every such exception either gets
            // handled (and our services log it appropriately) or reaches ExceptionHandlingMiddleware,
            // which logs it ONCE as an Error with the full stack trace and the correlation id.
            // EF's own copies would log the same failure 2–3 times: downgrade them to Debug.
            options.ConfigureWarnings(warnings => warnings.Log(
                (CoreEventId.SaveChangesFailed, LogLevel.Debug),
                (CoreEventId.QueryIterationFailed, LogLevel.Debug),
                (RelationalEventId.CommandError, LogLevel.Debug),
                (RelationalEventId.ConnectionError, LogLevel.Debug)));

            if (environment.IsDevelopment())
            {
                // Richer exception messages while developing. We deliberately do NOT call
                // EnableSensitiveDataLogging(): it would write parameter values
                // (password hashes, token hashes, emails) into the logs.
                options.EnableDetailedErrors();
            }
        });

        return services;
    }

    /// <summary>
    /// ASP.NET Core Identity (user/role management, password hashing, lockout) +
    /// JWT Bearer authentication (validates the token on every request).
    /// </summary>
    public static IServiceCollection AddIdentityAndJwtAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Options pattern + validation at startup: a missing/short signing key stops the app immediately.
        services.AddOptions<JwtSettings>()
            .Bind(configuration.GetSection(JwtSettings.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<AdminSeedSettings>()
            .Bind(configuration.GetSection(AdminSeedSettings.SectionName));

        services.AddOptions<EmailSettings>()
            .Bind(configuration.GetSection(EmailSettings.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Email-confirmation and password-reset tokens expire after 3 hours (Identity default: 1 day).
        services.Configure<DataProtectionTokenProviderOptions>(options =>
            options.TokenLifespan = TimeSpan.FromHours(3));

        // AddIdentityCore (not AddIdentity): registers user/role management WITHOUT cookie
        // authentication. AddIdentity would make cookies the default scheme — wrong for a JWT API.
        services.AddIdentityCore<ApplicationUser>(options =>
            {
                options.User.RequireUniqueEmail = true;

                options.Password.RequiredLength = PasswordRules.MinLength;
                options.Password.RequiredUniqueChars = PasswordRules.RequiredUniqueChars;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = true;

                // Brute-force protection: 5 wrong passwords → locked for 15 minutes.
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.Lockout.AllowedForNewUsers = true;

                // Unverified users may log in; Phase 7's "EmailVerified" policy limits what they can do.
                options.SignIn.RequireConfirmedEmail = false;
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<ApplicationDbContext>()  // users/roles stored via our DbContext
            .AddSignInManager()                                // CheckPasswordSignInAsync (lockout-aware)
            .AddDefaultTokenProviders();                       // email-confirmation / reset tokens (Phase 6)

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();

        // Configure JwtBearer from the VALIDATED JwtSettings (resolved lazily from DI).
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtSettings>>((options, jwtOptions) =>
            {
                var jwt = jwtOptions.Value;

                // Keep claim names exactly as in the token ("sub", "role") — no legacy
                // mapping to long XML-style URIs like http://schemas.xmlsoap.org/.../nameidentifier.
                options.MapInboundClaims = false;

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = jwt.CreateSigningKey(),
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256], // reject "alg: none" and algorithm swaps
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),              // default is 5 minutes — too generous
                    NameClaimType = AppClaimTypes.Email,               // User.Identity.Name → email
                    RoleClaimType = AppClaimTypes.Role                 // User.IsInRole("Admin") reads "role"
                };
            });

        return services;
    }

    /// <summary>
    /// Authorization policies.
    ///
    /// FALLBACK policy = "secure by default": every endpoint WITHOUT an [Authorize]/[AllowAnonymous]
    /// attribute requires an authenticated user. Forgetting an attribute on a new endpoint therefore
    /// fails closed (401) instead of silently exposing data.
    /// </summary>
    public static IServiceCollection AddAuthorizationPolicies(this IServiceCollection services)
    {
        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build())

            // ROLE-based: reads the "role" claims (RoleClaimType configured in JwtBearer).
            .AddPolicy(Policies.AdminOnly, policy => policy
                .RequireAuthenticatedUser()
                .RequireRole(Roles.Admin))

            // CLAIMS-based: the decision is based on a claim's VALUE, not on a role.
            .AddPolicy(Policies.EmailVerified, policy => policy
                .RequireAuthenticatedUser()
                .RequireVerifiedEmail())

            // ONE policy with SEVERAL requirements (all must pass). Compare with stacking two
            // [Authorize] attributes: same AND logic, but the rule lives in one named place,
            // so the controller just says "CanManageCategories" and the definition can evolve.
            .AddPolicy(Policies.CanManageCategories, policy => policy
                .RequireAuthenticatedUser()
                .RequireRole(Roles.Admin)
                .RequireVerifiedEmail())

            // RequireRole with several roles = OR (Admin OR Manager); chained requirements = AND.
            .AddPolicy(Policies.CanCreateProjects, policy => policy
                .RequireAuthenticatedUser()
                .RequireRole(Roles.Admin, Roles.Manager)
                .RequireVerifiedEmail());

        // RESOURCE-based handlers (invoked via IAuthorizationService.AuthorizeAsync(user, resource, operation)).
        // Singleton: pure logic without dependencies.
        services.AddSingleton<IAuthorizationHandler, UserAuthorizationHandler>();
        services.AddSingleton<IAuthorizationHandler, ProjectAuthorizationHandler>();
        services.AddSingleton<IAuthorizationHandler, TaskAuthorizationHandler>();
        services.AddSingleton<IAuthorizationHandler, CommentAuthorizationHandler>();

        return services;
    }

    /// <summary>
    /// Registers repositories, services, validators and the seeder (interface → implementation).
    ///
    /// Lifetimes:
    /// - Repositories: SCOPED — they depend on the scoped DbContext. A singleton repository would
    ///   capture one DbContext forever (captive dependency) and share it across requests.
    /// - Services: SCOPED — they depend on scoped repositories / UserManager; one instance per request.
    /// - TokenService, email sender, TimeProvider: SINGLETON — stateless, thread-safe, depend only on singletons.
    /// - Validators: TRANSIENT — tiny stateless objects; a new one per use is cheap and always safe.
    /// </summary>
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ITokenService, TokenService>();

        // Swap this one line for an SMTP / email-provider implementation in production.
        services.AddSingleton<IEmailSender, PickupDirectoryEmailSender>();

        services.AddScoped<ICategoryRepository, CategoryRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IProjectRepository, ProjectRepository>();
        services.AddScoped<ITaskRepository, TaskRepository>();
        services.AddScoped<ICommentRepository, CommentRepository>();
        services.AddScoped<IDashboardRepository, DashboardRepository>();
        services.AddScoped<IDashboardService, DashboardService>();

        services.AddScoped<ICategoryService, CategoryService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IProjectService, ProjectService>();
        services.AddScoped<ITaskService, TaskService>();
        services.AddScoped<ICommentService, CommentService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IAccountService, AccountService>();
        services.AddScoped<IAccountEmailService, AccountEmailService>(); // scoped: uses UserManager

        services.AddScoped<DbSeeder>();

        services.AddValidatorsFromAssemblyContaining(typeof(ServiceCollectionExtensions), ServiceLifetime.Transient);

        return services;
    }

    /// <summary>
    /// CORS: lets browser apps on the configured origins call this API.
    /// - Exact origins only (no "*"); tokens travel in the Authorization header, so no cookies/credentials.
    /// - Exposes X-Correlation-ID so front-end code can read it and show it in error messages.
    /// - Preflight responses are cached by the browser for 10 minutes.
    /// </summary>
    public static IServiceCollection AddCorsPolicy(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection(CorsSettings.SectionName).Get<CorsSettings>() ?? new CorsSettings();

        foreach (var origin in settings.AllowedOrigins)
        {
            if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) || uri.AbsolutePath != "/" || origin.EndsWith('/'))
            {
                throw new InvalidOperationException(
                    $"Invalid CORS origin '{origin}'. Use scheme://host[:port] without a path or trailing slash.");
            }
        }

        services.AddCors(options => options.AddPolicy(CorsSettings.PolicyName, policy => policy
            .WithOrigins(settings.AllowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .WithExposedHeaders(Middleware.CorrelationIdMiddleware.HeaderName)
            .SetPreflightMaxAge(TimeSpan.FromMinutes(10))));

        return services;
    }

    /// <summary>
    /// Generates the OpenAPI document (served at /openapi/v1.json), including the JWT "Bearer"
    /// security scheme so Swagger UI shows an "Authorize" button and a lock on protected endpoints.
    /// </summary>
    public static IServiceCollection AddApiDocumentation(this IServiceCollection services)
    {
        services.AddOpenApi(options =>
        {
            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Info.Title = "Task Management System API";
                document.Info.Version = "v1";
                document.Info.Description =
                    "ASP.NET Core (.NET 10) monolithic Web API for managing projects, tasks, categories and comments.\n\n" +
                    "To call protected endpoints: POST /api/auth/login, copy the accessToken, click **Authorize**, paste it.";

                document.Components ??= new OpenApiComponents();
                document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
                document.Components.SecuritySchemes[BearerSchemeId] = new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer",
                    BearerFormat = "JWT",
                    In = ParameterLocation.Header,
                    Description = "Paste the accessToken from POST /api/auth/login (without the word 'Bearer')."
                };
                return Task.CompletedTask;
            });

            // Mark only endpoints that require authentication ([Authorize] without [AllowAnonymous]).
            options.AddOperationTransformer((operation, context, _) =>
            {
                var metadata = context.Description.ActionDescriptor.EndpointMetadata;
                var requiresAuth = metadata.OfType<IAuthorizeData>().Any()
                                   && !metadata.OfType<IAllowAnonymous>().Any();
                if (requiresAuth)
                {
                    operation.Security ??= [];
                    operation.Security.Add(new OpenApiSecurityRequirement
                    {
                        [new OpenApiSecuritySchemeReference(BearerSchemeId, context.Document)] = []
                    });
                }
                return Task.CompletedTask;
            });
        });

        return services;
    }
}
