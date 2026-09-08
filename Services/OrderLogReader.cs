using System.Text.Json;
using System.Text.RegularExpressions;

namespace SoftcodeUnicontaMiddleware.Services
{
    /// <summary>One logged order event. Request/Response are null for entries written before the JSON log existed.</summary>
    public record OrderLogEntry(
        string Timestamp,
        string Level,
        int? OrderId,
        string Details,
        string? ClientId = null,
        string? Company = null,
        string? Request = null,
        string? Response = null,
        string? Detail = null);

    /// <summary>Counts for the dashboard, over every company or a single one.</summary>
    public record OrderLogStats(
        int Received,
        int Submitted,
        int Failed,
        int LineWarnings,
        int Received24h,
        int Failed24h,
        string? LastEvent,
        IReadOnlyList<CompanyStats> Companies);

    public record CompanyStats(string ClientId, string Company, int Received, int Submitted, int Failed, string? LastEvent);

    /// <summary>
    /// Reads the order log for the admin interface, newest first.
    ///
    /// Two sources are merged: logs/uniconta_orders.jsonl (current, carries the full
    /// request/response per entry) and logs/uniconta_orders.log (the original text log).
    /// Text lines are only used for the period before the JSON log started, so an event
    /// written to both is never listed twice.
    /// </summary>
    public class OrderLogReader
    {
        private readonly string _path;
        private readonly string _jsonPath;
        private readonly string _errorPath;

        private static readonly Regex LineRx = new(
            @"^(?<ts>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}) UTC \[(?<lvl>[^\]]+)\]\s*(?<msg>.*)$",
            RegexOptions.Compiled);
        private static readonly Regex OrderIdRx = new(@"orderId=(?<id>\d+)", RegexOptions.Compiled);

        public OrderLogReader(IWebHostEnvironment env)
        {
            var dir    = Path.Combine(env.ContentRootPath, "logs");
            _path      = Path.Combine(dir, "uniconta_orders.log");
            _jsonPath  = Path.Combine(dir, "uniconta_orders.jsonl");
            _errorPath = Path.Combine(dir, "uniconta_errors.log");
        }

