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
    /// Receive a Magento order and push it to Uniconta synchronously.
    /// Returns 200 OK on success, 422 Unprocessable on Uniconta failure.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Submit([FromBody] OrderRequest request)
    {
        if (request.OrderId <= 0)
            return BadRequest(new OrderResponse { Message = "Invalid OrderId" });

        if (string.IsNullOrWhiteSpace(request.Email))
            return BadRequest(new OrderResponse { Message = "Email is required" });

        UnicontaServiceClient client;
        try
        {
            client = await _factory.CreateAsync();
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new OrderResponse { Message = ex.Message });
        }

        var result = await _orderService.ProcessAsync(request, client);

        if (result.Success)
        {
            _logger.LogInformation("Order {OrderId} submitted to Uniconta successfully", request.OrderId);
            return Ok(new OrderResponse { Accepted = true, Message = result.Message });
        }

        _logger.LogWarning("Order {OrderId} failed Uniconta submission: {Message}", request.OrderId, result.Message);
        return UnprocessableEntity(new OrderResponse { Accepted = false, Message = result.Message });
    }
}
