namespace SoftcodeUnicontaMiddleware.Models.Common
{
    /// <summary>
    /// Generic paged response aligned with Uniconta offset/limit paging.
    /// </summary>
    public class PagedResponse<T>
    {
        /// <summary>
        /// Returned items for the current page.
        /// </summary>
        public IEnumerable<T> Items { get; set; } = Enumerable.Empty<T>();

        /// <summary>
        /// Zero-based offset used in the query.
        /// </summary>
        public int Offset { get; set; }

        /// <summary>
        /// Max number of items requested.
        /// </summary>
        public int Limit { get; set; }

        /// <summary>
        /// Total items available in Uniconta.
        /// </summary>
        public int Total { get; set; }

        /// <summary>
        /// Indicates whether more data can be fetched.
        /// </summary>
        public bool HasMore => Offset + Limit < Total;
    }
}
