using Hangfire;
using IonCrm.API.Middleware;
using IonCrm.Application;
using IonCrm.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using System.Text;

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
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// ── JWT Authentication ────────────────────────────────────────────────────────
// SECURITY: No fallback — application must NOT start without a real secret.
// Set via environment variable: Jwt__Key=<random-256-bit-value>
var jwtKey = builder.Configuration["Jwt:Key"];
if (string.IsNullOrWhiteSpace(jwtKey))
    throw new InvalidOperationException(
        "JWT signing key is not configured. Set the 'Jwt:Key' environment variable " +
        "(minimum 32 characters, cryptographically random). " +
        "Example: JWT__KEY=$(openssl rand -base64 32)");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "IonCrm",
            ValidAudience = builder.Configuration["Jwt:Audience"] ?? "IonCrm",
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ClockSkew = TimeSpan.FromSeconds(30)
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

// ── Swagger (Development only) ────────────────────────────────────────────────
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddSwaggerGen();
}

// ── CORS ──────────────────────────────────────────────────────────────────────
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
            ?? new[] { "http://localhost:3000" };
        policy.WithOrigins(origins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

var app = builder.Build();

// ── Middleware pipeline ───────────────────────────────────────────────────────
app.UseMiddleware<GlobalExceptionMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors("AllowFrontend");
app.UseAuthentication();
app.UseAuthorization();

// ── Hangfire Dashboard (SuperAdmin only, Development + Staging) ───────────────
if (!app.Environment.IsProduction())
{
    app.UseHangfireDashboard("/hangfire", new DashboardOptions
    {
        // Restrict dashboard access — anonymous is off in non-development too
        Authorization = new[] { new HangfireAdminAuthFilter() }
    });
}

app.MapControllers();

app.Run();

// Make Program class accessible for integration tests
public partial class Program { }
