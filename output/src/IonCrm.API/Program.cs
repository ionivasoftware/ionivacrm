using AspNetCoreRateLimit;
using Hangfire;
using IonCrm.API.Middleware;
using IonCrm.Application;
using IonCrm.Infrastructure;
using IonCrm.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;
using System.Text;

// Npgsql: treat Unspecified DateTime as UTC (prevents timestamptz errors from frontend input)
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

var builder = WebApplication.CreateBuilder(args);

// ── Serilog ───────────────────────────────────────────────────────────────────
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateLogger();

builder.Host.UseSerilog();

// ── Application + Infrastructure layers ──────────────────────────────────────
builder.Services.AddApplicationServices();
builder.Services.AddInfrastructureServices(builder.Configuration);

// ── Controllers ───────────────────────────────────────────────────────────────
builder.Services.AddControllers()
    .AddJsonOptions(opts =>
    {
        opts.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
        opts.JsonSerializerOptions.Converters.Add(new IonCrm.API.Common.UtcDateTimeConverter());
        opts.JsonSerializerOptions.Converters.Add(new IonCrm.API.Common.UtcNullableDateTimeConverter());
    });
builder.Services.AddEndpointsApiExplorer();

// ── JWT Authentication ────────────────────────────────────────────────────────
// SECURITY: No fallback — application must NOT start without a real secret.
// Set via environment variable: JwtSettings__Secret=<random-256-bit-value>
var jwtSecret = builder.Configuration["JwtSettings:Secret"];
if (string.IsNullOrWhiteSpace(jwtSecret))
    throw new InvalidOperationException(
        "JWT signing secret is not configured. Set the 'JwtSettings:Secret' environment variable " +
        "(minimum 32 characters, cryptographically random). " +
        "Example: JwtSettings__Secret=$(openssl rand -base64 32)");

var jwtIssuer   = builder.Configuration["JwtSettings:Issuer"]   ?? "IonCrm";
var jwtAudience = builder.Configuration["JwtSettings:Audience"] ?? "IonCrmUsers";

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer           = true,
            ValidateAudience         = true,
            ValidateLifetime         = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer              = jwtIssuer,
            ValidAudience            = jwtAudience,
            IssuerSigningKey         = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ClockSkew                = TimeSpan.FromSeconds(30)
        };
    });

// ── Authorization policies ────────────────────────────────────────────────────
builder.Services.AddAuthorization(options =>
{
    // SuperAdmin policy: claim "isSuperAdmin" must be "true"
    options.AddPolicy("SuperAdmin", policy =>
        policy.RequireClaim("isSuperAdmin", "true"));

    // Default policy: must be authenticated
    options.DefaultPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

// ── Rate Limiting (AspNetCoreRateLimit) ───────────────────────────────────────
// Limits /auth endpoints to 10 requests/minute per IP address.
// Rules are defined in appsettings.json under IpRateLimiting.GeneralRules.
builder.Services.AddMemoryCache();
builder.Services.AddInMemoryRateLimiting();
builder.Services.Configure<IpRateLimitOptions>(
    builder.Configuration.GetSection("IpRateLimiting"));
builder.Services.AddSingleton<IRateLimitConfiguration, RateLimitConfiguration>();

// ── Swagger / OpenAPI ─────────────────────────────────────────────────────────
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title       = "ION CRM API",
        Version     = "v1",
        Description = "Multi-tenant CRM backend — ION CRM"
    });

    // JWT Bearer security definition
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name        = "Authorization",
        Type        = SecuritySchemeType.Http,
        Scheme      = "bearer",
        BearerFormat = "JWT",
        In          = ParameterLocation.Header,
        Description = "Enter your JWT access token (without 'Bearer ' prefix)."
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id   = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

// ── CORS ──────────────────────────────────────────────────────────────────────
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        var origins = builder.Configuration
            .GetSection("Cors:AllowedOrigins").Get<string[]>()
            ?? new[] { "http://localhost:3000" };
        policy.WithOrigins(origins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

// ─────────────────────────────────────────────────────────────────────────────
var app = builder.Build();
// ─────────────────────────────────────────────────────────────────────────────

// ── Auto-apply pending EF Core migrations on startup ─────────────────────────
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

    try
    {
        await db.Database.MigrateAsync();
        Log.Information("EF Core migrations applied successfully");
    }
    catch (Exception ex)
    {
        Log.Warning(ex, "EF Core auto-migration failed — attempting raw SQL column fallback");
    }

    // Idempotent fallback: ensure columns added in later sprints exist regardless of EF migration state.
    // Uses IF NOT EXISTS so it is safe to run on every startup.
    try
    {
        await db.Database.ExecuteSqlRawAsync(@"
            ALTER TABLE ""Customers"" ADD COLUMN IF NOT EXISTS ""ExpirationDate""    timestamp with time zone;
            ALTER TABLE ""Customers"" ADD COLUMN IF NOT EXISTS ""ParasutContactId""  text;
            ALTER TABLE ""Projects""  ADD COLUMN IF NOT EXISTS ""EmsApiKey""         text;
            ALTER TABLE ""Projects""  ADD COLUMN IF NOT EXISTS ""RezervAlApiKey""    text;
        ");
        Log.Information("Column existence check complete");
    }
    catch (Exception ex)
    {
        Log.Warning(ex, "Raw SQL column fallback failed — some endpoints may return 500 until DB is updated");
    }
}

// ── Middleware pipeline ───────────────────────────────────────────────────────
app.UseMiddleware<GlobalExceptionMiddleware>();

// ── IP Rate Limiting (must be early in the pipeline) ─────────────────────────
app.UseIpRateLimiting();

// ── Health check (no auth required) ──────────────────────────────────────────
app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }))
   .AllowAnonymous();

// ── Swagger UI (non-production only) ─────────────────────────────────────────
// SECURITY FIX (CRITICAL-002): Swagger was unconditionally enabled, exposing the full
// API schema in production. Restricted to Development / Staging only.
if (!app.Environment.IsProduction())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "ION CRM API v1");
        c.RoutePrefix = "swagger";
    });
}

// Note: HTTPS redirection is intentionally omitted — Railway terminates TLS at the edge.
app.UseCors("AllowFrontend");
app.UseAuthentication();
app.UseAuthorization();

// ── Tenant middleware (runs after auth to log tenant context) ─────────────────
app.UseMiddleware<TenantMiddleware>();

// ── Hangfire Dashboard (SuperAdmin only, non-production) ──────────────────────
var hangfireEnabled = app.Configuration.GetValue<bool>("Hangfire:Enabled", false);
if (hangfireEnabled && !app.Environment.IsProduction())
{
    app.UseHangfireDashboard("/hangfire", new DashboardOptions
    {
        Authorization = new[] { new HangfireAdminAuthFilter() }
    });
}

app.MapControllers();

app.Run();

// Make Program class accessible for integration tests
public partial class Program { }
