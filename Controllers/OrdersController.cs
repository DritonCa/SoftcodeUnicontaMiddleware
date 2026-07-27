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

    /// <summary>
    /// Post the invoice in Uniconta for an existing order (turns the sales
    /// order into a posted invoice). Called when the payment is captured.
    /// Returns 404 if the order does not exist (or was already fully invoiced —
    /// Uniconta removes fully invoiced orders).
    /// </summary>
    [HttpPost("{orderNumber:int}/invoice")]
    public async Task<IActionResult> Invoice(int orderNumber, [FromBody] InvoiceOrderRequest request)
    {
        if (orderNumber <= 0)
            return BadRequest(new OrderResponse { Message = "Invalid order number" });

        UnicontaServiceClient client;
        try
        {
            client = await _factory.CreateAsync();
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new OrderResponse { Message = ex.Message });
        }

        var order = await client.GetOrderByNumberAsync(orderNumber);
        if (order == null)
        {
            _logger.LogWarning("Invoice request: order {OrderNumber} not found in Uniconta (already invoiced?)", orderNumber);
            return NotFound(new OrderResponse { Message = $"Order {orderNumber} not found in Uniconta (already invoiced?)" });
        }

        if (!string.IsNullOrWhiteSpace(request.OurRef))
        {
            order._OurRef = request.OurRef;
            var updateResult = await client.UpdateOrderHeaderAsync(order);
            if (updateResult != Uniconta.Common.ErrorCodes.Succes)
                _logger.LogWarning("Could not set OurRef on order {OrderNumber}: {Result}", orderNumber, updateResult);
        }

        var lines = await client.GetOrderLinesAsync(order);
        if (lines.Length == 0)
        {
            _logger.LogWarning("Invoice request: order {OrderNumber} has no lines to invoice", orderNumber);
            return UnprocessableEntity(new OrderResponse { Message = $"Order {orderNumber} has no lines to invoice" });
        }

        var result = await client.PostInvoiceAsync(order, lines);
        if (result == null || result.Err != Uniconta.Common.ErrorCodes.Succes)
        {
            var msg = $"PostInvoice returned {result?.Err}";
            _logger.LogError("Invoice posting failed for order {OrderNumber}: {Message}", orderNumber, msg);
            return UnprocessableEntity(new OrderResponse { Accepted = false, Message = msg });
        }

        _logger.LogInformation("Invoice posted in Uniconta for order {OrderNumber}", orderNumber);
        return Ok(new OrderResponse { Accepted = true, Message = $"Invoice posted for order {orderNumber}" });
    }
}
