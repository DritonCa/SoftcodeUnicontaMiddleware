# Security Policy

## Reporting a vulnerability

Please report suspected vulnerabilities privately rather than opening a public
issue. Email **cazdrit@gmail.com** with a description, reproduction steps and the
affected version. You can expect an acknowledgement within a few working days.

## Supported versions

This is a reference / portfolio project; only the `main` branch receives fixes.

---

## Threat model

The middleware is the only component allowed to talk to Uniconta ERP. Its job is to
keep two kinds of secret away from clients and out of transport:

1. **Uniconta credentials** (username, password, API key) — full ERP access.
2. **Client credentials** (`X-Client-Id` / `X-Client-Secret`) — the shop's key to
   the middleware.

### How each secret is handled

| Secret | At rest | In transit | Notes |
| --- | --- | --- | --- |
| Uniconta password / API key | Encrypted with ASP.NET **Data Protection** in an in-memory store, keyed by `username:companyId`, with a TTL | **Never** returned to a client and **never** placed in a JWT | Supplied once at `POST /api/auth/login`; used server-side thereafter |
| Client secret | Keyed **HMAC-SHA256** with a server-side pepper, compared in **constant time** (`CryptographicOperations.FixedTimeEquals`) | Sent by the client only over TLS on the login call | A stolen database alone cannot recover or brute-force it without the pepper |
| Refresh token | Stored only as a **SHA-256 hash**; the raw value is never persisted | Returned once at login, single-use (rotated on refresh) | High-entropy (64 random bytes), so a fast hash is the correct primitive |
| Access token (JWT) | Not stored | `Authorization: Bearer` header | Carries **identity only** (`username`, `companyId`) — no secrets |

### Design decisions worth calling out

- **The JWT is an identity, not a secret carrier.** A JWT payload is only
  base64-encoded, so anything placed in a claim is readable by anyone who intercepts
  the token. The Uniconta API key and password are therefore held server-side in
  `IUnicontaCredentialStore` and looked up by the JWT's identity claims, never
  embedded in the token itself. This is enforced by `JwtTokenServiceTests`.
- **Refresh tokens are single-use and hashed at rest.** `POST /api/auth/refresh`
  revokes the presented token before issuing a new one, and the store only ever
  holds a SHA-256 of each token, so a dump of the store yields nothing replayable.
- **The login endpoint is rate limited.** `POST /api/auth/login` is protected by a
  strict sliding-window limiter (5 requests / minute, partitioned by client id + IP)
  to blunt credential-stuffing and brute-force attempts.
- **Errors never leak internals.** A global exception middleware logs details
  server-side and returns a safe, generic message to the caller.

---

## Configuration & secret management

No real secret is committed to this repository. Supply secrets through the
git-ignored `appsettings.Development.json` / `appsettings.Production.json`,
environment variables, or `dotnet user-secrets`:

| Key | Purpose |
| --- | --- |
| `Jwt:Key` | Signing key for access tokens — use a long, random value |
| `Auth:SecretPepper` | Server-side pepper mixed into every client-secret hash |
| `Uniconta:ApiKey` / `Username` / `Password` | Uniconta ERP credentials |
| `ConnectionStrings:AppDb` | Overrides the default local SQLite file |

If any of these values is ever exposed, rotate it immediately: change the value in
configuration and restart the service. Rotating `Auth:SecretPepper` invalidates all
stored client-secret hashes, so re-seed or re-hash clients afterwards.

---

## Production hardening notes

This project favours a self-contained, easy-to-run reference implementation. Before
running it in production:

- **Terminate TLS in front of the service** (reverse proxy) and never accept the
  login call over plain HTTP.
- **Back the stores with a shared, persistent store** if you run more than one
  instance. `IUnicontaCredentialStore` and `IRefreshTokenStore` are `IMemoryCache`
  implementations today, so tokens/credentials live per-process and are lost on
  restart. A distributed implementation should keep the same hash-/encrypt-at-rest
  guarantees.
- **Persist Data Protection keys** to a durable, access-controlled location (the app
  already writes them to `dataprotection-keys/`, which is git-ignored) so encrypted
  credentials survive restarts and are shareable across instances.
- **Scope and rotate** the Uniconta API credentials to the minimum access the
  integration needs.
