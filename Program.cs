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
    SoftcodeUnicontaMiddleware.Data.DbSeeder.Seed(db, hasher);
}

app.Run();
