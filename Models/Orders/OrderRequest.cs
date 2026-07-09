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

    // GLS parcel shop (only when DeliveryType == 2)
    public string? GlsShopName { get; set; }
    public string? GlsShopAddress { get; set; }
    public string? GlsShopPostcode { get; set; }
    public string? GlsShopCity { get; set; }

    public double TotalPrice { get; set; }
    public double ShippingAmount { get; set; }

    /// <summary>SKU of the Uniconta shipping product (default: "860")</summary>
    public string ShippingProductSku { get; set; } = "860";

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
