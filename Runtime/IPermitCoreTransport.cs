using System.Threading.Tasks;

namespace PermitCore
{
    /// <summary>
    /// HTTP transport abstraction — the ONLY seam between PermitCoreClient's portable core logic
    /// (request building, JSON parsing, crypto) and the actual network call. Shipped default is
    /// <see cref="UnityWebRequestTransport"/> (uses UnityEngine.Networking, works on every Unity
    /// platform including WebGL, unlike System.Net.Http.HttpClient). Swappable via the
    /// PermitCoreClient constructor — this is also what lets this SDK's own test suite exercise
    /// every method against a real HTTP client outside the Unity Editor (no Editor/license
    /// available in an automated environment), proving the request/response logic is correct
    /// independent of which transport actually sends the bytes.
    /// </summary>
    public interface IPermitCoreTransport
    {
        Task<TransportResponse> GetAsync(string url, int timeoutSeconds);
        Task<TransportResponse> PostAsync(string url, string jsonBody, int timeoutSeconds);
    }

    public readonly struct TransportResponse
    {
        public readonly bool Success; // false on network-level failure (no response at all)
        public readonly long StatusCode;
        public readonly string Body;

        public TransportResponse(bool success, long statusCode, string body)
        {
            Success = success;
            StatusCode = statusCode;
            Body = body;
        }
    }
}
