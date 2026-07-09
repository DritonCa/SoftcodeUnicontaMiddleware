using SoftcodeUnicontaMiddleware.Models.Orders;
using OrderResult = SoftcodeUnicontaMiddleware.Models.Orders.OrderResult;
using SoftcodeUnicontaMiddleware.UnicontaService;
using Uniconta.ClientTools.DataModel;
using Uniconta.Common;
using Uniconta.DataModel;

namespace SoftcodeUnicontaMiddleware.Services;

public class OrderService
{
    private readonly ILogger<OrderService> _logger;
    private readonly IOrderLogger _orderLog;

    public OrderService(ILogger<OrderService> logger, IOrderLogger orderLog)
    {
        _logger   = logger;
        _orderLog = orderLog;
    }

    public async Task<OrderResult> ProcessAsync(OrderRequest req, UnicontaServiceClient client)
    {
        try
        {
            _logger.LogInformation("Processing Uniconta order {OrderId} type={Type}", req.OrderId, req.CustomerType);

            var debtors = await client.GetAllDebtorsAsync();
            var account = FindDebtorAccount(debtors, req);
            var debtorStatus = "existing";

            if (account == null)
            {
                debtorStatus = "new";
                var debtor       = BuildDebtor(req);
                var createResult = await client.CreateDebtorAsync(debtor);
                if (createResult != ErrorCodes.Succes)
                {
                    var msg = $"CreateDebtor returned {createResult}";
                    _logger.LogWarning(msg + " for order {OrderId}", req.OrderId);
                    _orderLog.LogFailed(req.OrderId, req.CustomerType, req.Email, msg);
                    return new OrderResult { Success = false, Message = msg };
                }

                account = req.CustomerType == "ean" ? (req.Ean ?? req.Email)
                        : req.CustomerType == "cvr" ? (req.Cvr ?? req.Email)
                        : req.Email;
            }

            var order       = BuildOrderHeader(req, account);
            var orderResult = await client.CreateOrderHeaderAsync(order);
            if (orderResult != ErrorCodes.Succes)
            {
                var msg = $"CreateOrderHeader returned {orderResult}";
                _logger.LogError(msg + " for order {OrderId}", req.OrderId);
                _orderLog.LogFailed(req.OrderId, req.CustomerType, req.Email, msg);
                return new OrderResult { Success = false, Message = msg };
            }

            var onlyCourseItems = req.Items.All(i => i.IsCourseOrModule);
            var createdLines    = new List<DebtorOrderLineClient>();

            foreach (var item in req.Items)
            {
                var line       = BuildOrderLine(req.OrderId, item, req.PricesIncludeTax);
                var lineResult = await client.CreateOrderLineAsync(line);
                if (lineResult != ErrorCodes.Succes)
                {
                    var warn = $"CreateOrderLine returned {lineResult}";
                    _logger.LogWarning(warn + " SKU={Sku} order={OrderId}", item.Sku, req.OrderId);
                    _orderLog.LogLineWarning(req.OrderId, item.Sku, warn);
                }
                else
                {
                    createdLines.Add(line);
                }
            }

            if (!onlyCourseItems && req.ShippingAmount > 0)
            {
                var shippingLine = new DebtorOrderLineClient
                {
                    _OrderNumber = req.OrderId,
                    _Item        = req.ShippingProductSku,
                    _Price       = req.ShippingAmount,
                    _Qty         = 1,
                    _Storage     = StorageRegister.Move
                };
                var shResult = await client.CreateOrderLineAsync(shippingLine);
                if (shResult != ErrorCodes.Succes)
                {
                    var warn = $"Shipping line returned {shResult}";
                    _logger.LogWarning(warn + " for order {OrderId}", req.OrderId);
                    _orderLog.LogLineWarning(req.OrderId, req.ShippingProductSku, warn);
                }
                else
                {
                    createdLines.Add(shippingLine);
                }
            }

            if (req.CustomerType == "privat" && createdLines.Count > 0)
            {
                var invoiceResult = await client.PostInvoiceAsync(order, createdLines.ToArray());
                if (invoiceResult == null || invoiceResult.Err != ErrorCodes.Succes)
                {
                    var warn = $"PostInvoice returned {invoiceResult?.Err}";
                    _logger.LogWarning(warn + " for order {OrderId}", req.OrderId);
                    _orderLog.LogLineWarning(req.OrderId, "INVOICE", warn);
                }
                else
                {
                    _logger.LogInformation("Invoice posted in Uniconta for order {OrderId}", req.OrderId);
                }
            }

            _logger.LogInformation("Uniconta order {OrderId} submitted successfully", req.OrderId);
            _orderLog.LogSubmitted(req.OrderId, req.CustomerType, req.Email, $"{account} ({debtorStatus})");
            return new OrderResult { Success = true, Message = $"Order submitted to debtor {account} ({debtorStatus})" };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Order processing failed for order {OrderId}", req.OrderId);
            _orderLog.LogFailed(req.OrderId, req.CustomerType, req.Email, ex.Message);
            return new OrderResult { Success = false, Message = ex.Message };
        }
    }

