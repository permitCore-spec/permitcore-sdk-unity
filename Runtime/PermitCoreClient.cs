// Official PermitCore client for Unity (https://permitcore.dev). Only UnityWebRequestTransport.cs
// and HardwareId's Unity-specific sibling touch UnityEngine — everything here is plain,
// portable C#, directly testable outside the Unity Editor.
//
// Quick start:
//   var client = new PermitCoreClient("https://api.permitcore.dev");
//   var result = await client.ValidateAsync("PERMIT-XXXX-XXXX-XXXX-XXXX");
//   if (result.IsValid) Debug.Log("Valid! Product: " + result.ProductName);
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace PermitCore
{
    public sealed class PermitCoreClient
    {
        private readonly string _baseUrl;
        private readonly IPermitCoreTransport _transport;
        private readonly bool _enableCache;
        private readonly string _cacheDirectory;
        private readonly int _timeoutSeconds;

        /// <param name="baseUrl">Your PermitCore API base URL, e.g. "https://api.permitcore.dev".</param>
        /// <param name="transport">HTTP transport. Defaults to <see cref="UnityWebRequestTransport"/>
        /// when null — pass a custom <see cref="IPermitCoreTransport"/> only for testing outside Unity.</param>
        /// <param name="enableOfflineCache">Cache a signed grace-cache token to local disk so
        /// Validate/Activate can fall back to a cryptographically-verified last-known-good result
        /// when the server is unreachable. Default true.</param>
        /// <param name="cacheDirectory">Where the offline cache and HWID seed files are written.
        /// Defaults to the system temp directory — pass Application.persistentDataPath from Unity
        /// for the idiomatic, per-app-safe location.</param>
        public PermitCoreClient(
            string baseUrl,
            IPermitCoreTransport transport = null,
            bool enableOfflineCache = true,
            string cacheDirectory = null,
            int timeoutSeconds = 10)
        {
            _baseUrl = (baseUrl ?? string.Empty).TrimEnd('/');
            _transport = transport ?? CreateDefaultTransport();
            _enableCache = enableOfflineCache;
            _cacheDirectory = cacheDirectory ?? Path.GetTempPath();
            _timeoutSeconds = timeoutSeconds;
        }

        // Only this one seam is conditionally compiled — everywhere else in this file is plain,
        // portable C# with no #if at all. Inside Unity (UNITY_5_3_OR_NEWER is defined by every
        // Unity Editor/player build automatically), `new PermitCoreClient(url)` works with zero
        // extra arguments, using UnityWebRequestTransport. Outside Unity (this SDK's own test
        // suite, or any other .NET host embedding just the portable core), a transport must be
        // passed explicitly — there's no meaningful "default" HTTP client to assume there.
#if UNITY_5_3_OR_NEWER
        private static IPermitCoreTransport CreateDefaultTransport() => new UnityWebRequestTransport();
#else
        private static IPermitCoreTransport CreateDefaultTransport() =>
            throw new InvalidOperationException(
                "No IPermitCoreTransport was provided and no default is available outside Unity — " +
                "pass one explicitly (e.g. new PermitCoreClient(url, new UnityWebRequestTransport())).");
#endif

        /// <summary>Generates this device's default hardware fingerprint (file-seed based —
        /// works identically on every platform and outside Unity). On mobile/console, prefer
        /// <see cref="UnityHardwareId.Get"/> (SystemInfo.deviceUniqueIdentifier) instead.</summary>
        public string GetHardwareId() => HardwareId.GetHardwareId(_cacheDirectory);

        // ── Validate ─────────────────────────────────────────────────────────────────────────

        /// <summary>Validates a license key. Does NOT consume an activation slot. version is
        /// optional — lets the server enforce MinVersion/MaxVersion restrictions. expectedProductId
        /// is optional — when set, the server rejects (LicenseResult.ErrorCode == "WrongProduct")
        /// unless this key belongs to that exact product; without it, any active key for the
        /// caller's tenant validates successfully regardless of which product it was issued for.
        /// Falls back to the local disk cache when the server is unreachable, as long as the
        /// license has an offline grace period configured. Never throws — a network failure and a
        /// rejected key both come back as a LicenseResult with IsValid == false.</summary>
        public async Task<LicenseResult> ValidateAsync(string licenseKey, string version = null, string expectedProductId = null)
        {
            var body = new Dictionary<string, object> { ["licenseKey"] = licenseKey };
            if (!string.IsNullOrEmpty(version)) body["version"] = version;
            if (!string.IsNullOrEmpty(expectedProductId)) body["expectedProductId"] = expectedProductId;

            var resp = await PostAsync("api/v1/validate", body);
            if (!resp.Success)
                return LoadCache(licenseKey) ?? OfflineUnreachable();

            var result = ParseLicenseResult(resp.Body);
            if (result.IsValid) await SaveCacheAsync(licenseKey, result);
            return result;
        }

        /// <summary>Validates AND activates the key on this device. Call only once per
        /// installation — use ValidateAsync on every later launch. deviceId defaults to
        /// GetHardwareId() when null/empty. version and expectedProductId are optional, same
        /// meaning as Validate's.</summary>
        public async Task<LicenseResult> ActivateAsync(string licenseKey, string deviceId = null, string deviceName = null, string version = null, string expectedProductId = null)
        {
            string hwid = string.IsNullOrEmpty(deviceId) ? GetHardwareId() : deviceId;

            var nonceResp = await GetAsync("api/v1/nonce");
            if (!nonceResp.Success)
                return LoadCache(licenseKey) ?? OfflineUnreachable(); // [S-Continuity]

            string nonce = Json.GetString(Json.Parse(nonceResp.Body) as Dictionary<string, object>, "nonce");

            var body = new Dictionary<string, object> { ["licenseKey"] = licenseKey, ["deviceId"] = hwid, ["nonce"] = nonce };
            if (!string.IsNullOrEmpty(deviceName)) body["deviceName"] = deviceName;
            if (!string.IsNullOrEmpty(version)) body["version"] = version;
            if (!string.IsNullOrEmpty(expectedProductId)) body["expectedProductId"] = expectedProductId;

            var resp = await PostAsync("api/v1/activate", body);
            if (!resp.Success)
                return LoadCache(licenseKey) ?? OfflineUnreachable();

            var result = ParseLicenseResult(resp.Body);
            if (result.IsValid) await SaveCacheAsync(licenseKey, result);
            return result;
        }

        // ── Meter ────────────────────────────────────────────────────────────────────────────

        /// <summary>Records a usage event for metered billing. Returns true if the event was
        /// recorded on the server, false on any failure (network error, or the server rejecting
        /// the event) — never throws.</summary>
        public async Task<bool> MeterAsync(string licenseKey, string eventName, int quantity = 1, Dictionary<string, object> meta = null)
        {
            var body = new Dictionary<string, object>
            {
                ["licenseKey"] = licenseKey,
                ["eventName"] = eventName,
                ["quantity"] = quantity <= 0 ? 1 : quantity,
            };
            if (meta != null) body["meta"] = meta;

            var resp = await PostAsync("api/v1/meter", body);
            if (!resp.Success) return false;
            var data = Json.Parse(resp.Body) as Dictionary<string, object>;
            return Json.GetBool(data, "recorded");
        }

        // ── Floating licenses ────────────────────────────────────────────────────────────────

        public async Task<FloatingSession> CheckoutAsync(string licenseKey, string deviceId = null, string deviceName = null)
        {
            string hwid = string.IsNullOrEmpty(deviceId) ? GetHardwareId() : deviceId;
            var body = new Dictionary<string, object> { ["licenseKey"] = licenseKey, ["deviceId"] = hwid };
            if (!string.IsNullOrEmpty(deviceName)) body["deviceName"] = deviceName;

            var resp = await PostAsync("api/v1/float/checkout", body);
            if (!resp.Success) return new FloatingSession { Success = false, Message = "Cannot reach license server." };
            return FloatingSession.FromJson(Json.Parse(resp.Body) as Dictionary<string, object>);
        }

        /// <summary>Keeps a floating session alive. Call every 4-5 minutes.</summary>
        public async Task<FloatingSession> HeartbeatAsync(string sessionToken)
        {
            var resp = await PostAsync("api/v1/float/heartbeat", new Dictionary<string, object> { ["sessionToken"] = sessionToken });
            if (!resp.Success) return new FloatingSession { Success = false, Message = "Cannot reach license server." };
            return FloatingSession.FromJson(Json.Parse(resp.Body) as Dictionary<string, object>);
        }

        /// <summary>Releases a floating seat. Best-effort — failures are swallowed, matching
        /// every other PermitCore SDK's checkin() (a failed release on shutdown must never block it).</summary>
        public async Task CheckinAsync(string sessionToken)
        {
            try
            {
                await PostAsync("api/v1/float/checkin", new Dictionary<string, object> { ["sessionToken"] = sessionToken });
            }
            catch { /* best-effort */ }
        }

        // ── Offline tokens (pure local verification — see OfflineVerification) ─────────────────

        public static OfflineTokenResult VerifyOfflineToken(string token, string publicKeyBase64Spki)
            => OfflineVerification.VerifyOfflineToken(token, publicKeyBase64Spki);

        public static GraceCacheResult VerifyGraceCacheToken(string token, string publicKeyBase64Spki)
            => OfflineVerification.VerifyGraceCacheToken(token, publicKeyBase64Spki);

        /// <summary>Verifies an offline token, checks it was issued for this device, and — on
        /// success — persists the verified payload to local disk so ValidateOffline can be
        /// called later without needing the original token again.</summary>
        public OfflineTokenResult ActivateOffline(string token, string publicKeyBase64Spki, string deviceId)
        {
            var result = OfflineVerification.VerifyOfflineToken(token, publicKeyBase64Spki);
            if (!result.IsValid || result.Payload == null) return result;

            if (!string.Equals(result.Payload.DeviceId ?? "", deviceId ?? "", StringComparison.OrdinalIgnoreCase))
                return new OfflineTokenResult { IsValid = false, Message = "Token was issued for a different device.", Payload = result.Payload };

            try
            {
                string json = Json.Serialize(result.Payload.ToJson());
                File.WriteAllText(OfflineCachePath(deviceId), json);
            }
            catch { /* cache failure must never block a successful verification */ }

            return result;
        }

        /// <summary>Reads the locally persisted offline-activation result (from a prior
        /// ActivateOffline call). No network call, no token needed.</summary>
        public OfflineTokenResult ValidateOffline(string deviceId)
        {
            try
            {
                string path = OfflineCachePath(deviceId);
                if (!File.Exists(path)) return new OfflineTokenResult { IsValid = false, Message = "No local offline activation found." };

                var payload = OfflineTokenPayload.FromJson(Json.Parse(File.ReadAllText(path)) as Dictionary<string, object>);
                if (payload == null) return new OfflineTokenResult { IsValid = false, Message = "Corrupt local offline activation." };

                if (!string.Equals(payload.DeviceId ?? "", deviceId ?? "", StringComparison.OrdinalIgnoreCase))
                    return new OfflineTokenResult { IsValid = false, Message = "Device mismatch.", Payload = payload };

                if (!string.IsNullOrEmpty(payload.ExpiresAt) &&
                    DateTime.TryParse(payload.ExpiresAt, System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var exp) &&
                    exp < DateTime.UtcNow)
                    return new OfflineTokenResult { IsValid = false, Message = "Offline activation expired.", Payload = payload };

                return new OfflineTokenResult { IsValid = true, Message = "Valid (offline).", Payload = payload };
            }
            catch
            {
                return new OfflineTokenResult { IsValid = false, Message = "Corrupt local offline activation." };
            }
        }

        /// <summary>Asks the server to verify a token AND check its revocation status. Requires
        /// network — use the static VerifyOfflineToken for pure offline verification.</summary>
        public async Task<OfflineTokenResult> VerifyOfflineOnlineAsync(string token)
        {
            var resp = await PostAsync("api/v1/offline/verify", new Dictionary<string, object> { ["token"] = token });
            if (!resp.Success) return new OfflineTokenResult { IsValid = false, Message = "Cannot reach license server." };
            var data = Json.Parse(resp.Body) as Dictionary<string, object>;
            return new OfflineTokenResult { IsValid = Json.GetBool(data, "isValid"), Message = Json.GetString(data, "message") ?? "" };
        }

        // ── HTTP helpers ─────────────────────────────────────────────────────────────────────

        private Task<TransportResponse> GetAsync(string path) => _transport.GetAsync($"{_baseUrl}/{path}", _timeoutSeconds);

        private Task<TransportResponse> PostAsync(string path, Dictionary<string, object> body)
            => _transport.PostAsync($"{_baseUrl}/{path}", Json.Serialize(body), _timeoutSeconds);

        private static LicenseResult ParseLicenseResult(string json)
        {
            try { return LicenseResult.FromJson(Json.Parse(json) as Dictionary<string, object>); }
            catch { return new LicenseResult { IsValid = false, Message = "Invalid response from server." }; }
        }

        private static LicenseResult OfflineUnreachable() =>
            new() { IsValid = false, Message = "Cannot reach license server.", IsOffline = true };

        // ── Offline grace cache (pc_grace_v1) — save on success, load on network failure ───────

        // Awaited by ValidateAsync/ActivateAsync before they return — must complete (or fail
        // silently) before the caller sees the result, otherwise an immediate offline fallback
        // check (e.g. this SDK's own tests, or an app that validates once then immediately drops
        // network) could race against a cache write that hasn't landed yet. Caching itself stays
        // best-effort: any failure here is swallowed, never surfaced to the caller.
        private async Task SaveCacheAsync(string licenseKey, LicenseResult result)
        {
            if (!_enableCache || string.IsNullOrEmpty(result.OfflineCacheToken)) return;

            string tenantSlug = ExtractUnverifiedTenantSlug(result.OfflineCacheToken);
            if (string.IsNullOrEmpty(tenantSlug)) return;

            try
            {
                var pkResp = await GetAsync($"api/v1/{Uri.EscapeDataString(tenantSlug)}/public-key");
                if (!pkResp.Success) return;
                string publicKey = Json.GetString(Json.Parse(pkResp.Body) as Dictionary<string, object>, "publicKey");
                if (string.IsNullOrEmpty(publicKey)) return;

                var check = OfflineVerification.VerifyGraceCacheToken(result.OfflineCacheToken, publicKey);
                if (!check.IsValid) return;

                var entry = new Dictionary<string, object> { ["token"] = result.OfflineCacheToken, ["public_key"] = publicKey };
                File.WriteAllText(CachePath(licenseKey), Json.Serialize(entry));
            }
            catch { /* cache failure must never block the normal online flow */ }
        }

        private LicenseResult LoadCache(string licenseKey)
        {
            if (!_enableCache) return null;
            try
            {
                string path = CachePath(licenseKey);
                if (!File.Exists(path)) return null;

                var entry = Json.Parse(File.ReadAllText(path)) as Dictionary<string, object>;
                string token = Json.GetString(entry, "token");
                string publicKey = Json.GetString(entry, "public_key");
                if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(publicKey)) return null;

                var check = OfflineVerification.VerifyGraceCacheToken(token, publicKey);
                if (!check.IsValid || check.Payload == null) return null;

                var p = check.Payload;
                return new LicenseResult
                {
                    IsValid = p.IsValid,
                    ProductName = p.ProductName,
                    RemainingActivations = p.RemainingActivations,
                    ExpiresAt = p.ExpiresAt,
                    Features = p.Features,
                    IsOffline = true,
                    Message = $"Offline mode — valid until {p.ValidUntil} (cryptographically verified)",
                };
            }
            catch { return null; }
        }

        private static string ExtractUnverifiedTenantSlug(string token)
        {
            var parts = (token ?? "").Split('.');
            if (parts.Length != 3 || parts[0] != "pc_grace_v1") return null;
            try
            {
                string json = System.Text.Encoding.UTF8.GetString(OfflineVerification.Base64UrlDecode(parts[1]));
                return Json.GetString(Json.Parse(json) as Dictionary<string, object>, "tenantSlug");
            }
            catch { return null; }
        }

        private string CachePath(string licenseKey) => Path.Combine(_cacheDirectory, ".permitcore_cache_" + ShortHash(licenseKey));
        private string OfflineCachePath(string deviceId) => Path.Combine(_cacheDirectory, ".permitcore_offline_" + ShortHash(deviceId));

        private static string ShortHash(string s) => HardwareId.HashRawId(s).Substring(0, 16);
    }
}
