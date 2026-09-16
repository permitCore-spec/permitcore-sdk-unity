// Offline/grace-cache token verification — entirely local, no network call. Mirrors
// PermitCore.Infrastructure.Services.OfflineActivationService.Verify/VerifyGraceCache
// byte-for-byte: ECDSA P-256, SHA-256, RAW IEEE P1363 signature (64-byte r||s). The signature
// covers the UTF-8 bytes of the base64url-encoded PAYLOAD STRING, not the decoded JSON bytes.
//
// Deliberately avoids ECDsa.ImportSubjectPublicKeyInfo — that API's presence under Unity's
// IL2CPP-compiled BCL is not guaranteed across every supported Unity/platform/scripting-backend
// combination the way plain ECParameters/ECDsa.Create(ECParameters) is (part of the
// netstandard2.1 contract). Instead this hand-parses the small, fixed X.509 SubjectPublicKeyInfo
// DER structure every PermitCore signing key uses (P-256 SPKI is a completely standard, fixed
// shape — see ParseP256PublicKeyFromSpki below) and builds ECParameters directly.
//
// No DER conversion is needed for the SIGNATURE itself, unlike PermitCore's PHP/Python/C++ SDKs
// (whose OpenSSL-based crypto libraries require DER): .NET's ECDsa.VerifyHash(byte[], byte[]) —
// the original two-argument overload, present since .NET Framework and squarely netstandard2.0/
// 2.1 — has always operated on the raw IEEE P1363 (r||s) signature format by default. The
// DSASignatureFormat enum added in .NET 5 exists to ALSO support DER as an alternative, not to
// change the original overload's default away from P1363 — so the raw 64-byte signature is
// passed straight through, exactly like this project's Go SDK does with ecdsa.Verify(pub,hash,r,s).
// (An earlier version of this file assumed the opposite and DER-encoded the signature first —
// caught immediately by this SDK's own test suite failing against the shared cross-SDK vectors,
// not left as a latent bug.)
using System;
using System.Security.Cryptography;
using System.Text;

namespace PermitCore
{
    public static class OfflineVerification
    {
        private const string OfflineTokenPrefix = "pc_offline_v1";
        private const string GraceTokenPrefix = "pc_grace_v1";

        /// <summary>
        /// Verifies a pc_offline_v1 offline activation token entirely locally. This is the
        /// primitive every other offline_* method builds on. Never throws — a malformed,
        /// tampered, or expired token just comes back with IsValid = false and a descriptive
        /// Message.
        /// </summary>
        public static OfflineTokenResult VerifyOfflineToken(string token, string publicKeyBase64Spki)
        {
            var parts = SplitToken(token, OfflineTokenPrefix);
            if (parts == null) return new OfflineTokenResult { IsValid = false, Message = "Malformed token." };

            if (!TryVerifySignature(parts[1], parts[2], publicKeyBase64Spki))
                return new OfflineTokenResult { IsValid = false, Message = "Invalid signature." };

            OfflineTokenPayload payload;
            try
            {
                var json = Encoding.UTF8.GetString(Base64UrlDecode(parts[1]));
                payload = OfflineTokenPayload.FromJson(Json.Parse(json) as System.Collections.Generic.Dictionary<string, object>);
            }
            catch
            {
                return new OfflineTokenResult { IsValid = false, Message = "Invalid or corrupt token." };
            }

            if (IsExpired(payload?.ExpiresAt))
                return new OfflineTokenResult { IsValid = false, Message = "Token expired.", Payload = payload };

            return new OfflineTokenResult { IsValid = true, Message = "Valid.", Payload = payload };
        }

        /// <summary>
        /// Verifies a pc_grace_v1 offline grace-cache token entirely locally. Exposed publicly
        /// so a custom integration (not using the built-in disk cache) can build its own caching
        /// around the same verified primitive.
        /// </summary>
        public static GraceCacheResult VerifyGraceCacheToken(string token, string publicKeyBase64Spki)
        {
            var parts = SplitToken(token, GraceTokenPrefix);
            if (parts == null) return new GraceCacheResult { IsValid = false, Message = "Malformed token." };

            if (!TryVerifySignature(parts[1], parts[2], publicKeyBase64Spki))
                return new GraceCacheResult { IsValid = false, Message = "Invalid signature." };

            GraceCachePayload payload;
            try
            {
                var json = Encoding.UTF8.GetString(Base64UrlDecode(parts[1]));
                payload = GraceCachePayload.FromJson(Json.Parse(json) as System.Collections.Generic.Dictionary<string, object>);
            }
            catch
            {
                return new GraceCacheResult { IsValid = false, Message = "Invalid or corrupt token." };
            }

            if (IsExpired(payload?.ValidUntil))
                return new GraceCacheResult { IsValid = false, Message = "Grace period expired.", Payload = payload };

            return new GraceCacheResult { IsValid = true, Message = "Valid.", Payload = payload };
        }

