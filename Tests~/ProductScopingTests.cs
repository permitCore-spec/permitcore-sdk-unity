using System.Collections.Generic;
using System.Threading.Tasks;
using PermitCore;
using Xunit;

namespace PermitCore.Tests
{
    // [Product-scoping, added 2026-09-17] Real gap found live while testing the Unity demo: any
    // active key for the caller's tenant validated/activated successfully regardless of which
    // product it was issued for. These tests prove (a) LicenseResult.ProductId is parsed from a
    // real server response shape, and (b) ValidateAsync/ActivateAsync only include
    // expectedProductId in the outgoing request body when the caller actually passes one — this
    // is opt-in and must never silently switch on.
    public class ProductScopingTests
    {
        // Captures the exact body PermitCoreClient sent, without needing a real network call —
        // the same portable-core-outside-Unity approach the rest of this test suite uses.
        private sealed class CapturingTransport : IPermitCoreTransport
        {
            public string LastPostBody;
            public string ResponseBody = "{\"isValid\":true,\"productId\":\"11111111-1111-1111-1111-111111111111\"}";

            public Task<TransportResponse> GetAsync(string url, int timeoutSeconds) =>
                Task.FromResult(new TransportResponse(true, 200, "{\"nonce\":\"abc123\"}"));

            public Task<TransportResponse> PostAsync(string url, string jsonBody, int timeoutSeconds)
            {
                LastPostBody = jsonBody;
                return Task.FromResult(new TransportResponse(true, 200, ResponseBody));
            }
        }

        [Fact]
        public void LicenseResult_FromJson_ParsesProductId()
        {
            var d = Json.Parse("{\"isValid\":true,\"productId\":\"11111111-1111-1111-1111-111111111111\"}") as Dictionary<string, object>;
            var result = LicenseResult.FromJson(d);

            Assert.Equal("11111111-1111-1111-1111-111111111111", result.ProductId);
        }

        [Fact]
        public void LicenseResult_FromJson_MissingProductId_IsNull()
        {
            var d = Json.Parse("{\"isValid\":false,\"message\":\"License not found.\"}") as Dictionary<string, object>;
            var result = LicenseResult.FromJson(d);

            Assert.Null(result.ProductId);
        }

        [Fact]
        public async Task ValidateAsync_WithoutExpectedProductId_OmitsItFromRequest()
        {
            var transport = new CapturingTransport();
            var client = new PermitCoreClient("https://example.test", transport);

            await client.ValidateAsync("PERMIT-TEST");

            Assert.DoesNotContain("expectedProductId", transport.LastPostBody);
        }

        [Fact]
        public async Task ValidateAsync_WithExpectedProductId_IncludesItInRequest()
        {
            var transport = new CapturingTransport();
            var client = new PermitCoreClient("https://example.test", transport);

            await client.ValidateAsync("PERMIT-TEST", expectedProductId: "22222222-2222-2222-2222-222222222222");

            Assert.Contains("\"expectedProductId\":\"22222222-2222-2222-2222-222222222222\"", transport.LastPostBody);
        }

        [Fact]
        public async Task ActivateAsync_WithExpectedProductId_IncludesItInRequest()
        {
            var transport = new CapturingTransport();
            var client = new PermitCoreClient("https://example.test", transport);

            var result = await client.ActivateAsync("PERMIT-TEST", deviceId: "dev-1", expectedProductId: "33333333-3333-3333-3333-333333333333");

            Assert.Contains("\"expectedProductId\":\"33333333-3333-3333-3333-333333333333\"", transport.LastPostBody);
            Assert.Equal("11111111-1111-1111-1111-111111111111", result.ProductId);
        }

        [Fact]
        public async Task ActivateAsync_WithoutExpectedProductId_OmitsItFromRequest()
        {
            var transport = new CapturingTransport();
            var client = new PermitCoreClient("https://example.test", transport);

            await client.ActivateAsync("PERMIT-TEST", deviceId: "dev-1");

            Assert.DoesNotContain("expectedProductId", transport.LastPostBody);
        }
    }
}