        /// <summary>
        /// Raw contents of logs/uniconta_errors.log, tail-trimmed. The admin shows failure
        /// detail per row now; this stays for reading the whole file off the server.
        /// </summary>
        public string ReadErrorLog(int maxChars = 200_000)
        {
            if (!File.Exists(_errorPath))
                return "";
            try
            {
                var content = ReadShared(_errorPath);
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

        /// <summary>Bucket for entries with no company, used only when no client exists to adopt them.</summary>
        public const string NoCompany = "__none__";

        public IReadOnlyList<OrderLogEntry> Read(string? search, int limit = 500, string? clientId = null,
                                                 string? legacyClientId = null, string? legacyCompany = null)
        {
            IEnumerable<OrderLogEntry> query = ReadAll(legacyClientId, legacyCompany);

            if (!string.IsNullOrWhiteSpace(clientId))
            {
                var c = clientId.Trim();
                query = c == NoCompany
                    ? query.Where(e => string.IsNullOrEmpty(e.ClientId))
                    : query.Where(e => string.Equals(e.ClientId, c, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim();
                query = query.Where(e =>
                    (e.OrderId?.ToString().Contains(s) ?? false) ||
                    e.Details.Contains(s, StringComparison.OrdinalIgnoreCase) ||
                    e.Level.Contains(s, StringComparison.OrdinalIgnoreCase) ||
                    (e.Company?.Contains(s, StringComparison.OrdinalIgnoreCase) ?? false));
            }

            var list = query.ToList();
            return list.Count > limit ? list.GetRange(0, limit) : list;
        }

        public OrderLogStats Stats(string? clientId = null, string? legacyClientId = null, string? legacyCompany = null)
        {
            var all = ReadAll(legacyClientId, legacyCompany);
            if (!string.IsNullOrWhiteSpace(clientId))
            {
                var c = clientId.Trim();
                all = (c == NoCompany
                    ? all.Where(e => string.IsNullOrEmpty(e.ClientId))
                    : all.Where(e => string.Equals(e.ClientId, c, StringComparison.OrdinalIgnoreCase))).ToList();
            }

            var cutoff = DateTime.UtcNow.AddHours(-24);

            // Entries logged before per-company attribution existed keep their own bucket
            // rather than disappearing from the company view.
            var companies = all
                .GroupBy(e => string.IsNullOrEmpty(e.ClientId) ? NoCompany : e.ClientId!, StringComparer.OrdinalIgnoreCase)
                .Select(g => new CompanyStats(
                    g.Key,
                    g.Key == NoCompany
                        ? "(uden virksomhed — logget før opdelingen)"
                        : g.Select(e => e.Company).FirstOrDefault(n => !string.IsNullOrEmpty(n)) ?? g.Key,
                    g.Count(e => e.Level == "RECEIVED"),
                    g.Count(e => e.Level == "SUBMITTED"),
                    g.Count(e => e.Level == "FAILED"),
                    g.Select(e => e.Timestamp).FirstOrDefault()))
                .OrderByDescending(c => c.Received)
                .ToList();

            return new OrderLogStats(
                all.Count(e => e.Level == "RECEIVED"),
                all.Count(e => e.Level == "SUBMITTED"),
                all.Count(e => e.Level == "FAILED"),
                all.Count(e => e.Level == "LINE_WARN"),
                all.Count(e => e.Level == "RECEIVED"  && After(e.Timestamp, cutoff)),
                all.Count(e => e.Level == "FAILED"    && After(e.Timestamp, cutoff)),
                all.Select(e => e.Timestamp).FirstOrDefault(),
                companies);
        }

        // ---- sources ---------------------------------------------------------------

        /// <summary>
        /// Every entry, newest first, JSON log first and older text lines behind it.
        ///
        /// Entries written before events carried a company are stamped with
        /// <paramref name="legacyClientId"/> — the company that existed when they were
        /// logged — so the history shows up under it instead of in a nameless bucket.
        /// </summary>
        private List<OrderLogEntry> ReadAll(string? legacyClientId = null, string? legacyCompany = null)
        {
            var json = ReadJson();

            // The text log holds the same events from the moment the JSON log started, so
            // only take the text lines that predate the oldest JSON entry.
            var oldest = json.Count > 0 ? json[^1].Timestamp : null;
            var text   = ReadText().Where(e => oldest == null || string.CompareOrdinal(e.Timestamp, oldest) < 0);

            var all = new List<OrderLogEntry>(json);
            all.AddRange(text);

            if (!string.IsNullOrWhiteSpace(legacyClientId))
            {
                for (var i = 0; i < all.Count; i++)
                    if (string.IsNullOrEmpty(all[i].ClientId))
                        all[i] = all[i] with { ClientId = legacyClientId, Company = legacyCompany ?? legacyClientId };
            }

            return all;
        }

        private List<OrderLogEntry> ReadJson()
        {
            var entries = new List<OrderLogEntry>();
            if (!File.Exists(_jsonPath))
                return entries;

            string content;
            try { content = ReadShared(_jsonPath); }
            catch { return entries; }

            foreach (var raw in content.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0)
                    continue;

                try
                {
                    var el = JsonDocument.Parse(line).RootElement;
                    entries.Add(new OrderLogEntry(
                        Str(el, "ts") ?? "",
                        (Str(el, "level") ?? "").Trim(),
                        el.TryGetProperty("orderId", out var oid) && oid.ValueKind == JsonValueKind.Number ? oid.GetInt32() : null,
                        Str(el, "details") ?? "",
                        Str(el, "clientId"),
                        Str(el, "company"),
                        Str(el, "request"),
                        Str(el, "response"),
                        Str(el, "detail")));
                }
                catch
                {
                    // A truncated last line (crash mid-write) must not hide the rest.
                }
            }

            entries.Reverse();
            return entries;
        }

        private List<OrderLogEntry> ReadText()
        {
            var entries = new List<OrderLogEntry>();
            if (!File.Exists(_path))
                return entries;

            string content;
            try { content = ReadShared(_path); }
            catch { return entries; }

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

            entries.Reverse();
            return entries;
        }

        // Shared read so we never block the append-only writer.
        private static string ReadShared(string path)
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var sr = new StreamReader(fs);
            return sr.ReadToEnd();
        }

        private static string? Str(JsonElement el, string name)
            => el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        private static bool After(string timestamp, DateTime cutoff)
            => DateTime.TryParse(timestamp, System.Globalization.CultureInfo.InvariantCulture,
                                 System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                                 out var ts) && ts >= cutoff;
    }
}
