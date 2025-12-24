Softcode Uniconta Middleware

Softcode Uniconta Middleware is an ASP.NET Core (.NET 8) API that exposes Uniconta ERP data through a secure, CMS-friendly, headless REST API.

It is designed as a generic ERP → CMS / platform integration layer with:

Explicit, stable DTOs

Secure authentication (Client Credentials + JWT)

Refresh tokens

Rate limiting

Caching

Stateless, scalable architecture

🎯 Purpose

Uniconta’s SDK is powerful, but not suitable to expose directly to external systems.

This middleware:

Translates Uniconta SDK data into safe, stable DTOs

Prevents SDK object graph leaks

Supports batch sync, range loading, and incremental access

Protects Uniconta against abuse and credential leakage

Works with any CMS, headless frontend, or BI system

Typical consumers:

CMS systems

Headless storefronts

Power BI

Custom backend services

🧱 Architecture Overview
Client / CMS / BI
        ↓
Client-ID + Client-Secret
        ↓
Auth API (JWT + Refresh Token)
        ↓
Softcode Uniconta Middleware
        ↓
Uniconta SDK

Key principles

Stateless API (no server-side sessions)

JWT-based authentication

Short-lived access tokens

Encrypted credential cache

Explicit DTOs only

Batch-first design

🔐 Authentication Model

Authentication is token-based, not session-based.

1️⃣ Client Authentication (API access)

Each consumer gets its own credentials:

X-Client-Id

X-Client-Secret

These are:

Stored hashed in the database

Used only to access /api/auth/login

Never reused after login

2️⃣ Login (Uniconta authentication)

Endpoint

POST /api/auth/login


Headers

X-Client-Id: your-client-id
X-Client-Secret: your-client-secret
Content-Type: application/json


Body

{
  "userName": "api@company.dk",
  "password": "UNICONTA_PASSWORD",
  "apiKey": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"
}


Response

{
  "access_token": "eyJhbGciOiJIUzI1NiIs...",
  "refresh_token": "long_random_string",
  "token_type": "Bearer"
}

Important notes

Uniconta password is used once

Password is never stored in JWT

Password is cached encrypted with TTL

Client secrets are never returned

3️⃣ Accessing protected endpoints

All API endpoints require a valid Bearer token:

Authorization: Bearer <access_token>

4️⃣ Refresh token flow

When the access token expires:

POST /api/auth/refresh


Body

"refresh_token_here"


A new access token and rotated refresh token are returned.

✔ Refresh tokens are server-stored
✔ Rotated on use
✔ Revocable
✔ Ready for Redis / DB persistence

📦 Products API
List / Range / Batch
GET /api/uniconta/products


Query parameters

Name	Type	Default	Description
offset	int	0	Start index
limit	int	100	Number of items
includeDynamic	bool	false	Include dynamic ERP fields

Example

GET /api/uniconta/products?offset=0&limit=50

Single Product
GET /api/uniconta/products/{sku}


Optional:

?includeDynamic=true

👤 Debtors (Customers) API
List / Batch
GET /api/uniconta/debtors


Query parameters

Name	Type	Default
offset	int	0
limit	int	100
includeDynamic	bool	false
Single Debtor
GET /api/uniconta/debtors/{account}

🧬 Dynamic Fields (includeDynamic=true)

When enabled, additional ERP fields are returned under extensions.

Example:

"extensions": {
  "_Account": "10000",
  "_Blocked": false,
  "_Group": "DK",
  "_CreditMax": 50000
}

Guarantees

Flat structure only

Safe primitive values

No nested SDK objects

No circular references

Serializer-safe

🧪 Debug Endpoints (Internal Use Only)
GET /api/debug/uniconta/debtors
GET /api/debug/uniconta/products/prod
GET /api/debug/uniconta/products/inv


⚠️ Do not expose publicly

🚦 Rate Limiting

Global rate limiting is enabled:

30 requests per 10 seconds

Per IP address

Sliding window

No queue

Excess requests return:

HTTP 429 Too Many Requests

⚡ Caching Strategy

In-memory caching is used to:

Reduce Uniconta load

Speed up batch and range queries

Cache expensive lookups

Cache keys are:

Tenant-scoped (CompanyId)

Versioned using dataset fingerprints

Offset + limit aware

No timestamps are relied on.

🛡 Error Handling
Scenario	Response
Invalid client credentials	401 Unauthorized
Invalid or expired token	401 Unauthorized
Entity not found	404 Not Found
Rate limit exceeded	429 Too Many Requests
Internal error	500 Internal Server Error

Controllers never rely on session state.

🧰 Tech Stack

.NET 8

ASP.NET Core

Uniconta SDK

Entity Framework Core (SQLite)

IMemoryCache

ASP.NET Core Data Protection

JWT + Refresh Tokens

Built-in ASP.NET Rate Limiter


🚀 Current Status

✔ Client-ID + Client-Secret authentication
✔ JWT access tokens
✔ Refresh tokens (rotating)
✔ Encrypted Uniconta credential cache
✔ Products: batch + single
✔ Debtors: batch + single
✔ Dynamic fields (safe)
✔ Rate limiting
✔ Caching
✔ Stateless architecture

🔜 Planned / Easy Extensions

Redis-backed credential & token stores

Orders & OrderLines

Admin endpoints for client management

Token revocation & auditing

Webhooks / event sync

📌 Notes

This project is designed as infrastructure, not a CMS plugin.

Any CMS, frontend, or integration platform can consume it safely.
