using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace PermitCore
{
    /// <summary>Portable half of hardware-id hashing — no UnityEngine dependency, so it's
    /// directly unit-testable outside the Editor. The Unity-idiomatic alternative is
    /// <see cref="UnityHardwareId"/> (SystemInfo.deviceUniqueIdentifier, in
    /// UnityWebRequestTransport.cs, which does need UnityEngine) — recommended on mobile/console
    /// where the engine's own device id is generally more stable than a file this class persists
    /// itself. <see cref="GetHardwareId"/> below (a random seed persisted to a local file, no OS
    /// API calls at all) is the SDK's zero-config default because it is the one approach that
    /// works identically on every platform this SDK claims to support AND outside Unity
    /// entirely, which is what makes it directly testable with plain `dotnet test`.</summary>
    public static class HardwareId
    {
        private const string SeedFileName = ".permitcore_seed";

        /// <summary>Default HWID source: a random seed generated once and persisted to
        /// <paramref name="cacheDirectory"/>, then SHA-256 hashed. No OS/platform API calls —
        /// deliberately avoids Environment.MachineName and friends, several of which throw
        /// PlatformNotSupportedException under IL2CPP on some console/mobile targets.</summary>
        public static string GetHardwareId(string cacheDirectory)
        {
            string seed;
            try
            {
                string seedPath = Path.Combine(cacheDirectory, SeedFileName);
                if (File.Exists(seedPath))
                {
                    seed = File.ReadAllText(seedPath).Trim();
                    if (string.IsNullOrEmpty(seed)) seed = NewSeed(seedPath);
                }
                else
                {
                    seed = NewSeed(seedPath);
                }
            }
            catch
            {
                // No writable filesystem (e.g. a sandboxed platform) — fall back to a
                // process-lifetime-only id rather than throwing. Not persisted, so it changes
                // every run; callers on such platforms should pass an explicit deviceId instead.
                seed = Guid.NewGuid().ToString("N");
            }
            return HashRawId(seed);
        }

        private static string NewSeed(string seedPath)
        {
            string seed = Guid.NewGuid().ToString("N");
            File.WriteAllText(seedPath, seed);
            return seed;
        }

        public static string HashRawId(string rawId)
        {
            if (string.IsNullOrEmpty(rawId)) rawId = "unknown-device";
            byte[] bytes = Encoding.UTF8.GetBytes(rawId);
            byte[] hash;
            using (var sha256 = SHA256.Create())
                hash = sha256.ComputeHash(bytes);

            var sb = new StringBuilder(hash.Length * 2);
            foreach (byte b in hash) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }
    }
}
