# PermitCore Unity SDK

Official Unity client for [PermitCore](https://permitcore.dev) license management.

**Requirements:** Unity 2021.2+ (.NET Standard 2.1 API Compatibility Level, the default since
that version). Zero external/third-party package dependencies — no Newtonsoft.Json. The package
does declare one dependency on Unity's own **built-in `UnityWebRequest` module**
(`com.unity.modules.unitywebrequest`, bundled with every Editor install, not fetched from any
registry) — installing the SDK via UPM auto-enables it, so this only matters if you're adding the
SDK's `Runtime/` files manually rather than through Package Manager; in that case enable "Unity
Web Request" yourself under **Package Manager → Packages: Built-in** if you hit
`CS1069`/"type forwarded to assembly UnityEngine.UnityWebRequestModule". Works on
Windows/macOS/Linux standalone, mobile, and consoles. **WebGL note** below.

---

## Installation

Not yet published as a UPM registry package or git-URL package — install from the source ZIP:

1. Download the SDK (Admin panel → SDKs, or `GET /api/sdks/unity`).
2. Extract it anywhere outside your project's `Assets/` folder (e.g. a sibling `Packages/`
   folder, or anywhere on disk).
3. In Unity: **Window → Package Manager → + → Install package from disk...** → select the
   extracted folder's `package.json`.

Or, for a local (non-UPM) install: copy the `Runtime/` folder's contents directly into your
`Assets/` folder — it has no dependency on being loaded as a package.

---

## Quick start

```csharp
using PermitCore;
using UnityEngine;

public class LicenseGate : MonoBehaviour
{
    private PermitCoreClient _client;

    async void Start()
    {
        _client = new PermitCoreClient("https://api.permitcore.dev");
        var result = await _client.ValidateAsync("PERMIT-XXXX-XXXX-XXXX-XXXX");

        if (!result.IsValid)
        {
            Debug.LogError("License invalid: " + result.Message);
            return;
        }

        if (result.HasFeature("pro"))
            EnableProFeatures();
    }
}
```

`ValidateAsync`/`ActivateAsync` never throw for a rejected key or a network failure — both come
back as a `LicenseResult` with `IsValid == false`, so there's only one branch to check.

**Product scoping** (added 1.1.0): `/validate` and `/activate` find a key purely by the key itself
— by default, any active key belonging to your tenant validates successfully, regardless of which
of your products it was actually issued for. If your app should only accept keys issued for *this*
product, either check `result.ProductId` yourself, or pass the optional `expectedProductId`
parameter and let the server reject a mismatch for you (`result.ErrorCode == "WrongProduct"`). Find
your product's ID in the Admin panel under Products (or on a license's own detail page) — both now
show it with a copy button.

---

## Activate (call once per installation)

```csharp
var result = await _client.ActivateAsync(
    "PERMIT-XXXX-XXXX-XXXX-XXXX",
    deviceId: null,                    // auto-generated when omitted — see Hardware ID below
    deviceName: SystemInfo.deviceName,
    version: Application.version,      // optional — enforces minVersion/maxVersion on the license
    expectedProductId: null);          // optional — reject a key issued for a different product

if (!result.IsValid)
    Debug.LogError("Activation failed: " + result.Message);
```

Call `ActivateAsync` **once** per installation; use `ValidateAsync` on every subsequent launch.

---

## Metered billing

```csharp
await _client.MeterAsync(licenseKey, "level_completed");

await _client.MeterAsync(licenseKey, "export", quantity: 5,
    meta: new Dictionary<string, object> { ["format"] = "png" });
```

---

## Floating licenses

```csharp
var session = await _client.CheckoutAsync(licenseKey);
if (!session.Success) { Debug.LogError("No seats available: " + session.Message); return; }

// Heartbeat every 4-5 minutes (e.g. from a coroutine or InvokeRepeating)
await _client.HeartbeatAsync(session.SessionToken);

// On quit / logout
await _client.CheckinAsync(session.SessionToken);
```

---

## Offline license tokens

