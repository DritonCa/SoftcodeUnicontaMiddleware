using System.Text.RegularExpressions;

namespace SoftcodeUnicontaMiddleware.Services
{
    public record OrderLogEntry(string Timestamp, string Level, int? OrderId, string Details);

    /// <summary>
    /// Reads and parses logs/uniconta_orders.log (written by <see cref="OrderLogger"/>)
    /// into structured entries for the admin interface, newest first, optionally filtered.
    /// </summary>
    public class OrderLogReader
    {
        private readonly string _path;
        private readonly string _errorPath;

        private static readonly Regex LineRx = new(
            @"^(?<ts>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}) UTC \[(?<lvl>[^\]]+)\]\s*(?<msg>.*)$",
            RegexOptions.Compiled);
        private static readonly Regex OrderIdRx = new(@"orderId=(?<id>\d+)", RegexOptions.Compiled);

        public OrderLogReader(IWebHostEnvironment env)
        {
            _path      = Path.Combine(env.ContentRootPath, "logs", "uniconta_orders.log");
            _errorPath = Path.Combine(env.ContentRootPath, "logs", "uniconta_errors.log");
        }

        /// <summary>
        /// Raw contents of logs/uniconta_errors.log (full exception/stack detail),
        /// tail-trimmed to <paramref name="maxChars"/> for the admin error-log panel.
        /// </summary>
        public string ReadErrorLog(int maxChars = 200_000)
        {
            if (!File.Exists(_errorPath))
                return "";
            try
            {
                using var fs = new FileStream(_errorPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var sr = new StreamReader(fs);
                var content = sr.ReadToEnd();
                if (content.Length > maxChars)
                    content = "…(afkortet — viser de seneste " + maxChars + " tegn)\n\n"
                            + content.Substring(content.Length - maxChars);
                return content;
            }
            catch
            {
                return "";
            }
        }

        public IReadOnlyList<OrderLogEntry> Read(string? search, int limit = 500)
        {
            if (!File.Exists(_path))
                return Array.Empty<OrderLogEntry>();

            string content;
            try
            {
                // Shared read so we never block the append-only writer.
                using var fs = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var sr = new StreamReader(fs);
                content = sr.ReadToEnd();
            }
            catch
            {
                return Array.Empty<OrderLogEntry>();
            }

            var entries = new List<OrderLogEntry>();
            foreach (var raw in content.Split('\n'))
            {
                var line = raw.TrimEnd('\r');
                if (line.Length == 0)
                    continue;

                var m = LineRx.Match(line);
                if (!m.Success)
                    continue;

                var msg = m.Groups["msg"].Value.Trim();
                int? orderId = null;
                var om = OrderIdRx.Match(msg);
                if (om.Success && int.TryParse(om.Groups["id"].Value, out var id))
                    orderId = id;

                entries.Add(new OrderLogEntry(
                    m.Groups["ts"].Value,
                    m.Groups["lvl"].Value.Trim(),
                    orderId,
                    msg));
            }

            IEnumerable<OrderLogEntry> query = entries;
            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim();
                query = query.Where(e =>
                    (e.OrderId?.ToString().Contains(s) ?? false) ||
                    e.Details.Contains(s, StringComparison.OrdinalIgnoreCase) ||
                    e.Level.Contains(s, StringComparison.OrdinalIgnoreCase));
            }

            // Newest first, capped.
            var list = query.ToList();
            list.Reverse();
            if (list.Count > limit)
                list = list.GetRange(0, limit);
            return list;
        }
    }
}