    // ---- Debtor matching -------------------------------------------------------

    private static string? FindDebtorAccount(Debtor[] debtors, OrderRequest req)
    {
        foreach (var d in debtors)
        {
            bool emailMatch   = !string.IsNullOrEmpty(req.Email) && d._ContactEmail == req.Email;
            bool addressMatch = !string.IsNullOrEmpty(req.DeliveryAddress) && d._Address1 == req.DeliveryAddress;

            if (emailMatch || addressMatch)
                return d._Account;
        }
        return null;
    }

    // ---- Debtor creation -------------------------------------------------------

    private static DebtorClient BuildDebtor(OrderRequest req)
    {
        var debtor = new DebtorClient
        {
            _Name          = !string.IsNullOrEmpty(req.CompanyName) ? req.CompanyName : req.ContactName,
            _Address1      = req.DeliveryAddress,
            _ZipCode       = req.DeliveryPostcode,
            _City          = req.DeliveryCity,
            _Country       = CountryCode.Denmark,
            _ContactPerson = req.ContactName,
            _ContactEmail  = req.Email,
            _MobilPhone    = req.Phone ?? "",
            _Payment       = req.PaymentCode,
            _Vat           = "U25",
            _Group         = req.CustomerType == "privat" ? "PRIV" : "ERHVERV"
        };

        if (req.CustomerType == "ean" && !string.IsNullOrEmpty(req.Ean))
            debtor._EAN = req.Ean;

        return debtor;
    }

    // ---- Order header ----------------------------------------------------------

    private static DebtorOrderClient BuildOrderHeader(OrderRequest req, string account)
    {
        var order = new DebtorOrderClient
        {
            _DCAccount    = account,
            _OrderNumber  = req.OrderId,
            _ContactName  = req.ContactName,
            _Payment      = req.PaymentCode,
            _Requisition  = $"Webordre: 0{req.OrderId}",
            _YourRef      = req.ContactName,
            _SalesValue   = req.TotalPrice,
            _DeliveryCountry = CountryCode.Denmark
        };

        if (!string.IsNullOrEmpty(req.Comment))
            order._Remark = req.Comment;

        if (req.DeliveryType == 2)
        {
            // GLS parcel shop
            order._DeliveryName    = req.DeliveryName;
            order._DeliveryAddress1 = $"c/o {req.GlsShopName}";
            order._DeliveryAddress2 = req.GlsShopAddress ?? "";
            order._DeliveryZipCode  = req.GlsShopPostcode ?? "";
            order._DeliveryCity     = req.GlsShopCity ?? "";
        }
        else
        {
            // Home/company address
            order._DeliveryName    = req.DeliveryName;
            order._DeliveryAddress1 = $"Att.: {req.DeliveryName}";
            order._DeliveryAddress2 = req.DeliveryAddress;
            order._DeliveryZipCode  = req.DeliveryPostcode;
            order._DeliveryCity     = req.DeliveryCity;
        }

        return order;
    }

    // ---- Order line ------------------------------------------------------------

    private static DebtorOrderLineClient BuildOrderLine(int orderId, OrderItemRequest item, bool pricesIncludeTax)
    {
        // Modules (SKU 700-799) are always stored ex-VAT — use price as-is.
        // Other items: if Magento sends incl. VAT (25% DK), strip it here so Uniconta receives ex-VAT.
        double price;
        if (IsModulePriced(item.Sku))
            price = item.Price;
        else
            price = pricesIncludeTax ? item.Price * 0.8 : item.Price;

        return new DebtorOrderLineClient
        {
            _OrderNumber = orderId,
            _Item        = item.Sku,
            _Price       = price,
            _Qty         = item.Qty,
            _Storage     = StorageRegister.Move
        };
    }

    private static bool IsModulePriced(string sku) =>
        int.TryParse(sku, out var n) && n >= 700 && n <= 799;
}
