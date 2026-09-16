using System.Collections.Generic;

namespace PermitCore
{
    /// <summary>
    /// Returned by Validate and Activate. Never null — a network failure or a rejected key both
    /// come back as a LicenseResult with IsValid == false, so callers only need one branch.
    /// </summary>
    public class LicenseResult
    {
        public bool IsValid;
        public string ProductName;
        public int? RemainingActivations;
        public string ExpiresAt;
        public string Message;
        public Dictionary<string, string> CustomFields;
        public string VendorWarning;
        public List<string> Features;
        public bool IsTrial;
        public int? TrialDaysRemaining;
        public bool NodeLocked;
        public int? OfflineGraceDays;
        public string MinVersion;
        public string MaxVersion;

        /// <summary>Set locally by this SDK (never sent by the server) — true when this result
        /// came from the local disk cache instead of a real network round trip.</summary>
        public bool IsOffline;

        public string OfflineCacheToken;

        /// <summary>Stable, machine-readable failure reason (e.g. "NotFound", "SeatsExhausted") —
        /// empty on success. Message stays free-text for display; branch on this instead.</summary>
        public string ErrorCode;

        /// <summary>Case-insensitive check whether a feature flag is present on the license.</summary>
        public bool HasFeature(string feature)
        {
            if (Features == null || feature == null) return false;
            foreach (var f in Features)
                if (string.Equals(f, feature, System.StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        internal static LicenseResult FromJson(Dictionary<string, object> d)
        {
            if (d == null) return new LicenseResult { IsValid = false, Message = "Empty response." };
            return new LicenseResult
            {
                IsValid = Json.GetBool(d, "isValid"),
                ProductName = Json.GetString(d, "productName"),
                RemainingActivations = Json.GetInt(d, "remainingActivations"),
                ExpiresAt = Json.GetString(d, "expiresAt"),
                Message = Json.GetString(d, "message"),
                CustomFields = Json.GetStringDictionary(d, "customFields"),
                VendorWarning = Json.GetString(d, "vendorWarning"),
                Features = Json.GetStringList(d, "features"),
                IsTrial = Json.GetBool(d, "isTrial"),
                TrialDaysRemaining = Json.GetInt(d, "trialDaysRemaining"),
                NodeLocked = Json.GetBool(d, "nodeLocked"),
                OfflineGraceDays = Json.GetInt(d, "offlineGraceDays"),
                MinVersion = Json.GetString(d, "minVersion"),
                MaxVersion = Json.GetString(d, "maxVersion"),
                OfflineCacheToken = Json.GetString(d, "offlineCacheToken"),
                ErrorCode = Json.GetString(d, "errorCode"),
            };
        }
    }

    /// <summary>Returned by Checkout and Heartbeat.</summary>
    public class FloatingSession
    {
        public bool Success;
        public string SessionToken;
        public string ExpiresAt;
        public string Message;

        internal static FloatingSession FromJson(Dictionary<string, object> d)
        {
            if (d == null) return new FloatingSession { Success = false, Message = "Empty response." };
            return new FloatingSession
            {
                Success = Json.GetBool(d, "success"),
                SessionToken = Json.GetString(d, "sessionToken"),
                ExpiresAt = Json.GetString(d, "expiresAt"),
                Message = Json.GetString(d, "message"),
            };
        }
    }

    /// <summary>Decoded payload of a pc_offline_v1 offline activation token.</summary>
    public class OfflineTokenPayload
    {
        public int Version;
        public string TokenId;
        public string TenantSlug;

        /// <summary>Identifies which of the tenant's signing keys produced this token — null on
        /// tokens issued before key versioning existed. Informational only.</summary>
        public string Kid;
        public string TenantId;
        public string LicenseId;
        public string LicenseKeyHash;
        public string DeviceId;
        public string DeviceName;
        public string ProductName;
        public int MaxActivations;
        public string IssuedAt;
        public string ExpiresAt;

        internal static OfflineTokenPayload FromJson(Dictionary<string, object> d)
        {
            if (d == null) return null;
            return new OfflineTokenPayload
            {
                Version = Json.GetInt(d, "version") ?? 1,
                TokenId = Json.GetString(d, "tokenId"),
                TenantSlug = Json.GetString(d, "tenantSlug"),
                Kid = Json.GetString(d, "kid"),
                TenantId = Json.GetString(d, "tenantId"),
                LicenseId = Json.GetString(d, "licenseId"),
                LicenseKeyHash = Json.GetString(d, "licenseKeyHash"),
                DeviceId = Json.GetString(d, "deviceId"),
                DeviceName = Json.GetString(d, "deviceName"),
                ProductName = Json.GetString(d, "productName"),
                MaxActivations = Json.GetInt(d, "maxActivations") ?? 0,
                IssuedAt = Json.GetString(d, "issuedAt"),
                ExpiresAt = Json.GetString(d, "expiresAt"),
            };
        }

        internal Dictionary<string, object> ToJson() => new()
        {
            ["version"] = Version,
            ["tokenId"] = TokenId,
            ["tenantSlug"] = TenantSlug,
            ["kid"] = Kid,
            ["tenantId"] = TenantId,
            ["licenseId"] = LicenseId,
            ["licenseKeyHash"] = LicenseKeyHash,
            ["deviceId"] = DeviceId,
            ["deviceName"] = DeviceName,
            ["productName"] = ProductName,
            ["maxActivations"] = MaxActivations,
            ["issuedAt"] = IssuedAt,
            ["expiresAt"] = ExpiresAt,
        };
    }

    public class OfflineTokenResult
    {
        public bool IsValid;
        public string Message;
        public OfflineTokenPayload Payload;
    }

    /// <summary>Decoded, verified payload of a pc_grace_v1 offline grace-cache token.</summary>
    public class GraceCachePayload
    {
        public int Version;
        public string TenantSlug;
        public string Kid;
        public string LicenseKeyHash;
        public string DeviceId;
        public bool IsValid;
        public string ProductName;
        public List<string> Features;
        public int RemainingActivations;
        public string ExpiresAt;
        public string IssuedAt;
        public string ValidUntil;

        internal static GraceCachePayload FromJson(Dictionary<string, object> d)
        {
            if (d == null) return null;
            return new GraceCachePayload
            {
                Version = Json.GetInt(d, "version") ?? 1,
                TenantSlug = Json.GetString(d, "tenantSlug"),
                Kid = Json.GetString(d, "kid"),
                LicenseKeyHash = Json.GetString(d, "licenseKeyHash"),
                DeviceId = Json.GetString(d, "deviceId"),
                IsValid = Json.GetBool(d, "isValid"),
                ProductName = Json.GetString(d, "productName"),
                Features = Json.GetStringList(d, "features"),
                RemainingActivations = Json.GetInt(d, "remainingActivations") ?? 0,
                ExpiresAt = Json.GetString(d, "expiresAt"),
                IssuedAt = Json.GetString(d, "issuedAt"),
                ValidUntil = Json.GetString(d, "validUntil"),
            };
        }
    }

    public class GraceCacheResult
    {
        public bool IsValid;
        public string Message;
        public GraceCachePayload Payload;
    }
}
