using NUnit.Framework;
using UnityEngine;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1458 — the Undertow beam must finish in a slight point, not a flat cut end. Spawns the real
    /// <see cref="UndertowVfx"/> with a 9 m beam (primary and secondary) and reads each core/sheath/strand
    /// line's RESOLVED width (width curve x widthMultiplier) at the tip and 1 m back from it.
    /// </summary>
    public sealed class MV1458UndertowTipTaperTests
    {
        private GameObject _go;

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
        }

        private static float Resolved(LineRenderer line, float t) => line.widthCurve.Evaluate(t) * line.widthMultiplier;

        [Test]
        public void BeamTipTapersToAPoint_MV1458()
        {
            _go = new GameObject("MV1458 UndertowVfx");
            var vfx = _go.AddComponent<UndertowVfx>();
            vfx.Init();
            vfx.SetStreaming(true);
            vfx.SetSecondaryStreaming(0, true);

            const float length = 9f;
            Vector3 muzzle = Vector3.zero;
            Vector3 tip = Vector3.forward * length;
            vfx.UpdateStream(muzzle, tip, Vector3.forward, 0.016f);
            vfx.UpdateSecondaryStream(0, muzzle, tip, Vector3.forward, 0.016f);

            foreach (string suffix in new[] { "", "Beam1" })
            {
                var names = new System.Collections.Generic.List<string> { $"UndertowCore{suffix}", $"UndertowSheath{suffix}" };
                for (int s = 0; s < 5; s++) names.Add($"UndertowStrand{suffix}{s}");

                foreach (string n in names)
                {
                    Transform t = _go.transform.Find(n);
                    Assert.IsNotNull(t, $"{n} must exist");
                    var line = t.GetComponent<LineRenderer>();
                    float wTip = Resolved(line, 1f);
                    float wBack = Resolved(line, 1f - 1f / length);
                    Assert.Greater(wBack, 0f, $"{n} width 1 m back must be positive");
                    Assert.LessOrEqual(wTip, 0.2f * wBack,
                        $"{n} tip width {wTip:0.0000} must be <= 20% of the width 1 m back ({wBack:0.0000})");
                }
            }
        }
    }
}
