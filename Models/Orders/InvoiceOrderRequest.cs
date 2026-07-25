namespace SoftcodeUnicontaMiddleware.Models.Orders;

public class InvoiceOrderRequest
{
    /// <summary>Optional "Our reference" stamped on the Uniconta order before invoicing (e.g. the admin user who captured the payment).</summary>
    public string? OurRef { get; set; }
}
