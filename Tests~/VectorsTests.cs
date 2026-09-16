// Cross-SDK protocol test-vector suite (CTO review F-27). Loads the shared, language-agnostic
// test vectors from SDKs/test-vectors/vectors.json and drives this SDK's own offline-token
// verification primitives against them, so a bug in this SDK's ECDSA P-256/SHA-256 handling or
// payload parsing is caught the same way it would be in any other language's SDK.
using System;
using System.Collections.Generic;
using System.IO;
using PermitCore;
using Xunit;

namespace PermitCore.UnityTests
{
    public class VectorsTests
    {
        private static readonly string VectorsPath =
            Path.Combine(AppContext.BaseDirectory, "test-vectors", "vectors.json");

        private static Dictionary<string, object> LoadVectors()
        {
            Assert.True(File.Exists(VectorsPath), $"Shared test-vector file not found at {VectorsPath}");
            string json = File.ReadAllText(VectorsPath);
            return (Dictionary<string, object>)Json.Parse(json);
        }

        private static string GetPublicKey(Dictionary<string, object> vectors)
        {
            var signingKeyPair = Json.GetObject(vectors, "signingKeyPair");
            return Json.GetString(signingKeyPair, "publicKeyBase64Spki");
        }

        [Fact]
        public void OfflineTokenVectors_AllMatchExpectedResult()
        {
            var vectors = LoadVectors();
            string publicKey = GetPublicKey(vectors);
            var list = (List<object>)vectors["offlineTokenVectors"];

            foreach (var raw in list)
            {
                var v = (Dictionary<string, object>)raw;
                string name = Json.GetString(v, "name");
                string token = Json.GetString(v, "token");
                bool expectValid = Json.GetBool(v, "expectValid");

                var result = OfflineVerification.VerifyOfflineToken(token, publicKey);
                Assert.True(result.IsValid == expectValid,
                    $"[offlineTokenVectors:{name}] expected IsValid={expectValid}, got {result.IsValid} ({result.Message})");

                var expectedPayload = Json.GetObject(v, "expectedPayload");
                if (expectedPayload == null) continue;

                Assert.NotNull(result.Payload);
                var p = result.Payload;
                Assert.Equal(Json.GetInt(expectedPayload, "version"), p.Version);
                Assert.Equal(Json.GetString(expectedPayload, "tokenId"), p.TokenId);
                Assert.Equal(Json.GetString(expectedPayload, "tenantSlug"), p.TenantSlug);
                Assert.Equal(Json.GetString(expectedPayload, "kid"), p.Kid);
                Assert.Equal(Json.GetString(expectedPayload, "tenantId"), p.TenantId);
                Assert.Equal(Json.GetString(expectedPayload, "licenseId"), p.LicenseId);
                Assert.Equal(Json.GetString(expectedPayload, "licenseKeyHash"), p.LicenseKeyHash);
                Assert.Equal(Json.GetString(expectedPayload, "deviceId"), p.DeviceId);
                Assert.Equal(Json.GetString(expectedPayload, "deviceName"), p.DeviceName);
                Assert.Equal(Json.GetString(expectedPayload, "productName"), p.ProductName);
                Assert.Equal(Json.GetInt(expectedPayload, "maxActivations"), p.MaxActivations);
                Assert.Equal(Json.GetString(expectedPayload, "issuedAt"), p.IssuedAt);
                Assert.Equal(Json.GetString(expectedPayload, "expiresAt"), p.ExpiresAt);
            }
        }