An offline activation token (`pc_offline_v1.<payload>.<signature>`) verifies a license with
**zero network calls**, using ECDSA P-256 against your tenant's public key
(`GET /api/v1/{tenantSlug}/public-key`). No extra package needed —
`System.Security.Cryptography.ECDsa` is part of Unity's .NET Standard 2.1 surface on Desktop,
Mobile, and Console targets.

```csharp
// Pure local verification — no network call, never throws.
var result = PermitCoreClient.VerifyOfflineToken(token, publicKeyBase64);
if (result.IsValid) Debug.Log("Valid! Product: " + result.Payload.ProductName);
```

```csharp
// Verify + bind to this device + persist locally (call once, e.g. at install time)
var result = _client.ActivateOffline(token, publicKeyBase64, deviceId);

// On every later launch — no token needed, reads the local cache, still no network call
var result = _client.ValidateOffline(deviceId);
```

```csharp
// Optional: ask the server to verify the token AND check its revocation status (requires network)
var result = await _client.VerifyOfflineOnlineAsync(token);
```

**Not supported on WebGL** — the browser sandbox WebGL builds run in has no
`System.Security.Cryptography` implementation (no raw socket/OS crypto access), so
`VerifyOfflineToken`/`ActivateOffline`/`ValidateOffline` will throw on that platform. Online
`ValidateAsync`/`ActivateAsync` work fine on WebGL (via `UnityWebRequest`); only the fully-offline
crypto path is Desktop/Mobile/Console-only. Not something this SDK can work around — it's a
platform constraint of the WebGL build target itself.

---

## Hardware ID

```csharp
string hwid = _client.GetHardwareId();
```

Default implementation: a random id generated once and persisted to a local file (under
`cacheDirectory`, see Constructor options below) — the same "works identically on every platform,
including outside Unity entirely" approach this SDK's own test suite relies on. On mobile/console,
Unity's own device identifier is usually a better fit (survives an app reinstall differently, and
needs no writable-file assumption):

```csharp
string hwid = UnityHardwareId.Get(); // SHA-256 of SystemInfo.deviceUniqueIdentifier
var result = await _client.ActivateAsync(licenseKey, deviceId: hwid);
```

---

## Constructor options

```csharp
var client = new PermitCoreClient(
    "https://api.permitcore.dev",
    transport: null,                              // defaults to UnityWebRequestTransport in Unity
    enableOfflineCache: true,
    cacheDirectory: Application.persistentDataPath, // recommended — see note below
    timeoutSeconds: 10);
```

Pass `Application.persistentDataPath` explicitly for `cacheDirectory` — the SDK's own default
(system temp directory) works but isn't the platform-idiomatic, guaranteed-writable, per-app
location Unity provides for exactly this purpose.

---

## LicenseResult reference

| Field | Type | Description |
|---|---|---|
| `IsValid` | `bool` | True if the license is active and valid |
| `ProductName` | `string` | Product the license belongs to |
| `RemainingActivations` | `int?` | Activation slots remaining |
| `ExpiresAt` | `string` | ISO 8601 expiry date, null = perpetual |
| `Features` | `List<string>` | Feature flag list, e.g. `["export", "api"]` |
| `CustomFields` | `Dictionary<string,string>` | Arbitrary key/value metadata set on the license |
| `IsTrial` | `bool` | True for trial licenses |
| `TrialDaysRemaining` | `int?` | Days until trial expires |
| `NodeLocked` | `bool` | True if bound to a specific device |
| `OfflineGraceDays` | `int?` | How many days the cache is valid |
| `MinVersion` / `MaxVersion` | `string` | Version enforcement bounds |
| `VendorWarning` | `string` | Non-fatal message from the vendor |
| `Message` | `string` | Reason when `IsValid == false` |
| `IsOffline` | `bool` | True when result came from local cache |
| `ErrorCode` | `string` | Stable, machine-readable failure reason (e.g. `"NotFound"`, `"SeatsExhausted"`, `"WrongProduct"`) |
| `ProductId` | `string` | Stable ID of the product this license belongs to. Always present when a license was found, even without passing `expectedProductId` |

`HasFeature(string feature)` — case-insensitive feature check.

---

## Architecture note: why this SDK is structured the way it is

