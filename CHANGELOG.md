# Changelog

All notable changes to this project are documented here. The format is based on
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

### Security

- **The Uniconta API key is no longer placed in the JWT.** Access tokens now carry
  identity only (`username` + `companyId`); the API key and password are held
  server-side in `IUnicontaCredentialStore` and never travel inside the token, which
  a client can trivially base64-decode.
- **Login now actually caches the Uniconta credentials.** Previously the credential
  store was never populated at login, so every subsequent authenticated request
  failed with "credentials expired". Credentials are now stored (encrypted, with a
  TTL) on login and slid forward on refresh.
- **Refresh tokens are hashed at rest.** `MemoryRefreshTokenStore` now persists only
  a SHA-256 of each token; the raw bearer secret is never retained.
- **The login endpoint is rate limited.** The `auth` sliding-window policy
  (5 requests/minute) is now applied to `POST /api/auth/login` via
  `[EnableRateLimiting("auth")]`; previously the policy existed but was attached to
  no endpoint.

### Fixed

- **Login now returns the refresh token** it generates, so clients can actually use
  `POST /api/auth/refresh` (the token was created and stored but never sent back).
- **`MemoryUnicontaCredentialStore.Get` is now idempotent.** It previously decrypted
  in place, corrupting the cached entry so the *second* read threw a
  `CryptographicException`. Since the client factory reads on every authenticated
  request, this broke all but the first call. `Store`/`Get` now operate on copies.

### Added

- xUnit regression tests for the token and credential subsystems:
  `JwtTokenServiceTests`, `MemoryRefreshTokenStoreTests`,
  `MemoryUnicontaCredentialStoreTests`.
- `SECURITY.md` documenting the threat model, secret handling and production
  hardening notes.

### Changed / hygiene

- Removed committed build artefacts from version control: a seeded SQLite database
  (`softcode_api.db`) and a stray `*.Backup.tmp` file. Both are covered by
  `.gitignore` going forward.