        [Fact]
        public void GraceTokenVectors_AllMatchExpectedResult()
        {
            var vectors = LoadVectors();
            string publicKey = GetPublicKey(vectors);
            var list = (List<object>)vectors["graceTokenVectors"];

            foreach (var raw in list)
            {
                var v = (Dictionary<string, object>)raw;
                string name = Json.GetString(v, "name");
                string token = Json.GetString(v, "token");
                bool expectValid = Json.GetBool(v, "expectValid");

                var result = OfflineVerification.VerifyGraceCacheToken(token, publicKey);
                Assert.True(result.IsValid == expectValid,
                    $"[graceTokenVectors:{name}] expected IsValid={expectValid}, got {result.IsValid} ({result.Message})");

                var expectedPayload = Json.GetObject(v, "expectedPayload");
                if (expectedPayload == null) continue;

                Assert.NotNull(result.Payload);
                var p = result.Payload;
                Assert.Equal(Json.GetInt(expectedPayload, "version"), p.Version);
                Assert.Equal(Json.GetString(expectedPayload, "tenantSlug"), p.TenantSlug);
                Assert.Equal(Json.GetString(expectedPayload, "kid"), p.Kid);
                Assert.Equal(Json.GetString(expectedPayload, "licenseKeyHash"), p.LicenseKeyHash);
                Assert.Equal(Json.GetString(expectedPayload, "deviceId"), p.DeviceId);
                Assert.Equal(Json.GetBool(expectedPayload, "isValid"), p.IsValid);
                Assert.Equal(Json.GetString(expectedPayload, "productName"), p.ProductName);
                Assert.Equal(Json.GetStringList(expectedPayload, "features"), p.Features);
                Assert.Equal(Json.GetInt(expectedPayload, "remainingActivations"), p.RemainingActivations);
                Assert.Equal(Json.GetString(expectedPayload, "expiresAt"), p.ExpiresAt);
                Assert.Equal(Json.GetString(expectedPayload, "issuedAt"), p.IssuedAt);
                Assert.Equal(Json.GetString(expectedPayload, "validUntil"), p.ValidUntil);
            }
        }

        // ── request shapes ──────────────────────────────────────────────────────────────────
        // Not executable against a live server here (see vectors.json's own note) — asserts the
        // key sets PermitCoreClient.ValidateAsync/ActivateAsync build match requiredKeys/
        // optionalKeys exactly, catching silent field drift.

        [Fact]
        public void ValidateRequestShape_MatchesSharedContract()
        {
            var vectors = LoadVectors();
            var shape = Json.GetObject(Json.GetObject(vectors, "requestShapes"), "validate");

            var minimal = new Dictionary<string, object> { ["licenseKey"] = "PERMIT-TEST" };
            var full = new Dictionary<string, object> { ["licenseKey"] = "PERMIT-TEST", ["version"] = "1.0" };

            AssertKeySet(minimal, Json.GetStringList(shape, "requiredKeys"), null);
            AssertKeySet(full, Json.GetStringList(shape, "requiredKeys"), Json.GetStringList(shape, "optionalKeys"));
        }

        [Fact]
        public void ActivateRequestShape_MatchesSharedContract()
        {
            var vectors = LoadVectors();
            var shape = Json.GetObject(Json.GetObject(vectors, "requestShapes"), "activate");

            var minimal = new Dictionary<string, object> { ["licenseKey"] = "PERMIT-TEST", ["deviceId"] = "dev-1", ["nonce"] = "abc" };
            var full = new Dictionary<string, object>
            {
                ["licenseKey"] = "PERMIT-TEST", ["deviceId"] = "dev-1", ["nonce"] = "abc",
                ["deviceName"] = "My PC", ["version"] = "1.0",
            };

            AssertKeySet(minimal, Json.GetStringList(shape, "requiredKeys"), null);
            AssertKeySet(full, Json.GetStringList(shape, "requiredKeys"), Json.GetStringList(shape, "optionalKeys"));
        }

        private static void AssertKeySet(Dictionary<string, object> body, List<string> required, List<string> optional)
        {
            var want = new HashSet<string>(required ?? new List<string>());
            if (optional != null) want.UnionWith(optional);
            Assert.Equal(want.Count, body.Count);
            foreach (var key in body.Keys)
                Assert.Contains(key, want);
        }
    }
}
