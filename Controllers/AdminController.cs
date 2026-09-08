using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using SoftcodeUnicontaMiddleware.Data;
using SoftcodeUnicontaMiddleware.Data.Entities;
using SoftcodeUnicontaMiddleware.Services;
using System.Security.Claims;
using System.Security.Cryptography;

namespace SoftcodeUnicontaMiddleware.Controllers;

/// <summary>
/// Browser interface for the Uniconta middleware: first-run setup (create the admin
/// login, stored PBKDF2-hashed), cookie login, a searchable, auto-refreshing order log
/// with a full error-log panel, and a Companies view listing tenants/clients and their
/// API secrets. Cookie scheme "AdminCookie" — separate from the JWT the API clients use.
/// </summary>
[ApiController]
public class AdminController : ControllerBase
{
    public const string CookieScheme = "AdminCookie";

    private readonly IAdminUserService _users;
    private readonly OrderLogReader _logReader;
    private readonly AppDbContext _db;
    private readonly SecretHasher _hasher;
    private readonly IDataProtector _secrets;

    public AdminController(
        IAdminUserService users,
        OrderLogReader logReader,
        AppDbContext db,
        SecretHasher hasher,
        IDataProtectionProvider dp)
    {
        _users     = users;
        _logReader = logReader;
        _db        = db;
        _hasher    = hasher;
        _secrets   = dp.CreateProtector("SoftcodeUnicontaMiddleware.ClientSecrets");
    }

    public class Credentials
    {
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
    }

    public class SecretReq
    {
        public string ClientId { get; set; } = "";
        public string Secret   { get; set; } = "";
    }

    public class NewCompanyReq
    {
        public string Name     { get; set; } = "";
        public string ClientId { get; set; } = "";
    }

    [HttpGet("/admin")]
    [AllowAnonymous]
    public ContentResult Index() => Content(Html, "text/html; charset=utf-8");

    [HttpGet("/admin/api/status")]
    [AllowAnonymous]
    public async Task<IActionResult> Status()
    {
        var setupRequired = !await _users.AnyExistsAsync();
        var auth          = await HttpContext.AuthenticateAsync(CookieScheme);

        return Ok(new
        {
            setupRequired,
            authenticated = auth.Succeeded,
            username      = auth.Principal?.Identity?.Name
        });
    }

    [HttpPost("/admin/api/setup")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Setup([FromBody] Credentials c)
    {
        if (await _users.AnyExistsAsync())
            return Conflict(new { message = "Opsætning er allerede gennemført." });

        var user = await _users.CreateFirstAsync(c.Username, c.Password);
        if (user == null)
            return BadRequest(new { message = "Brugernavn skal være mindst 3 tegn og adgangskode mindst 8 tegn." });

        await SignInAsync(user.Username);
        return Ok(new { username = user.Username });
    }

    [HttpPost("/admin/api/login")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Login([FromBody] Credentials c)
    {
        var user = await _users.ValidateAsync(c.Username, c.Password);
        if (user == null)
            return Unauthorized(new { message = "Forkert brugernavn eller adgangskode." });

        await SignInAsync(user.Username);
        return Ok(new { username = user.Username });
    }

    [HttpPost("/admin/api/logout")]
    [AllowAnonymous]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieScheme);
        return Ok();
    }

    [HttpGet("/admin/api/logs")]
    [Authorize(AuthenticationSchemes = CookieScheme)]
    public IActionResult Logs([FromQuery] string? search, [FromQuery] int limit = 500, [FromQuery] string? company = null)
    {
        var (legacyId, legacyName) = LegacyCompany();
        var entries = _logReader.Read(search, Math.Clamp(limit, 1, 2000), company, legacyId, legacyName);
        return Ok(entries);
    }

