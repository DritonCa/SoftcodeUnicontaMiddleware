namespace SoftcodeUnicontaMiddleware.Models.Dtos
{
    public class ProductDto
    {
        // Identity
        public string Sku { get; set; }
        public string Name { get; set; }
        public string Group { get; set; }

        // Pricing
        public double SalesPrice { get; set; }
        public double CostPrice { get; set; }

        // Stock
        public double StockOnHand { get; set; }
        public double StockReserved { get; set; }
        public double StockAvailable { get; set; }

        // Inventory
        public bool IsStockItem { get; set; }
        public string Unit { get; set; }
        public bool Blocked { get; set; }

        // ✅ Dynamic (FLAT, SAFE)
        public Dictionary<string, object> Extensions { get; set; }
    }
}
