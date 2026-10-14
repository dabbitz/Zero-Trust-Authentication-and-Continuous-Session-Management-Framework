# Zero Trust Authentication and Continuous Session Management Framework

A Zero Trust security implementation with continuous verification, built as an ASP.NET Core API + .NET MAUI client.

Never trust, always verify: every request is authenticated, contextual signals (IP, location, device, time, activity) are re-evaluated, and step-up OTP is forced whenever risk changes.

## Architecture

```
┌─────────────────────┐         HTTPS / HTTP         ┌─────────────────────┐
│   ZeroTrustClient   │ ◄──────────────────────────► │     ZeroTrustAPI    │
│     (.NET MAUI)     │    JWT + Refresh + OTP       │   (ASP.NET Core)    │
│ Android / Win / iOS │                              │  EF Core + Postgres │
└─────────────────────┘                              └──────────┬──────────┘
                                                                │
                                                                ▼
                                                     ┌─────────────────────┐
                                                     │  PostgreSQL         │
                                                     │  Users, DeviceAccess│
                                                     └─────────────────────┘
                                                     + in-memory SessionStore
```

Two roles in the demo flow:

- **Primary device (PC/Windows):** logs in, displays OTP, polls `status` until verified.
- **Secondary device (Mobile/Android):** logs in with same account, enters OTP in `PhonePage` to approve the primary device.

## Zero Trust Principles Implemented

1. **No implicit trust:** Short-lived JWT access tokens (10 min, `ClockSkew = Zero`), validated on every `[Authorize]` endpoint.
2. **Continuous posture check:** `POST /api/zerotrust/secure` compares current IP / location / device / login hour against the stored `DeviceAccess` baseline. Any mismatch → `otpRequired: true`.
3. **Step-up authentication:** 6-digit OTP (10 s expiry), cross-device verification via `generate` → `verify` → `status` polling.
4. **Least-privilege sessions:** In-memory `SessionContext` tracks `OtpRequired`, `IsAfkExpired`, `IsCriticalPending`, `IpChanged`, `LastActivity`.
5. **AFK / inactivity expiry:**
   - Client `InactivityService`: 45 s no-interaction → `POST /timeout` + force logout to `MainPage`.
   - Server `SessionCleanupService`: background sweep every 1 min, deletes sessions idle ≥ 10 min.
6. **Critical-action re-auth:** `Action = "critical_action"` always forces OTP before proceeding.
7. **Token rotation:** Refresh tokens are GUIDs rotated on every `POST /refresh`. `AuthInterceptor` auto-attaches JWT and retries once on 401.

## Repository Structure

```
Zero Trust App/
├── ZeroTrustAPI/
│   ├── ZeroTrustAPI/
│   │   ├── Controllers/ZeroTrustController.cs    # all auth endpoints
│   │   ├── Models/User.cs
│   │   ├── DeviceAccess.cs
│   │   ├── SessionContext.cs
│   │   ├── DeviceRequest.cs
│   │   ├── GenerateRequest.cs
│   │   ├── Services/SessionStore.cs              # Dictionary<int, SessionContext>
│   │   ├── Services/SessionCleanupService.cs     # BackgroundService
│   │   ├── AppDbContext.cs
│   │   ├── Program.cs
│   └── └── appsettings.json
├── ZeroTrustClient/
│   ├── ZeroTrustClient/
│   │   ├── StartPage, LoginPage, RegisterPage    # entry + credentials
│   │   ├── MainPage (+ OtpTimerDrawable)         # PC: shows OTP, polls status, posture check
│   │   ├── PhonePage                             # Mobile: enters OTP
│   │   ├── AppPage, AccountDetailsPage           # protected areas, critical-action demo
│   │   ├── Services/ApiService.cs                # secure/test/logout/timeout calls
│   │   ├── Services/DelegationHandler.cs         # AuthInterceptor (JWT + refresh retry)
│   │   ├── Services/DeviceInfoHelper.cs          # public IP (ipify), geolocation, device model
│   └── └── Services/InactivityService.cs         # 45s AFK timer
├── README.md                                     # Project documentation
├── ZeroTrustAPI.slnx                             # Solution for the API
└── ZeroTrustClient.slnx                          # Solution for the client app
```

