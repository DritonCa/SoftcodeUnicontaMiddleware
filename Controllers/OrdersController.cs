using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SoftcodeUnicontaMiddleware.Models.Orders;
using SoftcodeUnicontaMiddleware.Services;
using SoftcodeUnicontaMiddleware.UnicontaService;

namespace SoftcodeUnicontaMiddleware.Controllers;

[Authorize]
[ApiController]
[Route("api/uniconta/orders")]
public class OrdersController : ControllerBase
{
    private readonly UnicontaServiceClientFactory _factory;
    private readonly OrderService _orderService;
    private readonly ILogger<OrdersController> _logger;

    public OrdersController(
        UnicontaServiceClientFactory factory,
        OrderService orderService,
        ILogger<OrdersController> logger)
    {
        _factory      = factory;
        _orderService = orderService;
        _logger       = logger;
    }

    /// <summary>
    /// Receive a Magento order and push it to Uniconta asynchronously.
    /// Returns 202 Accepted immediately; processing happens in a background thread.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Submit([FromBody] OrderRequest request)
    {
        if (request.OrderId <= 0)
            return BadRequest(new OrderResponse { Message = "Invalid OrderId" });

        if (string.IsNullOrWhiteSpace(request.Email))
            return BadRequest(new OrderResponse { Message = "Email is required" });

        // Initialize Uniconta client NOW while we still have the JWT HTTP context.
        // The factory reads claims from the current request — cannot be called after response.
        UnicontaServiceClient client;
        try
        {
            client = await _factory.CreateAsync();
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new OrderResponse { Message = ex.Message });
        }

        // Fire and forget — Uniconta API calls can be slow; don't block the caller.
        _ = Task.Run(async () =>
        {
            try
            {
                await _orderService.ProcessAsync(request, client);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error processing order {OrderId}", request.OrderId);
            }
        });

        _logger.LogInformation("Order {OrderId} accepted for Uniconta submission", request.OrderId);

        return Accepted(new OrderResponse
        {
            Accepted = true,
            Message  = $"Order {request.OrderId} queued for Uniconta submission"
        });
    }
}
