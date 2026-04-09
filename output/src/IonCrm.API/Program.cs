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

// ── Auto-apply pending EF Core migrations after app starts listening ──────────
// Run migrations AFTER the app is already accepting requests so Railway's
// health-check probes succeed during the (potentially slow) Neon cold-start.
app.Lifetime.ApplicationStarted.Register(() =>
{
    _ = Task.Run(async () =>
    {
        ApplicationDbContext? db = null;
        try
        {
            using var scope = app.Services.CreateScope();
            db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Bootstrap FAILED to resolve DbContext — aborting startup SQL");
            return;
        }

        // Local helper: runs an idempotent bootstrap SQL block in its own try/catch so a
        // single failing block does NOT prevent the rest of the bootstrap from executing.
        // Previously a single outer catch swallowed the first exception and skipped every
        // subsequent table-creation step, causing late tables (e.g. CustomerContracts) to
        // never get created — observed in prod as a 500 "relation does not exist".
        async Task RunSafe(string label, string sql)
        {
            try
            {
                await db.Database.ExecuteSqlRawAsync(sql);
                Log.Information("Bootstrap OK: {Label}", label);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Bootstrap FAILED (continuing): {Label}", label);
            }
        }

        // EF Core migrations — isolated so a migration failure does NOT skip the
        // idempotent CREATE TABLE fallback blocks below (they exist precisely to recover
        // from missing migrations).
        try
        {
            await db.Database.MigrateAsync();
            Log.Information("EF Core migrations applied successfully");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "EF Core MigrateAsync FAILED — falling back to idempotent SQL bootstrap");
        }

        // ── CustomerContracts FIRST ───────────────────────────────────────────────
        // Run BEFORE all other bootstrap blocks so that even if every other block fails,
        // the contracts table still gets created. The contract endpoints are the ones
        // that have been hitting "relation does not exist" in prod.
        await RunSafe("CustomerContracts table + indexes", @"
            CREATE TABLE IF NOT EXISTS ""CustomerContracts"" (
                ""Id""                       uuid         NOT NULL DEFAULT gen_random_uuid() PRIMARY KEY,
                ""ProjectId""                uuid         NOT NULL,
                ""CustomerId""               uuid         NOT NULL,
                ""Title""                    text         NOT NULL DEFAULT '',
                ""MonthlyAmount""            numeric(18,2) NOT NULL DEFAULT 0,
                ""PaymentType""              integer      NOT NULL DEFAULT 0,
                ""StartDate""                timestamp with time zone NOT NULL DEFAULT now(),
                ""DurationMonths""           integer,
                ""EndDate""                  timestamp with time zone,
                ""Status""                   integer      NOT NULL DEFAULT 0,
                ""RezervalSubscriptionId""   text,
                ""RezervalPaymentPlanId""    text,
                ""NextInvoiceDate""          timestamp with time zone,
                ""LastInvoiceGeneratedDate"" timestamp with time zone,
                ""CreatedAt""                timestamp with time zone NOT NULL DEFAULT now(),
                ""UpdatedAt""                timestamp with time zone NOT NULL DEFAULT now(),
                ""IsDeleted""                boolean      NOT NULL DEFAULT false,
                CONSTRAINT ""fk_customercontracts_projects"" FOREIGN KEY (""ProjectId"")
                    REFERENCES ""Projects"" (""Id"") ON DELETE CASCADE,
                CONSTRAINT ""fk_customercontracts_customers"" FOREIGN KEY (""CustomerId"")
                    REFERENCES ""Customers"" (""Id"") ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS ""ix_customercontracts_projectid_customerid""
                ON ""CustomerContracts"" (""ProjectId"", ""CustomerId"")
                WHERE ""IsDeleted"" = false;
            CREATE INDEX IF NOT EXISTS ""ix_customercontracts_customerid_status""
                ON ""CustomerContracts"" (""CustomerId"", ""Status"")
                WHERE ""IsDeleted"" = false;
            CREATE INDEX IF NOT EXISTS ""ix_customercontracts_due""
                ON ""CustomerContracts"" (""Status"", ""PaymentType"", ""NextInvoiceDate"")
                WHERE ""IsDeleted"" = false;
        ");

        // ── Idempotent column adds (later-sprint additions) ─────────────────────
        await RunSafe("Customers/Projects ADD COLUMN IF NOT EXISTS", @"
            ALTER TABLE ""Customers"" ADD COLUMN IF NOT EXISTS ""ExpirationDate""      timestamp with time zone;
            ALTER TABLE ""Customers"" ADD COLUMN IF NOT EXISTS ""ParasutContactId""  text;
            ALTER TABLE ""Customers"" ADD COLUMN IF NOT EXISTS ""IsEInvoicePayer""   boolean NOT NULL DEFAULT false;
            ALTER TABLE ""Customers"" ADD COLUMN IF NOT EXISTS ""EInvoiceAddress""   text;
            ALTER TABLE ""Customers"" ADD COLUMN IF NOT EXISTS ""MonthlyLicenseFee"" numeric;
            ALTER TABLE ""Projects""  ADD COLUMN IF NOT EXISTS ""EmsBaseUrl""        text;
            ALTER TABLE ""Projects""  ADD COLUMN IF NOT EXISTS ""EmsApiKey""         text;
            ALTER TABLE ""Projects""  ADD COLUMN IF NOT EXISTS ""RezervAlBaseUrl""   text;
            ALTER TABLE ""Projects""  ADD COLUMN IF NOT EXISTS ""RezervAlApiKey""    text;
            ALTER TABLE ""Projects""  ADD COLUMN IF NOT EXISTS ""SmsCount""          integer NOT NULL DEFAULT 0;
        ");

        // Idempotent cleanup: drop obsolete columns that have been removed from the domain model.
        await RunSafe("Drop obsolete Opportunities.Value", @"
            ALTER TABLE ""Opportunities"" DROP COLUMN IF EXISTS ""Value"";
        ");

        // One-time cleanup: delete customers with LegacyId = 'REZV-0' — these were created
        // by a bug where the Rezerval create-company response envelope was not unwrapped,
        // causing CompanyId = 0 to be stored. Next sync will re-create them with correct IDs.
        try
        {
            var deleted = await db.Database.ExecuteSqlRawAsync(@"
                DELETE FROM ""Customers""
                WHERE ""LegacyId"" = 'REZV-0'
                  AND ""IsDeleted"" = false;
            ");
            if (deleted > 0)
                Log.Warning("Cleaned up {Count} customer(s) with invalid LegacyId 'REZV-0'.", deleted);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Bootstrap FAILED (continuing): REZV-0 cleanup");
        }

        // Idempotent fallback: create ParasutConnections table if EF migration hasn't run yet.
        await RunSafe("ParasutConnections table + index", @"
            CREATE TABLE IF NOT EXISTS ""ParasutConnections"" (
                ""Id""             uuid         NOT NULL DEFAULT gen_random_uuid() PRIMARY KEY,
                ""ProjectId""      uuid         NOT NULL,
                ""CompanyId""      bigint       NOT NULL DEFAULT 0,
                ""ClientId""       text         NOT NULL DEFAULT '',
                ""ClientSecret""   text         NOT NULL DEFAULT '',
                ""Username""       text         NOT NULL DEFAULT '',
                ""Password""       text         NOT NULL DEFAULT '',
                ""AccessToken""    text,
                ""RefreshToken""   text,
                ""TokenExpiresAt"" timestamp with time zone,
                ""CreatedAt""      timestamp with time zone NOT NULL DEFAULT now(),
                ""UpdatedAt""      timestamp with time zone NOT NULL DEFAULT now(),
                ""IsDeleted""      boolean      NOT NULL DEFAULT false,
                CONSTRAINT ""fk_parasutconnections_projects"" FOREIGN KEY (""ProjectId"")
                    REFERENCES ""Projects"" (""Id"") ON DELETE CASCADE
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ""ix_parasutconnections_projectid""
                ON ""ParasutConnections"" (""ProjectId"")
                WHERE ""IsDeleted"" = false;
        ");

        // Reconnect tracking columns for ParasutAutoConnectService
        await RunSafe("ParasutConnections reconnect tracking columns", @"
            ALTER TABLE ""ParasutConnections"" ADD COLUMN IF NOT EXISTS ""LastConnectedAt""   timestamp with time zone;
            ALTER TABLE ""ParasutConnections"" ADD COLUMN IF NOT EXISTS ""LastError""         text;
            ALTER TABLE ""ParasutConnections"" ADD COLUMN IF NOT EXISTS ""ReconnectAttempts"" integer NOT NULL DEFAULT 0;
        ");

        // Global connection support: ProjectId becomes nullable (NULL = global, shared by all projects).
        await RunSafe("ParasutConnections nullable + partial indexes", @"
            ALTER TABLE ""ParasutConnections"" ALTER COLUMN ""ProjectId"" DROP NOT NULL;
            DROP INDEX IF EXISTS ""ix_parasutconnections_projectid"";
            CREATE UNIQUE INDEX IF NOT EXISTS ""ix_parasutconnections_projectid_notnull""
                ON ""ParasutConnections"" (""ProjectId"")
                WHERE ""ProjectId"" IS NOT NULL AND ""IsDeleted"" = false;
            CREATE UNIQUE INDEX IF NOT EXISTS ""ix_parasutconnections_global""
                ON ""ParasutConnections"" ((0))
                WHERE ""ProjectId"" IS NULL AND ""IsDeleted"" = false;
        ");

        // Idempotent fallback: create Invoices table if EF migration hasn't run yet.
        await RunSafe("Invoices table + indexes", @"
            CREATE TABLE IF NOT EXISTS ""Invoices"" (
                ""Id""             uuid         NOT NULL DEFAULT gen_random_uuid() PRIMARY KEY,
                ""ProjectId""      uuid         NOT NULL,
                ""CustomerId""     uuid         NOT NULL,
                ""Title""          text         NOT NULL DEFAULT '',
                ""Description""    text,
                ""InvoiceSeries""  text,
                ""InvoiceNumber""  integer,
                ""IssueDate""      timestamp with time zone NOT NULL DEFAULT now(),
                ""DueDate""        timestamp with time zone NOT NULL DEFAULT now(),
                ""Currency""       text         NOT NULL DEFAULT 'TRL',
                ""GrossTotal""     numeric      NOT NULL DEFAULT 0,
                ""NetTotal""       numeric      NOT NULL DEFAULT 0,
                ""LinesJson""      text         NOT NULL DEFAULT '[]',
                ""Status""         integer      NOT NULL DEFAULT 0,
                ""ParasutId""      text,
                ""CreatedAt""      timestamp with time zone NOT NULL DEFAULT now(),
                ""UpdatedAt""      timestamp with time zone NOT NULL DEFAULT now(),
                ""IsDeleted""      boolean      NOT NULL DEFAULT false,
                CONSTRAINT ""fk_invoices_projects""  FOREIGN KEY (""ProjectId"")  REFERENCES ""Projects""  (""Id"") ON DELETE CASCADE,
                CONSTRAINT ""fk_invoices_customers"" FOREIGN KEY (""CustomerId"") REFERENCES ""Customers"" (""Id"") ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS ""ix_invoices_projectid_status""
                ON ""Invoices"" (""ProjectId"", ""Status"")
                WHERE ""IsDeleted"" = false;
            CREATE INDEX IF NOT EXISTS ""ix_invoices_customerid""
                ON ""Invoices"" (""CustomerId"")
                WHERE ""IsDeleted"" = false;
        ");

        // Idempotent fallback: create ParasutProducts table (per-project product catalog for invoices).
        await RunSafe("ParasutProducts table + index", @"
            CREATE TABLE IF NOT EXISTS ""ParasutProducts"" (
                ""Id""               uuid         NOT NULL DEFAULT gen_random_uuid() PRIMARY KEY,
                ""ProjectId""        uuid         NOT NULL,
                ""ProductName""      text         NOT NULL DEFAULT '',
                ""ParasutProductId"" text         NOT NULL DEFAULT '',
                ""UnitPrice""        numeric      NOT NULL DEFAULT 0,
                ""TaxRate""          numeric      NOT NULL DEFAULT 0.20,
                ""CreatedAt""        timestamp with time zone NOT NULL DEFAULT now(),
                ""UpdatedAt""        timestamp with time zone NOT NULL DEFAULT now(),
                ""IsDeleted""        boolean      NOT NULL DEFAULT false,
                CONSTRAINT ""fk_parasutproducts_projects"" FOREIGN KEY (""ProjectId"")
                    REFERENCES ""Projects"" (""Id"") ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS ""ix_parasutproducts_projectid""
                ON ""ParasutProducts"" (""ProjectId"")
                WHERE ""IsDeleted"" = false;
        ");

        await RunSafe("ParasutProducts/Invoices late columns", @"
            ALTER TABLE ""ParasutProducts""
                ADD COLUMN IF NOT EXISTS ""ParasutProductName"" text;
            ALTER TABLE ""ParasutProducts""
                ADD COLUMN IF NOT EXISTS ""EmsProductId"" text;
            ALTER TABLE ""Invoices""
                ADD COLUMN IF NOT EXISTS ""EmsPaymentId"" text;
        ");

        // Fix: Segment was originally created as integer (enum) but the entity
        // uses string. Convert to text so EMS API string values can be stored.
        await RunSafe("Customers.Segment integer→text", @"
            DO $$
            BEGIN
                IF EXISTS (
                    SELECT 1 FROM information_schema.columns
                    WHERE table_name = 'Customers'
                      AND column_name = 'Segment'
                      AND data_type = 'integer'
                ) THEN
                    ALTER TABLE ""Customers""
                        ALTER COLUMN ""Segment"" TYPE text USING ""Segment""::text;
                END IF;
            END$$;
        ");

        // Set Status = Lead (1) for all customers with no ExpirationDate.
        await RunSafe("Customers Status=Lead backfill", @"
            UPDATE ""Customers""
            SET ""Status"" = 1
            WHERE ""ExpirationDate"" IS NULL
              AND ""IsDeleted"" = false;
        ");

        Log.Information("Startup bootstrap complete");
    });
});

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

// ── Pre-startup: ensure new columns exist before accepting requests ────────────
// Runs synchronously so EF Core never sees a missing column on the first request.
// Wrapped in try/catch — Neon cold-start may fail here; the background task will retry.
try
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await db.Database.ExecuteSqlRawAsync(@"
        ALTER TABLE ""ParasutProducts"" ADD COLUMN IF NOT EXISTS ""ParasutProductName"" text;
        ALTER TABLE ""ParasutProducts"" ADD COLUMN IF NOT EXISTS ""ProductKey""         text NOT NULL DEFAULT '';
        ALTER TABLE ""ParasutProducts"" ADD COLUMN IF NOT EXISTS ""EmsProductId""       text;
        ALTER TABLE ""Invoices""        ADD COLUMN IF NOT EXISTS ""EmsPaymentId""       text;
    ");
}
catch (Exception ex)
{
    Log.Warning(ex, "Pre-startup column check failed (Neon cold-start?) — background task will retry.");
}

app.Run();

// Make Program class accessible for integration tests
public partial class Program { }
