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

// 🔐 JWT AUTH
var jwt = builder.Configuration.GetSection("Jwt");

var jwtKey = jwt["Key"]
    ?? throw new InvalidOperationException("Jwt:Key is missing");

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

builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseSqlite("Data Source=softcode_api.db");
});


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
builder.Services.AddHttpContextAccessor();
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
    db.Database.Migrate();
    SoftcodeUnicontaMiddleware.Data.DbSeeder.Seed(db);
}

app.Run();
