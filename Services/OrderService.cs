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
                debtorStatus     = "new";
                account          = NextAccountNumber(debtors);   // highest existing Uniconta account + 1
                var debtor       = BuildDebtor(req, account);
                var createResult = await client.CreateDebtorAsync(debtor);
                if (createResult != ErrorCodes.Succes)
                {
                    var msg = $"CreateDebtor returned {createResult}";
                    _logger.LogWarning(msg + " for order {OrderId}", req.OrderId);
                    _orderLog.LogFailed(req.OrderId, req.CustomerType, req.Email, msg, DescribeDebtor(debtor, req));
                    return new OrderResult { Success = false, Message = msg };
                }
            }

            var order       = BuildOrderHeader(req, account);
            var orderResult = await client.CreateOrderHeaderAsync(order);
            if (orderResult != ErrorCodes.Succes)
            {
                var msg = $"CreateOrderHeader returned {orderResult}";
                _logger.LogError(msg + " for order {OrderId}", req.OrderId);
                _orderLog.LogFailed(req.OrderId, req.CustomerType, req.Email, msg,
                    $"ORDER header create attempt:\n  Account={account}\n  OrderNumber={req.OrderId}\n" +
                    $"  Payment={req.PaymentCode}\n  SalesValue={req.TotalPrice}\n  DeliveryType={req.DeliveryType}");
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
                // Shipping is booked the way it is entered by hand in Uniconta:
                // one VAT-free amount in the "I alt" column, with no quantity and
                // no unit sales price. _AmountEntered is that column; a _Qty of 1
                // plus a _Price would instead print "1 ... 31,20" under Antal and
                // Salgspris, and would post the ex-VAT amount rather than what the
                // customer actually paid for delivery.
                var shippingLine = new DebtorOrderLineClient
                {
                    _OrderNumber   = req.OrderId,
                    _Item          = req.ShippingProductSku,
                    _AmountEntered = req.ShippingAmount,
                    _Storage       = StorageRegister.Move
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

            // Nothing is invoiced automatically — for any customer type. Only the
            // sales order is created here. The invoice is posted later via
            // POST /orders/{n}/invoice, which fires when the order is invoiced in
            // the Magento backend, so Uniconta only ever shows what has actually
            // been invoiced.
            _logger.LogInformation(
                "Order {OrderId} ({CustomerType}) created as sales order — invoice deferred until invoiced in Magento",
                req.OrderId, req.CustomerType);

            _logger.LogInformation("Uniconta order {OrderId} submitted successfully", req.OrderId);
            _orderLog.LogSubmitted(req.OrderId, req.CustomerType, req.Email, $"{account} ({debtorStatus})");
            return new OrderResult { Success = true, Message = $"Order submitted to debtor {account} ({debtorStatus})" };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Order processing failed for order {OrderId}", req.OrderId);
            _orderLog.LogFailed(req.OrderId, req.CustomerType, req.Email, ex.Message, ex.ToString());
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

    // Next debtor account = highest existing account number + 1, mirroring the store's
    // 6-digit numbering. Uniconta requires an explicit key (empty -> KeyIsEmpty) and does
    // NOT auto-number on insert. The EAN/CVR itself lives in _EAN/_VatNumber, NOT the key.
    // Guard: only real account numbers count (≤ 9 digits) so a stray EAN-shaped account
    // (13 digits) can never hijack the max and blow the numbering up.
    private static string NextAccountNumber(Debtor[] debtors)
    {
        long max = 0;
        foreach (var d in debtors)
            if ((d._Account?.Length ?? 0) <= 9 && long.TryParse(d._Account, out var n) && n > max)
                max = n;
        return (max + 1).ToString();
    }

    private static DebtorClient BuildDebtor(OrderRequest req, string account)
    {
        var debtor = new DebtorClient
        {
            // Uniconta requires the debtor key (_Account); without it Insert fails with
            // KeyIsEmpty. This is the account the order header will also reference.
            _Account       = account,
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
            // Uniconta debtor group MUST be an existing group in the company:
            // EAN / CVR / PRIV. The previous "ERHVERV" does not exist there, so
            // creating a new company debtor failed with FieldHasInvalidValue.
            _Group         = req.CustomerType switch
            {
                "ean" => "EAN",
                "cvr" => "CVR",
                _     => "PRIV"
            }
        };

        // CVR customers: legal/VAT number on the debtor.
        // EAN customers: GLN + electronic (OIOUBL/XML) invoicing, like the old app.
        if (req.CustomerType == "cvr" && !string.IsNullOrEmpty(req.Cvr))
        {
            debtor._VatNumber = req.Cvr;
        }
        else if (req.CustomerType == "ean" && !string.IsNullOrEmpty(req.Ean))
        {
            debtor._EAN = req.Ean;
            debtor._InvoiceInXML = true;
        }

        return debtor;
    }

    // Full field dump of an attempted debtor — written to the error log so a
    // FieldHasInvalidValue failure shows exactly which value Uniconta rejected.
    private static string DescribeDebtor(DebtorClient d, OrderRequest req) =>
        $"DEBTOR create attempt:\n" +
        $"  Name={d._Name}\n  Group={d._Group}\n  Vat={d._Vat}\n  Payment={d._Payment}\n" +
        $"  Country={d._Country}\n  VatNumber={d._VatNumber}\n  EAN={d._EAN}\n  InvoiceInXML={d._InvoiceInXML}\n" +
        $"  Address1={d._Address1}\n  ZipCode={d._ZipCode}\n  City={d._City}\n" +
        $"  ContactEmail={d._ContactEmail}\n  MobilPhone={d._MobilPhone}\n" +
        $"  (request) CustomerType={req.CustomerType} Cvr={req.Cvr} Ean={req.Ean} CompanyName={req.CompanyName}";

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
            // Home/company address. On a "Ship to Business" order the parcel goes to a
            // company, so the company is the recipient and the person named in the
            // checkout drops to the attention line. Without a company both lines would
            // repeat the same name.
            var company = req.DeliveryCompany?.Trim();

            order._DeliveryName     = !string.IsNullOrEmpty(company) ? company : req.DeliveryName;
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
