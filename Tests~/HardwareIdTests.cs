using System;
using System.IO;
using PermitCore;
using Xunit;

namespace PermitCore.UnityTests
{
    public class HardwareIdTests
    {
        [Fact]
        public void HashRawId_IsDeterministicSha256Hex()
        {
            string h1 = HardwareId.HashRawId("same-input");
            string h2 = HardwareId.HashRawId("same-input");
            Assert.Equal(h1, h2);
            Assert.Equal(64, h1.Length);
            Assert.Matches("^[0-9a-f]{64}$", h1);
        }

        [Fact]
        public void HashRawId_DifferentInputsProduceDifferentHashes()
        {
            Assert.NotEqual(HardwareId.HashRawId("a"), HardwareId.HashRawId("b"));
        }

        [Fact]
        public void GetHardwareId_IsStableAcrossCallsInSameDirectory()
        {
            string dir = Path.Combine(Path.GetTempPath(), "permitcore_unity_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string id1 = HardwareId.GetHardwareId(dir);
                string id2 = HardwareId.GetHardwareId(dir);
                Assert.Equal(id1, id2);
                Assert.Equal(64, id1.Length);
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void GetHardwareId_DiffersAcrossDirectories()
        {
            string dirA = Path.Combine(Path.GetTempPath(), "permitcore_unity_test_a_" + Guid.NewGuid().ToString("N"));
            string dirB = Path.Combine(Path.GetTempPath(), "permitcore_unity_test_b_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dirA);
            Directory.CreateDirectory(dirB);
            try
            {
                Assert.NotEqual(HardwareId.GetHardwareId(dirA), HardwareId.GetHardwareId(dirB));
            }
            finally
            {
                Directory.Delete(dirA, true);
                Directory.Delete(dirB, true);
            }
        }
    }
}
