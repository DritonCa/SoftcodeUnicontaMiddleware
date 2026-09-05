using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using SoftcodeUnicontaMiddleware.Data;
using SoftcodeUnicontaMiddleware.Filters;
using SoftcodeUnicontaMiddleware.Services;
using SoftcodeUnicontaMiddleware.UnicontaService;
using System.Text;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// ----------------------------------------------------
// SERVICES
// ----------------------------------------------------

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddMemoryCache();

// ✅ REQUIRED for UnicontaServiceClientFactory
builder.Services.AddHttpContextAccessor();

// 🔐 Fail fast on missing or placeholder secrets before anything else starts.
// A predictable JWT key or client-secret pepper is a real vulnerability, so the
// app refuses to boot until real values are configured.
var jwtKey = StartupSecrets.Require(builder.Configuration, "Jwt:Key", 32);
StartupSecrets.Require(builder.Configuration, "Auth:SecretPepper", 16);

// 🔐 JWT AUTH
var jwt = builder.Configuration.GetSection("Jwt");

var key = Encoding.UTF8.GetBytes(jwtKey);

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwt["Issuer"],
            ValidAudience = jwt["Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(key)
        };
    })
    // 🍪 Cookie scheme for the browser order-log interface only (JWT stays the
    // default, so the API clients are unaffected).
    .AddCookie(SoftcodeUnicontaMiddleware.Controllers.AdminController.CookieScheme, options =>
    {
        options.Cookie.Name         = "sc_admin";
        options.Cookie.HttpOnly     = true;
        options.Cookie.SameSite     = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.ExpireTimeSpan      = TimeSpan.FromHours(8);
        options.SlidingExpiration   = true;
        // Return status codes instead of redirecting (the UI is a single page).
        options.Events.OnRedirectToLogin        = ctx => { ctx.Response.StatusCode = 401; return Task.CompletedTask; };
        options.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = 403; return Task.CompletedTask; };
    });

builder.Services.AddAuthorization();

builder.Services.AddScoped<UnicontaServiceClientFactory>();

// Connection string comes from configuration (appsettings / env / user-secrets),
// falling back to a local dev SQLite file. Never hard-code infrastructure paths.
var connectionString = builder.Configuration.GetConnectionString("AppDb")
    ?? "Data Source=softcode_api.db";

builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseSqlite(connectionString);
});


builder.Services.AddSingleton<SecretHasher>();
builder.Services.AddScoped<IClientAuthService, ClientAuthService>();
builder.Services.AddScoped<ClientAuthFilter>();
builder.Services.AddScoped<JwtTokenService>();
builder.Services.AddScoped<IUnicontaCredentialStore, MemoryUnicontaCredentialStore>();
builder.Services
    .AddDataProtection()
    .PersistKeysToFileSystem(
        new DirectoryInfo(Path.Combine(
            builder.Environment.ContentRootPath,
            "dataprotection-keys")))
    .SetApplicationName("SoftcodeUnicontaMiddleware");
builder.Services.AddScoped<IRefreshTokenStore, MemoryRefreshTokenStore>();
// NOTE: AddHttpContextAccessor() is already registered above (line ~25); duplicate removed.
builder.Services.AddScoped<IAuditLogger, AuditLogger>();
builder.Services.AddScoped<SoftcodeUnicontaMiddleware.Services.OrderService>();
builder.Services.AddSingleton<SoftcodeUnicontaMiddleware.Services.IOrderLogger, SoftcodeUnicontaMiddleware.Services.OrderLogger>();

// Order-log admin interface
builder.Services.AddScoped<SoftcodeUnicontaMiddleware.Services.IAdminUserService, SoftcodeUnicontaMiddleware.Services.AdminUserService>();
builder.Services.AddSingleton<SoftcodeUnicontaMiddleware.Services.OrderLogReader>();

builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("auth", context =>
    {
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var clientId = context.Request.Headers["X-Client-Id"].FirstOrDefault();

        var key = string.IsNullOrWhiteSpace(clientId)
            ? $"auth:ip:{ip}"
            : $"auth:client:{clientId}:ip:{ip}";

        return RateLimitPartition.GetSlidingWindowLimiter(
            key,
            _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = 5,               // very strict
                Window = TimeSpan.FromMinutes(1),
                SegmentsPerWindow = 1,
                QueueLimit = 0
            });
    });
});


var app = builder.Build();

// ----------------------------------------------------
// PIPELINE
// ----------------------------------------------------

app.UseRateLimiter();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthentication();
app.UseAuthorization();

app.UseMiddleware<SoftcodeUnicontaMiddleware.Middleware.ApiExceptionMiddleware>();

app.MapControllers();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<SoftcodeUnicontaMiddleware.Data.AppDbContext>();
    var hasher = scope.ServiceProvider.GetRequiredService<SoftcodeUnicontaMiddleware.Services.SecretHasher>();
    db.Database.Migrate();

    // AdminUsers is managed outside EF migrations (kept isolated from the security
    // schema) — ensure it exists on startup.
    db.Database.ExecuteSqlRaw(
        @"CREATE TABLE IF NOT EXISTS ""AdminUsers"" (
            ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_AdminUsers"" PRIMARY KEY AUTOINCREMENT,
            ""Username"" TEXT NOT NULL,
            ""PasswordHash"" TEXT NOT NULL,
            ""CreatedAt"" TEXT NOT NULL,
            ""LastLoginAt"" TEXT NULL
          );");
    db.Database.ExecuteSqlRaw(
        @"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_AdminUsers_Username"" ON ""AdminUsers"" (""Username"");");

    // ClientSecretEnc (reversibly-encrypted secret for the admin Companies view) is
    // added out-of-migration; SQLite has no ADD COLUMN IF NOT EXISTS, so ignore the
    // "duplicate column" error on subsequent boots.
    try
    {
        db.Database.ExecuteSqlRaw(@"ALTER TABLE ""Clients"" ADD COLUMN ""ClientSecretEnc"" TEXT NULL;");
    }
    catch
    {
        /* column already exists */
    }

    SoftcodeUnicontaMiddleware.Data.DbSeeder.Seed(db, hasher);
}

app.Run();
