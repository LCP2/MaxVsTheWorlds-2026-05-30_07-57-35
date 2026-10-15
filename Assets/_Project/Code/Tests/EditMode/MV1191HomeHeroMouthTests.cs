using System.IO;
using System.Security.Cryptography;
using NUnit.Framework;
using UnityEngine;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1191 — the home/slot-screen hero banner must carry Max's flat-mouth face (matching the
    /// app icon, MV-1139), not the older downturned-mouth variant. Pins the exact art bytes so a
    /// future resource swap can't silently reintroduce the sad mouth.
    /// </summary>
    public class MV1191HomeHeroMouthTests
    {
        private const string ExpectedSha256 = "524481eef34d2320eaf6cfb0fccf6dade0c9070f70c48f964f254dd706551467";

        [Test]
        public void HomeHeroBanner_IsTheFlatMouthVariant()
        {
            string path = Path.Combine(Application.dataPath, "_Project", "Resources", "Art", "HomeHero.png");
            Assert.IsTrue(File.Exists(path), $"HomeHero.png not found: {path}");

            byte[] bytes = File.ReadAllBytes(path);
            using (SHA256 sha256 = SHA256.Create())
            {
                string hex = System.BitConverter.ToString(sha256.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
                Assert.AreEqual(ExpectedSha256, hex, "HomeHero.png must be the flat-mouth variant (MV-1191)");
            }
        }
    }
}
