using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SoftcodeUnicontaMiddleware.Services;
using System.Security.Claims;

namespace SoftcodeUnicontaMiddleware.Controllers;

/// <summary>
/// Browser interface for the Uniconta order log: first-run setup (create the admin
/// login, stored PBKDF2-hashed), cookie login, and a searchable, auto-refreshing view
/// of received orders and how they were saved. Cookie scheme "AdminCookie" — separate
/// from the JWT the API clients use.
/// </summary>
[ApiController]
public class AdminController : ControllerBase
{
    public const string CookieScheme = "AdminCookie";

    private readonly IAdminUserService _users;
    private readonly OrderLogReader _logReader;

    public AdminController(IAdminUserService users, OrderLogReader logReader)
    {
        _users     = users;
        _logReader = logReader;
    }

    public class Credentials
    {
        public string Username { get; set; } = "";
        public string Password { get; set; } = "";
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
    public IActionResult Logs([FromQuery] string? search, [FromQuery] int limit = 500)
    {
        var entries = _logReader.Read(search, Math.Clamp(limit, 1, 2000));
        return Ok(entries);
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
<title>Uniconta Ordre-log</title>
<style>
  :root { --bg:#0f172a; --card:#1e293b; --line:#334155; --txt:#e2e8f0; --muted:#94a3b8; --accent:#00bcd4; }
  * { box-sizing:border-box; }
  body { margin:0; font-family:system-ui,Segoe UI,Arial,sans-serif; background:var(--bg); color:var(--txt); }
  header { display:flex; align-items:center; justify-content:space-between; padding:14px 20px; border-bottom:4px solid var(--accent); background:var(--card); }
  header h1 { font-size:16px; margin:0; letter-spacing:.5px; }
  header .who { font-size:13px; color:var(--muted); }
  button { cursor:pointer; border:0; border-radius:6px; padding:9px 14px; font-size:14px; background:var(--accent); color:#04222a; font-weight:600; }
  button.ghost { background:transparent; color:var(--muted); border:1px solid var(--line); font-weight:500; }
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
  th { color:var(--muted); font-size:11px; text-transform:uppercase; letter-spacing:.4px; position:sticky; top:0; background:var(--card); }
  td.details { font-family:ui-monospace,Menlo,Consolas,monospace; color:#cbd5e1; word-break:break-all; }
  .tag { display:inline-block; padding:2px 8px; border-radius:20px; font-size:11px; font-weight:700; letter-spacing:.3px; }
  .RECEIVED { background:#0e7490; color:#cffafe; }
  .SUBMITTED { background:#166534; color:#dcfce7; }
  .FAILED { background:#991b1b; color:#fee2e2; }
  .LINE_WARN { background:#9a3412; color:#ffedd5; }
  .empty { text-align:center; color:var(--muted); padding:40px; }
  [hidden] { display:none !important; }
</style>
</head>
<body>
<header>
  <h1>UNICONTA · ORDRE-LOG</h1>
  <div id="hdrRight" hidden>
    <span class="who" id="whoami"></span>
    <button class="ghost" id="logoutBtn" style="margin-left:12px;">Log ud</button>
  </div>
</header>

<!-- SETUP -->
<div id="setupView" class="card" hidden>
  <h2>Første gang – opret adgang</h2>
  <p>Det ser ud til at være første besøg. Opret et brugernavn og en adgangskode for at beskytte ordre-loggen. Adgangskoden gemmes krypteret (hashet) i databasen.</p>
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
  <p>Indtast dit brugernavn og adgangskode for at se ordre-loggen.</p>
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
    <input id="search" placeholder="Søg på ordrenummer, e-mail, status …">
    <button class="ghost" id="refreshBtn">Opdater nu</button>
    <span class="status" id="dashStatus"></span>
  </div>
  <table>
    <thead><tr><th style="width:170px;">Tidspunkt (UTC)</th><th style="width:110px;">Status</th><th style="width:90px;">Ordre</th><th>Detaljer</th></tr></thead>
    <tbody id="rows"></tbody>
  </table>
  <div class="empty" id="empty" hidden>Ingen log-linjer endnu.</div>
</div>

<script>
const $ = s => document.querySelector(s);
const api = (p, opt) => fetch('/admin/api/' + p, Object.assign({ headers:{'Content-Type':'application/json'} }, opt));
let timer = null;

function show(view) {
  for (const v of ['setupView','loginView','dashView']) $('#'+v).hidden = (v !== view);
  $('#hdrRight').hidden = (view !== 'dashView');
  if (timer) { clearInterval(timer); timer = null; }
  if (view === 'dashView') { loadLogs(); timer = setInterval(loadLogs, 4000); }
}

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
$('#refreshBtn').onclick = () => loadLogs();
let searchDebounce;
$('#search').addEventListener('input', () => { clearTimeout(searchDebounce); searchDebounce = setTimeout(loadLogs, 300); });

function esc(s){ return (s||'').replace(/[&<>"]/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;'}[c])); }

async function loadLogs() {
  const q = encodeURIComponent($('#search').value.trim());
  let r;
  try { r = await api('logs?search=' + q); } catch { return; }
  if (r.status === 401) { show('loginView'); return; }
  if (!r.ok) return;
  const data = await r.json();
  const rows = $('#rows');
  rows.innerHTML = data.map(e => {
    const lvl = (e.level || '').trim();
    return '<tr><td>'+esc(e.timestamp)+'</td>'+
           '<td><span class="tag '+esc(lvl)+'">'+esc(lvl)+'</span></td>'+
           '<td>'+(e.orderId ?? '')+'</td>'+
           '<td class="details">'+esc(e.details)+'</td></tr>';
  }).join('');
  $('#empty').hidden = data.length > 0;
  const t = new Date();
  $('#dashStatus').textContent = 'Opdateret ' + t.toLocaleTimeString('da-DK') + ' · ' + data.length + ' linjer';
}

boot();
</script>
</body>
</html>
""";
}
