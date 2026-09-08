namespace SoftcodeUnicontaMiddleware.Models.Orders;

public class OrderRequest
{
    public int OrderId { get; set; }

    /// <summary>"privat" | "cvr" | "ean"</summary>
    public string CustomerType { get; set; } = "privat";

    public string Email { get; set; } = "";
    public string? Ean { get; set; }
    public string? Cvr { get; set; }
    public string CompanyName { get; set; } = "";
    public string ContactName { get; set; } = "";
    public string? Phone { get; set; }
    public string? Comment { get; set; }

    /// <summary>
    /// Uniconta payment code. For B2B: NK8 / NK15 from Magento config.
    /// For privat: mapped payment method title (MobilePay, Dankort, Visa…).
    /// </summary>
    public string PaymentCode { get; set; } = "";

    /// <summary>1 = home address, 2 = GLS parcel shop</summary>
    public int DeliveryType { get; set; } = 1;

    // Home address delivery
    public string DeliveryName { get; set; } = "";
    public string DeliveryAddress { get; set; } = "";
    public string DeliveryPostcode { get; set; } = "";
    public string DeliveryCity { get; set; } = "";

    /// <summary>
    /// Company the parcel goes to, set only when the customer chose "Ship to Business".
    /// When present it becomes the Uniconta delivery name and DeliveryName drops to
    /// the attention line.
    /// </summary>
    public string? DeliveryCompany { get; set; }

    // GLS parcel shop (only when DeliveryType == 2)
    public string? GlsShopName { get; set; }
    public string? GlsShopAddress { get; set; }
    public string? GlsShopPostcode { get; set; }
    public string? GlsShopCity { get; set; }

    public double TotalPrice { get; set; }

    /// <summary>
    /// What the customer paid for delivery, incl. VAT. The Uniconta shipping item
    /// is VAT-free, so this amount is posted as-is — unlike the product lines it is
    /// never reduced by × 0.8.
    /// </summary>
    public double ShippingAmount { get; set; }

    /// <summary>SKU of the Uniconta shipping product (configured in Magento admin)</summary>
    public string ShippingProductSku { get; set; } = "8000";

    /// <summary>
    /// The clerk web orders are booked under in Uniconta ("Vores ref"), configured in
    /// Magento admin. Set on the sales order so it is filled in before invoicing.
    /// </summary>
    public string? OurRef { get; set; }

    /// <summary>
    /// True when Magento sends prices incl. 25% DK VAT → middleware strips × 0.8.
    /// False when Magento sends prices excl. VAT → middleware uses price as-is.
    /// </summary>
    public bool PricesIncludeTax { get; set; } = true;

    public List<OrderItemRequest> Items { get; set; } = new();
}

public class OrderItemRequest
{
    public string Sku { get; set; } = "";
    public string Name { get; set; } = "";
    public double Price { get; set; }
    public int Qty { get; set; } = 1;

    /// <summary>
    /// True when the item is a course or game module.
    /// Used to determine whether to skip the shipping line.
    /// </summary>
    public bool IsCourseOrModule { get; set; }
}
