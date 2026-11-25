using NUnit.Framework;
using UnityEngine;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1464 - Undertow POWER (p_dmg) 0..8 must be visible. Spawns the real <see cref="UndertowVfx"/> with a
    /// 9 m beam at POWER 0, 4 and 8 and reads RESOLVED renderer state: core width, active strand count,
    /// furthest strand point (as a fraction of beam length) and active arc count.
    /// The level is set through the PowerLevel property by reflection so this file compiles (and fails on
    /// its assertions) against the base commit, where that property does not exist yet.
    /// </summary>
    public sealed class MV1464UndertowPowerLookTests
    {
        private GameObject _go;

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
        }

        [Test]
        public void PowerLevelChangesCoreStrandsReachAndArcs_MV1464()
        {
            const float length = 9f;
            float[] coreExpected = { 0.22f, 0.2475f, 0.275f };
            int[] strandsExpected = { 5, 6, 7 };
            float[] reachExpected = { 0.25f, 0.625f, 1f };
            int[] arcsExpected = { 0, 2, 4 };
            int[] levels = { 0, 4, 8 };

            for (int k = 0; k < levels.Length; k++)
            {
                if (_go != null) Object.DestroyImmediate(_go);
                _go = new GameObject("MV1464 UndertowVfx");
                var vfx = _go.AddComponent<UndertowVfx>();
                var prop = typeof(UndertowVfx).GetProperty("PowerLevel");
                if (prop != null) prop.SetValue(vfx, levels[k]);
                vfx.Init();
                vfx.SetStreaming(true);
                for (int f = 0; f < 3; f++)
                    vfx.UpdateStream(Vector3.zero, Vector3.forward * length, Vector3.forward, 0.016f);

                string tag = $"L{levels[k]}";
                var core = _go.transform.Find("UndertowCore").GetComponent<LineRenderer>();
                Assert.AreEqual(coreExpected[k], core.widthMultiplier, coreExpected[k] * 0.02f, $"{tag} core width");

                int strands = 0;
                float furthest = 0f;
                for (int s = 0; s < 8; s++)
                {
                    Transform t = _go.transform.Find($"UndertowStrand{s}");
                    if (t == null) continue;
                    var line = t.GetComponent<LineRenderer>();
                    if (!line.enabled) continue;
                    strands++;
                    for (int i = 0; i < line.positionCount; i++)
                        furthest = Mathf.Max(furthest, line.GetPosition(i).z / length);
                }
                Assert.AreEqual(strandsExpected[k], strands, $"{tag} active strand count");
                Assert.AreEqual(reachExpected[k], furthest, 0.05f, $"{tag} furthest strand point / beam length");

                int arcs = 0;
                for (int a = 0; a < 8; a++)
                {
                    Transform t = _go.transform.Find($"UndertowArc{a}");
                    if (t != null && t.GetComponent<LineRenderer>().enabled) arcs++;
                }
                Assert.AreEqual(arcsExpected[k], arcs, $"{tag} active arc count");
            }
        }
    }
}
