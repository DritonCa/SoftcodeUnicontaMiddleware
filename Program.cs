using Microsoft.AspNetCore.Authentication.JwtBearer;
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
builder.Services.AddDataProtection();
builder.Services.AddScoped<IRefreshTokenStore, MemoryRefreshTokenStore>();

builder.Services.AddRateLimiter(options =>
{
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        var key = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        return RateLimitPartition.GetSlidingWindowLimiter(
            key,
            _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = 30,
                Window = TimeSpan.FromSeconds(10),
                SegmentsPerWindow = 10,
                QueueLimit = 0
            });
    });

    options.RejectionStatusCode = 429;
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

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<SoftcodeUnicontaMiddleware.Data.AppDbContext>();
    SoftcodeUnicontaMiddleware.Data.DbSeeder.Seed(db);
}

app.Run();