    /// <summary>
    /// Front-page figures: order counts across every company, plus a per-company
    /// breakdown that also lists companies with no traffic yet.
    /// </summary>
    [HttpGet("/admin/api/dashboard")]
    [Authorize(AuthenticationSchemes = CookieScheme)]
    public IActionResult Dashboard()
    {
        var (legacyId, legacyName) = LegacyCompany();
        var stats = _logReader.Stats(null, legacyId, legacyName);

        var registered = _db.Clients
            .Include(c => c.Tenant)
            .AsNoTracking()
            .ToList();

        var byClient = stats.Companies.ToDictionary(c => c.ClientId, StringComparer.OrdinalIgnoreCase);

        var companies = registered
            .Select(c =>
            {
                byClient.TryGetValue(c.ClientId, out var s);
                return new
                {
                    clientId  = c.ClientId,
                    company   = c.Tenant?.Name ?? c.ClientId,
                    isActive  = c.IsActive,
                    received  = s?.Received  ?? 0,
                    submitted = s?.Submitted ?? 0,
                    failed    = s?.Failed    ?? 0,
                    lastEvent = s?.LastEvent
                };
            })
            .OrderByDescending(c => c.received)
            .ThenBy(c => c.company)
            .ToList();

        // Traffic from a client that has since been deleted must still be visible.
        var known   = registered.Select(c => c.ClientId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var orphans = stats.Companies
            .Where(c => !known.Contains(c.ClientId))
            .Select(c => new
            {
                clientId  = c.ClientId,
                company   = c.Company,
                isActive  = false,
                received  = c.Received,
                submitted = c.Submitted,
                failed    = c.Failed,
                lastEvent = c.LastEvent
            });

        return Ok(new
        {
            received     = stats.Received,
            submitted    = stats.Submitted,
            failed       = stats.Failed,
            lineWarnings = stats.LineWarnings,
            received24h  = stats.Received24h,
            failed24h    = stats.Failed24h,
            lastEvent    = stats.LastEvent,
            companies    = companies.Concat(orphans).ToList()
        });
    }

    /// <summary>
    /// The company that owns log entries written before events carried one — the
    /// oldest registered client, i.e. the one that existed back then.
    /// </summary>
    private (string? ClientId, string? Company) LegacyCompany()
    {
        var c = _db.Clients
            .Include(x => x.Tenant)
            .AsNoTracking()
            .OrderBy(x => x.CreatedAt)
            .FirstOrDefault();

        return c == null ? (null, null) : (c.ClientId, c.Tenant?.Name ?? c.ClientId);
    }

    /// <summary>Full multi-line failure detail (exception/stack + rejected fields).</summary>
    [HttpGet("/admin/api/errors")]
    [Authorize(AuthenticationSchemes = CookieScheme)]
    public IActionResult Errors() => Ok(new { text = _logReader.ReadErrorLog() });

    // ---- Companies -------------------------------------------------------------

    [HttpGet("/admin/api/companies")]
    [Authorize(AuthenticationSchemes = CookieScheme)]
    public IActionResult Companies()
    {
        var rows = _db.Clients
            .Include(c => c.Tenant)
            .AsNoTracking()
            .OrderBy(c => c.Tenant!.Name)
            .ToList()
            .Select(c => new
            {
                tenantId     = c.TenantId,
                tenantName   = c.Tenant != null ? c.Tenant.Name : "(ukendt)",
                tenantActive = c.Tenant != null && c.Tenant.IsActive,
                clientId     = c.ClientId,
                isActive     = c.IsActive,
                createdAt    = c.CreatedAt.ToString("yyyy-MM-dd HH:mm"),
                secretStored = !string.IsNullOrEmpty(c.ClientSecretEnc),
                secret       = RevealSecret(c.ClientSecretEnc)
            })
            .ToList();

        return Ok(rows);
    }

    [HttpPost("/admin/api/companies/secret")]
    [Authorize(AuthenticationSchemes = CookieScheme)]
    public async Task<IActionResult> SetSecret([FromBody] SecretReq r)
    {
        var secret = (r.Secret ?? "").Trim();
        if (string.IsNullOrWhiteSpace(r.ClientId) || secret.Length < 16)
            return BadRequest(new { message = "Client-id kræves og hemmeligheden skal være mindst 16 tegn." });

        var client = await _db.Clients.FirstOrDefaultAsync(c => c.ClientId == r.ClientId);
        if (client == null)
            return NotFound(new { message = "Klienten findes ikke." });

        client.ClientSecretHash = _hasher.Hash(secret);
        client.ClientSecretEnc  = _secrets.Protect(secret);
        await _db.SaveChangesAsync();
        return Ok(new { clientId = client.ClientId });
    }

    [HttpPost("/admin/api/companies")]
    [Authorize(AuthenticationSchemes = CookieScheme)]
    public async Task<IActionResult> CreateCompany([FromBody] NewCompanyReq r)
    {
        var name     = (r.Name ?? "").Trim();
        var clientId = (r.ClientId ?? "").Trim();
        if (name.Length < 2 || clientId.Length < 3)
            return BadRequest(new { message = "Navn (min. 2 tegn) og client-id (min. 3 tegn) kræves." });
        if (await _db.Clients.AnyAsync(c => c.ClientId == clientId))
            return Conflict(new { message = "Client-id findes allerede." });

        var secret = GenerateSecret();
        var tenant = new ApiTenant { Id = Guid.NewGuid(), Name = name, IsActive = true, CreatedAt = DateTime.UtcNow };
        var client = new ApiClient
        {
            Id               = Guid.NewGuid(),
            TenantId         = tenant.Id,
            ClientId         = clientId,
            ClientSecretHash = _hasher.Hash(secret),
            ClientSecretEnc  = _secrets.Protect(secret),
            IsActive         = true,
            CreatedAt        = DateTime.UtcNow
        };
        _db.Tenants.Add(tenant);
        _db.Clients.Add(client);
        await _db.SaveChangesAsync();
        return Ok(new { clientId, secret });
    }

    private string? RevealSecret(string? enc)
    {
        if (string.IsNullOrEmpty(enc)) return null;
        try { return _secrets.Unprotect(enc); }
        catch { return null; }
    }

    private static string GenerateSecret()
    {
        var bytes = RandomNumberGenerator.GetBytes(24);
        return Convert.ToBase64String(bytes)
            .Replace("+", "").Replace("/", "").Replace("=", "");
    }

    private Task SignInAsync(string username)
    {
        var identity  = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, username) }, CookieScheme);
        var principal = new ClaimsPrincipal(identity);
        return HttpContext.SignInAsync(CookieScheme, principal, new AuthenticationProperties
        {
            IsPersistent = true,
            ExpiresUtc   = DateTimeOffset.UtcNow.AddHours(8)
        });
    }

    private const string Html = """
<!doctype html>
<html lang="da">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Uniconta Middleware · Admin</title>
<style>
  :root { --bg:#0f172a; --card:#1e293b; --line:#334155; --txt:#e2e8f0; --muted:#94a3b8; --accent:#00bcd4; }
  * { box-sizing:border-box; }
  body { margin:0; font-family:system-ui,Segoe UI,Arial,sans-serif; background:var(--bg); color:var(--txt); }
  header { display:flex; align-items:center; justify-content:space-between; padding:14px 20px; border-bottom:4px solid var(--accent); background:var(--card); }
  header h1 { font-size:16px; margin:0; letter-spacing:.5px; }
  header .who { font-size:13px; color:var(--muted); }
  button { cursor:pointer; border:0; border-radius:6px; padding:9px 14px; font-size:14px; background:var(--accent); color:#04222a; font-weight:600; }
  button.ghost { background:transparent; color:var(--muted); border:1px solid var(--line); font-weight:500; }
  button.navbtn.active { background:var(--accent); color:#04222a; font-weight:600; border-color:var(--accent); }
  .wrap { max-width:1100px; margin:0 auto; padding:24px 20px; }
  .card { background:var(--card); border:1px solid var(--line); border-radius:10px; padding:26px; max-width:400px; margin:8vh auto; }
  .card h2 { margin:0 0 6px; font-size:18px; }
  .card p { margin:0 0 18px; color:var(--muted); font-size:13px; }
  label { display:block; font-size:12px; color:var(--muted); margin:12px 0 5px; text-transform:uppercase; letter-spacing:.4px; }
  input { width:100%; padding:11px 12px; border-radius:6px; border:1px solid var(--line); background:#0b1220; color:var(--txt); font-size:15px; }
  .row-actions { margin-top:20px; display:flex; gap:10px; }
  .msg { margin-top:14px; font-size:13px; min-height:18px; }
  .msg.err { color:#f87171; } .msg.ok { color:#4ade80; }
  .toolbar { display:flex; gap:12px; align-items:center; margin-bottom:14px; flex-wrap:wrap; }
  .toolbar input { max-width:320px; }
  .toolbar .status { font-size:12px; color:var(--muted); margin-left:auto; }
  table { width:100%; border-collapse:collapse; font-size:13px; }
  th,td { text-align:left; padding:9px 10px; border-bottom:1px solid var(--line); vertical-align:top; }
  th { color:var(--muted); font-size:11px; text-transform:uppercase; letter-spacing:.4px; }
  td.details { font-family:ui-monospace,Menlo,Consolas,monospace; color:#cbd5e1; word-break:break-all; }
  .tag { display:inline-block; padding:2px 8px; border-radius:20px; font-size:11px; font-weight:700; letter-spacing:.3px; }
  .RECEIVED { background:#0e7490; color:#cffafe; }
  .SUBMITTED { background:#166534; color:#dcfce7; }
  .FAILED { background:#991b1b; color:#fee2e2; }
  .LINE_WARN { background:#9a3412; color:#ffedd5; }
  .empty { text-align:center; color:var(--muted); padding:40px; }
  .secret { font-family:ui-monospace,Menlo,Consolas,monospace; color:#e2e8f0; }
  .linkbtn { background:transparent; border:0; color:var(--accent); cursor:pointer; padding:0 6px; font-size:12px; font-weight:600; }
  .muted-note { color:var(--muted); font-style:italic; }
  [hidden] { display:none !important; }

  /* dashboard */
  .kpis { display:grid; grid-template-columns:repeat(auto-fit,minmax(180px,1fr)); gap:14px; margin-bottom:26px; }
  .kpi { background:var(--card); border:1px solid var(--line); border-radius:10px; padding:18px 20px; }
  .kpi .n { font-size:30px; font-weight:700; line-height:1.1; }
  .kpi .l { font-size:12px; color:var(--muted); text-transform:uppercase; letter-spacing:.4px; margin-top:6px; }
  .kpi .s { font-size:12px; color:var(--muted); margin-top:8px; }
  .kpi.bad .n { color:#f87171; }
  .kpi.good .n { color:#4ade80; }
  .section-title { font-size:14px; font-weight:600; margin:0 0 12px; }
  tr.clickable { cursor:pointer; }
  tr.clickable:hover td { background:#243147; }
  .detail-cell { background:#0b1220; padding:0 10px 14px; }
  .payload { margin:12px 0 0; background:#020617; border:1px solid var(--line); border-radius:8px; padding:12px;
             font-family:ui-monospace,Menlo,Consolas,monospace; font-size:12px; color:#cbd5e1;
             white-space:pre-wrap; word-break:break-word; max-height:340px; overflow:auto; }
  .payload.err { color:#fca5a5; }
  .payload-label { font-size:11px; color:var(--muted); text-transform:uppercase; letter-spacing:.4px; margin-top:12px; }
  .chev { display:inline-block; width:12px; color:var(--muted); }
  .crumb { color:var(--muted); font-size:13px; margin-bottom:10px; }
  .crumb button { padding:0; }
</style>
</head>
<body>
<header>
  <h1>UNICONTA · MIDDLEWARE ADMIN</h1>
  <div id="hdrRight" hidden>
    <button class="ghost navbtn" id="navDash">Dashboard</button>
    <button class="ghost navbtn" id="navCompanies">Virksomheder</button>
    <span class="who" id="whoami" style="margin-left:12px;"></span>
    <button class="ghost" id="logoutBtn" style="margin-left:12px;">Log ud</button>
  </div>
</header>

<!-- SETUP -->
<div id="setupView" class="card" hidden>
  <h2>Første gang – opret adgang</h2>
  <p>Det ser ud til at være første besøg. Opret et brugernavn og en adgangskode for at beskytte administrationen. Adgangskoden gemmes krypteret (hashet) i databasen.</p>
  <label>Brugernavn</label>
  <input id="suUser" autocomplete="username" autofocus>
  <label>Adgangskode (min. 8 tegn)</label>
  <input id="suPass" type="password" autocomplete="new-password">
  <label>Gentag adgangskode</label>
  <input id="suPass2" type="password" autocomplete="new-password">
  <div class="row-actions"><button id="suBtn">Opret og log ind</button></div>
  <div class="msg err" id="suMsg"></div>
</div>

<!-- LOGIN -->
<div id="loginView" class="card" hidden>
  <h2>Log ind</h2>
  <p>Indtast dit brugernavn og adgangskode for at fortsætte.</p>
  <label>Brugernavn</label>
  <input id="liUser" autocomplete="username" autofocus>
  <label>Adgangskode</label>
  <input id="liPass" type="password" autocomplete="current-password">
  <div class="row-actions"><button id="liBtn">Log ind</button></div>
  <div class="msg err" id="liMsg"></div>
</div>

<!-- DASHBOARD -->
<div id="dashView" class="wrap" hidden>
  <div class="toolbar">
    <strong style="font-size:14px;">Overblik</strong>
    <button class="ghost" id="reloadDash">Opdater</button>
    <span class="status" id="dashStatus"></span>
  </div>
  <div class="kpis" id="kpis"></div>

  <h3 class="section-title">Ordrer pr. virksomhed</h3>
  <table>
    <thead><tr>
      <th>Virksomhed</th>
      <th style="width:160px;">Client-id</th>
      <th style="width:100px;">Modtaget</th>
      <th style="width:100px;">Overført</th>
      <th style="width:90px;">Fejlet</th>
      <th style="width:150px;">Seneste</th>
      <th style="width:110px;"></th>
    </tr></thead>
    <tbody id="dashCompanyRows"></tbody>
  </table>
  <div class="empty" id="dashEmpty" hidden>Ingen virksomheder endnu.</div>
</div>

<!-- ORDER LOG (per company) -->
<div id="logView" class="wrap" hidden>
  <div class="crumb">
    <button class="linkbtn" id="backToDash">← Dashboard</button>
    · Ordre-log for <strong id="logCompanyName"></strong>
  </div>
  <div class="toolbar">
    <input id="search" placeholder="Søg på ordrenummer, e-mail, status …">
    <button class="ghost" id="refreshBtn">Opdater nu</button>
    <span class="status" id="logStatus"></span>
  </div>
  <table>
    <thead><tr>
      <th style="width:24px;"></th>
      <th style="width:160px;">Tidspunkt (UTC)</th>
      <th style="width:110px;">Status</th>
      <th style="width:90px;">Ordre</th>
      <th>Detaljer</th>
    </tr></thead>
    <tbody id="rows"></tbody>
  </table>
  <div class="empty" id="empty" hidden>Ingen log-linjer endnu.</div>
</div>

<!-- COMPANIES -->
<div id="companiesView" class="wrap" hidden>
  <div class="toolbar">
    <strong style="font-size:14px;">Virksomheder &amp; API-nøgler</strong>
    <button class="ghost" id="reloadCompanies">Opdater</button>
    <span class="status" id="compStatus"></span>
  </div>
  <table>
    <thead><tr>
      <th>Virksomhed</th>
      <th style="width:160px;">Client-id (X-Client-Id)</th>
      <th>Hemmelighed (X-Client-Secret)</th>
      <th style="width:60px;">Aktiv</th>
      <th style="width:120px;">Oprettet</th>
      <th style="width:110px;"></th>
    </tr></thead>
    <tbody id="compRows"></tbody>
  </table>
  <div class="empty" id="compEmpty" hidden>Ingen virksomheder endnu.</div>

  <div class="card" style="max-width:none;margin:24px 0 0;">
    <h2 style="font-size:15px;">Opret ny virksomhed</h2>
    <p>Genererer automatisk en stærk hemmelighed. Den vises her og gemmes krypteret, så den kan hentes frem igen.</p>
    <div style="display:flex;gap:12px;flex-wrap:wrap;align-items:flex-end;">
      <div style="flex:1;min-width:180px;"><label>Virksomhedsnavn</label><input id="ncName"></div>
      <div style="flex:1;min-width:180px;"><label>Client-id</label><input id="ncClientId" placeholder="fx firmanavn-prod"></div>
      <button id="ncBtn">Opret</button>
    </div>
    <div class="msg" id="ncMsg"></div>
  </div>
</div>

<script>
const $ = s => document.querySelector(s);
const api = (p, opt) => fetch('/admin/api/' + p, Object.assign({ headers:{'Content-Type':'application/json'} }, opt));
let timer = null;
let currentCompany = null;      // { clientId, name } — whose log is on screen
let openRows = new Set();       // rows the user expanded, kept across auto-refresh

function show(view) {
  for (const v of ['setupView','loginView','dashView','logView','companiesView']) $('#'+v).hidden = (v !== view);
  $('#hdrRight').hidden = ['setupView','loginView'].includes(view);
  $('#navDash').classList.toggle('active', view === 'dashView' || view === 'logView');
  $('#navCompanies').classList.toggle('active', view === 'companiesView');
  if (timer) { clearInterval(timer); timer = null; }
  if (view === 'dashView') { loadDashboard(); timer = setInterval(loadDashboard, 10000); }
  if (view === 'logView')  { loadLogs();      timer = setInterval(loadLogs, 5000); }
  if (view === 'companiesView') { loadCompanies(); }
}

function esc(s){ return (s==null?'':String(s)).replace(/[&<>"]/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;'}[c])); }

async function boot() {
  const s = await (await api('status')).json();
  if (s.setupRequired) return show('setupView');
  if (!s.authenticated) return show('loginView');
  $('#whoami').textContent = s.username || '';
  show('dashView');
}

$('#suBtn').onclick = async () => {
  const u = $('#suUser').value.trim(), p = $('#suPass').value, p2 = $('#suPass2').value;
  $('#suMsg').textContent = '';
  if (p !== p2) { $('#suMsg').textContent = 'Adgangskoderne er ikke ens.'; return; }
  const r = await api('setup', { method:'POST', body: JSON.stringify({ username:u, password:p }) });
  if (r.ok) { const d = await r.json(); $('#whoami').textContent = d.username; show('dashView'); }
  else { $('#suMsg').textContent = (await r.json().catch(()=>({}))).message || 'Kunne ikke oprette.'; }
};

$('#liBtn').onclick = async () => {
  const u = $('#liUser').value.trim(), p = $('#liPass').value;
  $('#liMsg').textContent = '';
  const r = await api('login', { method:'POST', body: JSON.stringify({ username:u, password:p }) });
  if (r.ok) { const d = await r.json(); $('#whoami').textContent = d.username; show('dashView'); }
  else { $('#liMsg').textContent = (await r.json().catch(()=>({}))).message || 'Login mislykkedes.'; }
};
$('#liPass').addEventListener('keydown', e => { if (e.key === 'Enter') $('#liBtn').click(); });

$('#logoutBtn').onclick = async () => { await api('logout', { method:'POST' }); show('loginView'); };
$('#navDash').onclick = () => show('dashView');
$('#navCompanies').onclick = () => show('companiesView');
$('#backToDash').onclick = () => show('dashView');
$('#reloadDash').onclick = () => loadDashboard();
$('#refreshBtn').onclick = () => loadLogs();
$('#reloadCompanies').onclick = () => loadCompanies();
let searchDebounce;
$('#search').addEventListener('input', () => { clearTimeout(searchDebounce); searchDebounce = setTimeout(loadLogs, 300); });

// ---- dashboard -------------------------------------------------------------

function kpi(n, label, sub, cls) {
  return '<div class="kpi '+(cls||'')+'"><div class="n">'+n+'</div><div class="l">'+esc(label)+'</div>'+
         (sub ? '<div class="s">'+esc(sub)+'</div>' : '')+'</div>';
}

async function loadDashboard() {
  let r;
  try { r = await api('dashboard'); } catch { return; }
  if (r.status === 401) { show('loginView'); return; }
  if (!r.ok) return;
  const d = await r.json();

  $('#kpis').innerHTML =
    kpi(d.received,  'Ordrer modtaget',      d.received24h + ' seneste døgn') +
    kpi(d.submitted, 'Overført til Uniconta', null, 'good') +
    kpi(d.failed,    'Fejlede ordrer',        d.failed24h + ' seneste døgn', d.failed ? 'bad' : '') +
    kpi(d.lineWarnings, 'Linje-advarsler',    null) +
    kpi(d.lastEvent ? d.lastEvent.substring(11,16) : '—', 'Seneste hændelse',
        d.lastEvent ? d.lastEvent.substring(0,10) + ' UTC' : 'ingen endnu');

  const rows = (d.companies || []).map(c =>
    '<tr>'+
      '<td>'+esc(c.company)+(c.isActive ? '' : ' <span class="muted-note">(inaktiv)</span>')+'</td>'+
      '<td class="secret">'+esc(c.clientId)+'</td>'+
      '<td>'+c.received+'</td>'+
      '<td>'+c.submitted+'</td>'+
      '<td'+(c.failed ? ' style="color:#f87171;font-weight:600;"' : '')+'>'+c.failed+'</td>'+
      '<td>'+esc(c.lastEvent || '—')+'</td>'+
      '<td><button class="linkbtn" onclick="openLog(\'' + esc(c.clientId) + '\',\'' + esc(c.company).replace(/'/g,"\\'") + '\')">Ordre-log →</button></td>'+
    '</tr>').join('');
  $('#dashCompanyRows').innerHTML = rows;
  $('#dashEmpty').hidden = (d.companies || []).length > 0;
  $('#dashStatus').textContent = 'Opdateret ' + new Date().toLocaleTimeString('da-DK');
}

// ---- order log -------------------------------------------------------------

function openLog(clientId, name) {
  currentCompany = { clientId, name };
  openRows = new Set();
  $('#logCompanyName').textContent = name;
  $('#search').value = '';
  show('logView');
}

function payload(label, text, cls) {
  if (!text) return '';
  return '<div class="payload-label">'+esc(label)+'</div><pre class="payload '+(cls||'')+'">'+esc(text)+'</pre>';
}

async function loadLogs() {
  if (!currentCompany) { show('dashView'); return; }
  const q = encodeURIComponent($('#search').value.trim());
  let r;
  try { r = await api('logs?company=' + encodeURIComponent(currentCompany.clientId) + '&search=' + q); } catch { return; }
  if (r.status === 401) { show('loginView'); return; }
  if (!r.ok) return;
  const data = await r.json();

  $('#rows').innerHTML = data.map((e, i) => {
    const lvl  = (e.level || '').trim();
    const key  = e.timestamp + '|' + (e.orderId ?? '') + '|' + lvl;
    const has  = !!(e.request || e.response || e.detail);
    const open = openRows.has(key);
    const body =
      payload('Request — modtaget fra webshoppen', e.request) +
      payload('Response — sendt retur', e.response) +
      payload('Fejldetaljer', e.detail, 'err');

    return '<tr class="'+(has ? 'clickable' : '')+'"'+(has ? ' onclick="toggleRow(\'' + esc(key) + '\',' + i + ')"' : '')+'>'+
             '<td class="chev" id="chev'+i+'">'+(has ? (open ? '▾' : '▸') : '')+'</td>'+
             '<td>'+esc(e.timestamp)+'</td>'+
             '<td><span class="tag '+esc(lvl)+'">'+esc(lvl)+'</span></td>'+
             '<td>'+(e.orderId ?? '')+'</td>'+
             '<td class="details">'+esc(e.details)+'</td>'+
           '</tr>'+
           '<tr id="det'+i+'"'+(open ? '' : ' hidden')+'><td></td><td colspan="4" class="detail-cell">'+
             (has ? body : '<div class="muted-note" style="padding-top:12px;">Ingen gemte detaljer for denne linje.</div>')+
           '</td></tr>';
  }).join('');

  $('#empty').hidden = data.length > 0;
  $('#logStatus').textContent = 'Opdateret ' + new Date().toLocaleTimeString('da-DK') + ' · ' + data.length + ' linjer';
}

function toggleRow(key, i) {
  const row = $('#det'+i), chev = $('#chev'+i);
  if (!row) return;
  row.hidden = !row.hidden;
  chev.textContent = row.hidden ? '▸' : '▾';
  if (row.hidden) openRows.delete(key); else openRows.add(key);
}

// ---- companies -------------------------------------------------------------

async function loadCompanies() {
  $('#compStatus').textContent = 'Henter …';
  let r;
  try { r = await api('companies'); } catch { return; }
  if (r.status === 401) { show('loginView'); return; }
  if (!r.ok) { $('#compStatus').textContent = 'Kunne ikke hente.'; return; }
  const data = await r.json();
  const rows = $('#compRows');
  rows.innerHTML = data.map((c, i) => {
    let secretCell;
    if (c.secretStored && c.secret) {
      secretCell =
        '<span class="secret" id="sec'+i+'" data-v="'+esc(c.secret)+'">••••••••••••••••</span>'+
        '<button class="linkbtn" onclick="toggleSecret('+i+')" id="secbtn'+i+'">Vis</button>'+
        '<button class="linkbtn" onclick="copySecret('+i+')">Kopiér</button>'+
        '<button class="linkbtn" onclick="setSecret(\''+esc(c.clientId)+'\')">Skift</button>';
    } else {
      secretCell =
        '<span class="muted-note">gemt som hash — kan ikke vises</span>'+
        '<button class="linkbtn" onclick="setSecret(\''+esc(c.clientId)+'\')">Registrér for at vise</button>';
    }
    return '<tr>'+
      '<td>'+esc(c.tenantName)+'</td>'+
      '<td class="secret">'+esc(c.clientId)+'</td>'+
      '<td>'+secretCell+'</td>'+
      '<td>'+(c.isActive ? '✓' : '—')+'</td>'+
      '<td>'+esc(c.createdAt)+'</td>'+
      '<td><button class="linkbtn" onclick="openLog(\'' + esc(c.clientId) + '\',\'' + esc(c.tenantName).replace(/'/g,"\\'") + '\')">Ordre-log →</button></td>'+
    '</tr>';
  }).join('');
  $('#compEmpty').hidden = data.length > 0;
  $('#compStatus').textContent = data.length + ' virksomhed(er)';
}

function toggleSecret(i) {
  const el = $('#sec'+i), btn = $('#secbtn'+i);
  if (btn.textContent === 'Vis') { el.textContent = el.dataset.v; btn.textContent = 'Skjul'; }
  else { el.textContent = '••••••••••••••••'; btn.textContent = 'Vis'; }
}

function copySecret(i) {
  const v = $('#sec'+i).dataset.v || '';
  navigator.clipboard.writeText(v).then(() => { $('#compStatus').textContent = 'Hemmelighed kopieret.'; });
}

async function setSecret(clientId) {
  const secret = prompt('Ny hemmelighed for "' + clientId + '" (min. 16 tegn).\nBemærk: Magento skal opdateres med samme værdi (X-Client-Secret), ellers fejler login.');
  if (secret == null) return;
  const r = await api('companies/secret', { method:'POST', body: JSON.stringify({ clientId, secret: secret.trim() }) });
  if (r.ok) { $('#compStatus').textContent = 'Hemmelighed opdateret for ' + clientId + '.'; loadCompanies(); }
  else { alert((await r.json().catch(()=>({}))).message || 'Kunne ikke opdatere hemmeligheden.'); }
}

$('#ncBtn').onclick = async () => {
  const name = $('#ncName').value.trim(), clientId = $('#ncClientId').value.trim();
  const msg = $('#ncMsg'); msg.className = 'msg'; msg.textContent = '';
  const r = await api('companies', { method:'POST', body: JSON.stringify({ name, clientId }) });
  if (r.ok) {
    const d = await r.json();
    msg.className = 'msg ok';
    msg.textContent = 'Oprettet. Client-id: ' + d.clientId + '  ·  Hemmelighed: ' + d.secret + '  (gem den nu — den vises krypteret bagefter)';
    $('#ncName').value = ''; $('#ncClientId').value = '';
    loadCompanies();
  } else {
    msg.className = 'msg err';
    msg.textContent = (await r.json().catch(()=>({}))).message || 'Kunne ikke oprette virksomheden.';
  }
};

boot();
</script>
</body>
</html>
""";
}
