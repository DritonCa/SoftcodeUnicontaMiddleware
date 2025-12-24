# Softcode Uniconta Middleware

ASP.NET Core middleware that exposes **Uniconta ERP data** through a **CMS-friendly REST API**.

It is designed as a **generic ERP → CMS integration layer** with:

- Safe data contracts
- Batch & range loading
- Optional dynamic (custom) fields
- Rate limiting
- Caching
- SSH-based GitHub workflow

---

## 🎯 Purpose

Uniconta’s SDK is powerful but **not suitable to expose directly** to CMS systems.

This middleware:
- Translates Uniconta data into **stable DTOs**
- Prevents SDK object graph leaks
- Supports **nightly syncs**, previews, and incremental loading
- Protects the API against abuse

Typical consumers:
- CMS systems
- Headless storefronts
- Power BI

---

## 🧱 Architecture Overview

CMS / Client
↓
REST API (ASP.NET Core)
↓
SoftcodeUnicontaMiddleware
↓
Uniconta SDK

Key principles:
- **One Uniconta session per API instance**
- **Explicit DTOs** (no raw SDK objects)
- **Optional dynamic fields**, flattened and safe
- **Batch-first design**

---

## 🔐 Authentication Model

Authentication is **session-based**:

1. Client logs in using Uniconta credentials
2. Session is stored in memory
3. All subsequent calls reuse the session

There is **no password or token exchange** beyond the initial login.

### Login Endpoint

POST /api/auth/login


{
  "userName": "api@company.dk",
  "password": "password",
  "apiKey": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"
}

POST /api/auth/login


All other endpoints require a successful login first.

### 📦 Products API
List / Range / Batch
GET /api/uniconta/products


Query parameters:

Name	Type	Default	Description
offset	int	0	Start index
limit	int	100	Number of items
includeDynamic	bool	false	Include dynamic fields

Example:

GET /api/uniconta/products?offset=0&limit=50


This endpoint is used for:

Nightly batch sync

CMS imports

Range loading (e.g. “get 10 products”)

Single Product
GET /api/uniconta/products/{sku}


Example:

GET /api/uniconta/products/ITEM-1001


Optional:

?includeDynamic=true

### 👤 Debtors (Customers) API
List / Batch
GET /api/uniconta/debtors


Query parameters:

Name	Type	Default
offset	int	0
limit	int	100
includeDynamic	bool	false
Single Debtor
GET /api/uniconta/debtors/{account}


Example:

GET /api/uniconta/debtors/10000

🧬 Dynamic Fields (includeDynamic=true)

When includeDynamic=true is used:

Additional ERP fields are included under extensions

Fields are flattened

Only safe values are exposed (strings, numbers, enums, dates)

❌ No nested SDK objects
❌ No circular references
❌ No serializer depth issues

Example:

"extensions": {
  "_Account": "10000",
  "_Blocked": false,
  "_Group": "DK",
  "_CreditMax": 50000
}


This makes the API future-proof against ERP schema changes.

🧪 Debug Endpoints (Internal Use)

These endpoints expose raw SDK data via reflection and are intended for development/debugging only.

GET /api/debug/uniconta/debtors
GET /api/debug/uniconta/products/prod
GET /api/debug/uniconta/products/inv


### ⚠️ Do not expose these publicly.

### 🚦 Rate Limiting

A global rate limiter protects all endpoints:

30 requests per 10 seconds

Per IP address

Sliding window

No request queue

Excess requests return:

HTTP 429 Too Many Requests

### ⚡ Caching Strategy

In-memory caching is used to:

Reduce load on Uniconta

Speed up batch and range requests

Cache keys are:

Tenant-scoped (CompanyId)

Versioned using dataset fingerprints

Offset + limit aware

No timestamps are relied on.

### 🛡 Error Handling
Scenario	Response
Not logged in	401 Unauthorized
Entity not found	404 Not Found
Rate limit exceeded	429 Too Many Requests
Internal error	500

Controllers never crash on missing session state.

### 🧰 Tech Stack

.NET 8

ASP.NET Core

Uniconta SDK

IMemoryCache

Built-in ASP.NET Rate Limiter

SSH-based GitHub workflow


### 🚀 Current Status

✔ Products: batch + single
✔ Debtors: batch + single
✔ Dynamic fields (safe)
✔ Rate limiting
✔ Caching
✔ GitHub repository initialized