## Tech Stack

- **API:** .NET 10, ASP.NET Core, JWT Bearer, EF Core 10 + Npgsql, BCrypt.Net-Next, Swagger / OpenAPI, `SessionCleanupService`
- **Client:** .NET MAUI 10 (Android 21+, iOS 15+, MacCatalyst 15+, Windows 10.0.17763+), `SecureStorage` for tokens, `IHttpClientFactory` + `AuthInterceptor`, CommunityToolkit.Mvvm
- **DB:** PostgreSQL (`Users`, `DeviceAccesses`)
- **OTP:** `RandomNumberGenerator.GetInt32(100000, 999999)`, 10 s TTL

## API Reference

Base URLs (from `Program.cs` / `MauiProgram.cs`):

- `http://0.0.0.0:5193` (Android emulator via `http://10.0.2.2:5193/`)
- `https://localhost:7092` (Windows / iOS)

| Method | Endpoint | Auth | Description |
|---|---|---|---|
| GET | `/api/zerotrust/test` | No | Health check → `"API is working!"` |
| POST | `/api/zerotrust/register` | No | `{ username, password }` → BCrypt hash, create `User` |
| POST | `/api/zerotrust/login` | No | `{ username, password, isMobile }` → `{ accessToken, refreshToken, userId }`. Reuses refresh token on mobile if present |
| POST | `/api/zerotrust/generate` | Yes | `{ deviceId, realIp, location }` → `{ otp, expiresAt }`, sets `OtpRequired=true` |
| POST | `/api/zerotrust/verify` | Yes* | `{ code }` → on success stores pending tokens, updates `DeviceAccess` baseline, clears flags |
| GET | `/api/zerotrust/status` | Yes | Polling endpoint. Returns tokens once (single-fetch), then resets |
| POST | `/api/zerotrust/secure` | Yes | `{ deviceId, action, location, realIp, localHour }` → `{ otpRequired, message }`. Baseline creation, IP/location/device/±4h hour check |
| GET | `/api/zerotrust/session-status` | Yes | `{ isReady, message }` — gate before entering `AppPage` |
| POST | `/api/zerotrust/refresh` | No | `{ refreshToken }` → rotated `{ accessToken, refreshToken }` |
| POST | `/api/zerotrust/timeout` | No† | Marks session `OtpRequired + IsAfkExpired` |
| POST | `/api/zerotrust/logout` | Yes | Clears session, refresh/pending/OTP state |
| GET | `/api/zerotrust/whoami` | Yes | Debug: echo claims |
| GET | `/api/zerotrust/debug-token` | Yes | Debug: echo claims |
| GET | `/api/zerotrust/how-many-active-sessions` | No | Debug: `SessionStore.Sessions.Count` |

\* `VerifyOtp` calls `GetUserId()` so a valid JWT is required in practice despite missing `[Authorize]` attribute.
† `Timeout` also calls `GetUserId()` and will throw if unauthenticated.

### Risk Checks in `secure` (`ZeroTrustController.cs:367-416`)

```text
isIpMatch       = record.IpAddress  == currentIp
isLocationMatch = record.Location   == currentLocation
isDeviceMatch   = record.DeviceModel == currentDevice
isUsualHour     = |now.TimeOfDay - record.AccessTime.TimeOfDay| <= 4h (midnight-aware)
if any false → otpRequired:true + reason (IP / location / device / unusual hours)
```

## Auth Flows

### 1. Register / Login

```text
Client → POST /register → POST /login → SecureStorage[access_token, refresh_token]
Android → PhonePage, Desktop → MainPage
```

