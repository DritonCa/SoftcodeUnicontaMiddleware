using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SoftcodeUnicontaMiddleware.UnicontaService;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// ----------------------------------------------------
// SERVICES
// ----------------------------------------------------

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// ✅ REQUIRED: Memory cache (for UnicontaServiceClient)
builder.Services.AddMemoryCache();

// ✅ Uniconta session (singleton = one ERP session per API)
builder.Services.AddSingleton<UnicontaSessionService>();

// ----------------------------------------------------
// RATE LIMITING (GLOBAL)
// ----------------------------------------------------

builder.Services.AddRateLimiter(options =>
{
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        var key = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        return RateLimitPartition.GetSlidingWindowLimiter(
            partitionKey: key,
            factory: _ => new SlidingWindowRateLimiterOptions
            {
                PermitLimit = 30,              // 30 requests
                Window = TimeSpan.FromSeconds(10),
                SegmentsPerWindow = 10,        // smooth sliding window
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0                 // reject immediately
            });
    });

    options.RejectionStatusCode = 429;
});

var app = builder.Build();

// ----------------------------------------------------
// PIPELINE
// ----------------------------------------------------

app.UseRateLimiter();

// Swagger only in dev
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();
