using System.Text.Json;

namespace SoftcodeUnicontaMiddleware.Models.Dtos
{
    /// <summary>
    /// DEBUG / INSPECTION DTO.
    /// Not part of the public Magento API contract.
    /// </summary>
    public class DynamicEntityDto
    {
        /// <summary>
        /// Primary identifier (Item, Account, etc.)
        /// </summary>
        public string Key { get; set; }

        /// <summary>
        /// Human readable label.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Raw Uniconta fields for inspection.
        /// JSON-safe, no SDK leakage.
        /// </summary>
        public Dictionary<string, JsonElement> Fields { get; set; } = new();
    }
}