### 2. Cross-device OTP (happy path)

```text
PC MainPage.OnAppearing → POST /secure (posture check)
  └─ mismatch → POST /generate → shows 6-digit OTP + 10s circular timer
Mobile PhonePage → POST /verify { code }
PC polls GET /status every 3s → receives tokens once → navigates to AppPage
```

### 3. Continuous verification

- Every `AppPage` / `AccountDetailsPage` entry and `critical_action` calls `POST /secure`.
- `AuthInterceptor` (`DelegationHandler.cs:9-35`) attaches JWT, refreshes and retries on 401.
- Client-side JWT expiry check + server-side `session-status` gate before navigation.

## Prerequisites

- .NET 10 SDK
- PostgreSQL 14+ running on `localhost:5432`
- Android emulator (for mobile flow) or Windows for desktop flow
- EF Core CLI: `dotnet tool install -g dotnet-ef`

## Setup

### 1. Database

Update `ZeroTrustAPI/ZeroTrustAPI/appsettings.json`:

```json
{
  "Jwt": { "Key": "<32+ char secret>", "ExpireMinutes": 10 },
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=ZeroTrustDB;Username=<user>;Password=<password>"
  }
}
```

Apply migrations:

```powershell
cd ZeroTrustAPI/ZeroTrustAPI
dotnet ef database update
```

### 2. Run API

```powershell
dotnet run
# Swagger: https://localhost:7092/swagger
# Health:  GET http://localhost:5193/api/zerotrust/test
```

Ports are forced in `Program.cs:72-73`.

### 3. Run Client

Open `ZeroTrustClient.slnx` in Visual Studio, set `ZeroTrustClient` as startup, select target:

- `Android Emulator` → uses `http://10.0.2.2:5193/`
- `Windows Machine` → uses `https://localhost:7092/`

Or via CLI (Windows target example):

```powershell
cd ZeroTrustClient/ZeroTrustClient
dotnet build -f net10.0-windows10.0.19041.0
```

> Android HTTP (not HTTPS) is intentional to bypass emulator cert issues (`Program.cs:35`, `MauiProgram.cs:24-26`). Do not use in production.

## Configuration Highlights

| Setting | Location | Default |
|---|---|---|
| JWT lifetime | `ZeroTrustController.cs:583` | 10 min |
| OTP lifetime | `ZeroTrustController.cs:120` | 10 s |
| OTP polling interval | `MainPage.xaml.cs:260` | 3 s |
| Client AFK timeout | `InactivityService.cs:12` | 45 s |
| Server session sweep | `SessionCleanupService.cs:21,33` | ≥10 min idle, every 1 min |
| Unusual-hour window | `ZeroTrustController.cs:385` | ±4 h |

## Security Notes / Limitations

- `SessionStore`, OTPs, refresh tokens, and `_authStatus` are **in-memory dictionaries** — lost on restart, not multi-instance safe. Use Redis / DB for production.
- `AllowAll` CORS, `RequireHttpsMetadata=false`, hardcoded demo JWT key, and OTP returned in API response are demo-only.
- `verify` / `timeout` rely on `GetUserId()` without `[Authorize]` — add explicit `[Authorize]` and model validation.
- Location uses raw `lat,lon` string equality — minor GPS drift triggers OTP. Normalize / geohash in production.
- No rate-limiting, lockout, or OTP attempt counter; no audit log beyond `Debug.WriteLine` perf traces.

## Future Work

- [ ] Persist sessions / refresh tokens (DB + revocation list)
- [ ] Push-based OTP approval instead of 3 s polling (SignalR / WebSocket)
- [ ] TOTP / WebAuthn / FIDO2 as second factor
- [ ] Risk scoring (weighted IP + device + hour + velocity) vs binary match
- [ ] Rate limiting, account lockout, structured audit logging
- [ ] Harden CORS, HTTPS-only, secret management (user-secrets / Key Vault)
