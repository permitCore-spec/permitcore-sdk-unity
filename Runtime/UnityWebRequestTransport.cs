// The one file in this SDK that references UnityEngine — deliberately isolated so the rest of
// the package (request building, JSON, ECDSA) stays plain, portable C# with zero engine
// dependency (testable with a plain `dotnet test` outside Unity). Uses UnityWebRequest rather
// than System.Net.Http.HttpClient because UnityWebRequest is the one HTTP client that works
// across every Unity build target this SDK claims to support, including WebGL (HttpClient does
// not work on WebGL — no raw socket access in the browser sandbox).
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace PermitCore
{
    public sealed class UnityWebRequestTransport : IPermitCoreTransport
    {
        public Task<TransportResponse> GetAsync(string url, int timeoutSeconds)
        {
            var req = UnityWebRequest.Get(url);
            return SendAsync(req, timeoutSeconds);
        }

        public Task<TransportResponse> PostAsync(string url, string jsonBody, int timeoutSeconds)
        {
            var req = new UnityWebRequest(url, "POST");
            byte[] bodyBytes = System.Text.Encoding.UTF8.GetBytes(jsonBody ?? string.Empty);
            req.uploadHandler = new UploadHandlerRaw(bodyBytes);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            return SendAsync(req, timeoutSeconds);
        }

        private static Task<TransportResponse> SendAsync(UnityWebRequest req, int timeoutSeconds)
        {
            req.timeout = timeoutSeconds;
            if (req.downloadHandler == null) req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("User-Agent", "PermitCore-Unity/1.0");

            var tcs = new TaskCompletionSource<TransportResponse>();
            var op = req.SendWebRequest();
            op.completed += _ =>
            {
                try
                {
#if UNITY_2020_1_OR_NEWER
                    bool networkOrProtocolError = req.result == UnityWebRequest.Result.ConnectionError
                                                    || req.result == UnityWebRequest.Result.DataProcessingError;
#else
                    bool networkOrProtocolError = req.isNetworkError || req.isHttpError && req.responseCode == 0;
#endif
                    if (networkOrProtocolError && req.responseCode == 0)
                    {
                        tcs.SetResult(new TransportResponse(false, 0, req.error));
                    }
                    else
                    {
                        // A non-2xx HTTP status is still a "successful" transport-level response for
                        // our purposes — the API returns real JSON bodies (with isValid:false) for
                        // most failure cases; only a genuine connection failure means "unreachable."
                        tcs.SetResult(new TransportResponse(true, req.responseCode, req.downloadHandler.text));
                    }
                }
                finally
                {
                    req.Dispose();
                }
            };
            return tcs.Task;
        }
    }

    /// <summary>Resolves this device's stable hardware fingerprint via Unity's own
    /// SystemInfo.deviceUniqueIdentifier — the engine's own cross-platform (desktop, mobile,
    /// console) answer to this exact problem, rather than reimplementing hostname/CPU-based
    /// fingerprinting like the non-Unity SDKs do. Not available/meaningful on WebGL (Unity
    /// returns "n/a" there) — callers targeting WebGL should pass their own deviceId.</summary>
    public static class UnityHardwareId
    {
        public static string Get()
        {
            string raw = SystemInfo.deviceUniqueIdentifier;
            return HardwareId.HashRawId(raw);
        }
    }
}