Everything except two small files (`UnityWebRequestTransport.cs` and the `UnityHardwareId`
helper alongside it) is plain, portable C# with **zero** `UnityEngine` dependency —
`IPermitCoreTransport` is the one seam between the portable request/response/crypto logic and
the actual network call. This is not incidental: it is what lets this SDK's own test suite
(`Tests~/`, xUnit, run via `dotnet test` against the exact same `.cs` files the shipped package
uses) exercise the JSON parser, the ECDSA P-256/SHA-256 offline-token verification, and the
HWID hashing directly, outside Unity, with zero Editor/license dependency.

**Honest scope of what has and hasn't been verified** (updated 2026-09-17): the portable core
passed all 10 shared cross-SDK cryptographic test vectors (`SDKs/test-vectors/vectors.json`) plus
a full live run against a real PermitCore API and a real, server-issued offline token (activate,
validate, meter, floating checkout/heartbeat/checkin, offline-token verify with a genuine tamper/
wrong-device/wrong-key rejection, and the offline grace-cache fallback with the server pulled
offline mid-test) — the same rigor as this project's Go SDK.

**Update — a real Unity Editor smoke test has now been run** (2026-09-17), closing this gap. The
full package (including `UnityWebRequestTransport.cs`) was compiled inside a real, licensed Unity
Editor (2022.3.21f1) with the demo project, then actually exercised in Play mode: license
activation, the task dashboard, and Free/Pro feature-gating all confirmed working live against the
real staging API. Three real bugs were found and fixed by this, not hypothetical:

1. **Missing built-in module dependency**: `Demos/unity/task-manager-pro/Packages/manifest.json`
   was missing `com.unity.modules.unitywebrequest` as a listed dependency, and this SDK's own
   `package.json` declared no dependency on it either, so a fresh install hit
   `CS1069`/"type forwarded to assembly UnityEngine.UnityWebRequestModule, not referenced" — Unity's
   built-in Web Request module isn't enabled by default in every project template. Fixed by
   declaring it in both `package.json` (so any UPM install auto-enables it) and the demo's
   `manifest.json`.
2. **`ConfigureAwait(false)` broke Unity's main-thread requirement**: `PermitCoreClient.cs`'s
   internal awaits all used `.ConfigureAwait(false)` — correct, idiomatic practice for a generic
   portable .NET library (and a no-op under the `Tests~/` xUnit runner, which has no
   `SynchronizationContext` anyway), but it explicitly disables Unity's own
   `SynchronizationContext` continuation-marshaling. In the real Editor this meant a chained
   `await` (e.g. the nonce fetch inside `ActivateAsync` before the actual activate POST) could
   resume off Unity's main thread, and the next `UnityWebRequest` construction then threw "Create
   can only be called from the main thread." Fixed by removing all 11 `.ConfigureAwait(false)`
   calls from `PermitCoreClient.cs` — verified both by re-running `Tests~/` (still 14/14 green) and
   by a real, successful live activation in the Editor afterward.
3. **Demo swallowed exceptions silently**: `TaskManagerProDemo.ActivateAsync`/`RefreshLicenseAsync`
   are called fire-and-forget (`_ = ActivateAsync();`, required since `OnGUI` isn't `async`) but had
   no `catch` block — an exception (like bug #2 above) vanished with zero UI feedback and zero
   Console output, since nothing ever observed the discarded `Task`'s exception. This is what made
   bug #2 nearly undiagnosable at first; fixed by adding a `catch` that surfaces the message in both
   the demo's own error UI and `Debug.LogError`.

IL2CPP AOT compilation on an actual built player (as opposed to the Editor's Mono/JIT Play mode)
remains unverified — a real, if lower-risk, gap for anyone shipping a release build rather than
testing in-Editor.

---

## Development

```bash
cd Tests~
dotnet test
```

Compiles the SDK's portable core (`Runtime/*.cs` minus `UnityWebRequestTransport.cs`) against a
current .NET SDK — a superset of the netstandard2.1 surface this code deliberately restricts
itself to — and runs the JSON parser round-trip tests, HWID hashing tests, and the shared
cross-SDK cryptographic vectors from `../test-vectors/vectors.json`.