        private static string[] SplitToken(string token, string expectedPrefix)
        {
            if (string.IsNullOrEmpty(token)) return null;
            var parts = token.Split('.');
            if (parts.Length != 3 || parts[0] != expectedPrefix) return null;
            return parts;
        }

        private static bool TryVerifySignature(string payloadB64Url, string sigB64Url, string publicKeyBase64Spki)
        {
            try
            {
                // Raw 64-byte r||s — IEEE P1363 is .NET's ECDsa.VerifyHash(byte[],byte[]) default
                // and ORIGINAL signature format (predates .NET 5, unlike RSA's PKCS#1 default);
                // the DSASignatureFormat enum added in .NET 5 exists to ALSO support DER, not to
                // change the default away from P1363. No conversion needed — pass it straight
                // through, exactly like this project's Go SDK does with ecdsa.Verify(pub,hash,r,s).
                byte[] sig = Base64UrlDecode(sigB64Url);
                if (sig.Length != 64) return false;

                var (x, y) = ParseP256PublicKeyFromSpki(Convert.FromBase64String(publicKeyBase64Spki));

                var ecParams = new ECParameters
                {
                    Curve = ECCurve.NamedCurves.nistP256,
                    Q = new ECPoint { X = x, Y = y },
                };

                using var ecdsa = ECDsa.Create(ecParams);
                byte[] payloadBytes = Encoding.UTF8.GetBytes(payloadB64Url);
                byte[] hash;
                using (var sha256 = SHA256.Create())
                    hash = sha256.ComputeHash(payloadBytes);

                return ecdsa.VerifyHash(hash, sig);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Extracts the raw EC point (X, Y — 32 bytes each) from a P-256 X.509
        /// SubjectPublicKeyInfo DER blob. Every PermitCore tenant signing key is exported in
        /// exactly this standard, fixed shape:
        ///   SEQUENCE {
        ///     SEQUENCE { OID ecPublicKey, OID prime256v1 }   -- AlgorithmIdentifier
        ///     BIT STRING { 0x00 unused-bits, 0x04 uncompressed-point-marker, X[32], Y[32] }
        ///   }
        /// Walked generically (tag/length-aware, not hardcoded byte offsets) so it tolerates the
        /// AlgorithmIdentifier SEQUENCE being any length, not just today's exact 21 bytes.
        /// </summary>
        private static (byte[] x, byte[] y) ParseP256PublicKeyFromSpki(byte[] der)
        {
            int i = 0;
            ExpectTag(der, ref i, 0x30); // outer SEQUENCE
            ReadLength(der, ref i);

            ExpectTag(der, ref i, 0x30); // AlgorithmIdentifier SEQUENCE
            int algLen = ReadLength(der, ref i);
            i += algLen; // skip over it entirely — its content doesn't matter, every key here is P-256

            ExpectTag(der, ref i, 0x03); // BIT STRING
            int bitStrLen = ReadLength(der, ref i);
            if (bitStrLen != 66) throw new FormatException("Unexpected EC point length in SPKI — not a P-256 key?");

            int unusedBits = der[i++];
            if (unusedBits != 0) throw new FormatException("Unexpected unused-bits count in SPKI BIT STRING.");
            int marker = der[i++];
            if (marker != 0x04) throw new FormatException("Expected uncompressed EC point marker (0x04).");

            byte[] x = new byte[32];
            byte[] y = new byte[32];
            Array.Copy(der, i, x, 0, 32);
            Array.Copy(der, i + 32, y, 0, 32);
            return (x, y);
        }

        private static void ExpectTag(byte[] der, ref int i, byte tag)
        {
            if (i >= der.Length || der[i] != tag)
                throw new FormatException($"Expected DER tag 0x{tag:X2} at offset {i}.");
            i++;
        }

        private static int ReadLength(byte[] der, ref int i)
        {
            int first = der[i++];
            if ((first & 0x80) == 0) return first; // short form
            int numBytes = first & 0x7F;
            int len = 0;
            for (int b = 0; b < numBytes; b++) len = (len << 8) | der[i++];
            return len;
        }

        private static bool IsExpired(string iso8601)
        {
            if (string.IsNullOrEmpty(iso8601)) return false;
            if (!DateTime.TryParse(iso8601, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal,
                    out var dt))
                return false;
            return dt < DateTime.UtcNow;
        }

        internal static byte[] Base64UrlDecode(string s)
        {
            string padded = s.Replace('-', '+').Replace('_', '/');
            switch (padded.Length % 4)
            {
                case 2: padded += "=="; break;
                case 3: padded += "="; break;
            }
            return Convert.FromBase64String(padded);
        }
    }
}
