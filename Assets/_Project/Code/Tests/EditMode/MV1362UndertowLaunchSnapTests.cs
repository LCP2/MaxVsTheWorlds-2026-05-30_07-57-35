using System.Reflection;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Combat;
using MaxWorlds.Core;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1362 — Undertow's beam (the wavy white/cyan beam drawn by <see cref="MaxWorlds.VFX.UndertowVfx"/>,
    /// tip simulated in <see cref="Undertow"/>) must reach its full length within 6 frames at 60 fps of
    /// the trigger going down, with a short lasso overshoot that settles. Asserts the RESOLVED tip distance
    /// from the muzzle, frame by frame (Tier 2) - never the authored launch constants.
    /// </summary>
    public sealed class MV1362UndertowLaunchSnapTests
    {
        private static readonly BindingFlags NonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;
        private static readonly MethodInfo UndertowAwake = typeof(Undertow).GetMethod("Awake", NonPublicInstance);
        private static readonly MethodInfo UndertowTick = typeof(Undertow).GetMethod("Tick", NonPublicInstance);

        private GameObject _go;

        [TearDown]
        public void TearDown()
        {
            WeaponSystemState.Reset();
            DevMode.Reset();
            if (_go != null) Object.DestroyImmediate(_go);
        }

        [Test]
        public void TipReaches95PercentOfFullLengthByFrame6_AndOvershootSettles_MV1362()
        {
            WeaponSystemState.Reset();
            DevMode.Reset();
            WeaponSystemState.ApplyWorldLoadout(WorldCatalog.IndexOf(WorldIds.Reef)); // UNDERTOW active
            DevMode.Enabled = true;
            DevMode.InfiniteEnergy = true;

            _go = new GameObject("MV1362 Undertow");
            var undertow = _go.AddComponent<Undertow>();
            UndertowAwake.Invoke(undertow, null);
            undertow.SetFiring(true);

            Vector3 origin = _go.transform.position;
            float full = undertow.Range;
            const float dt = 1f / 60f;

            var sb = new StringBuilder("frame:tipDistance(m) ");
            float at6 = 0f, maxDist = 0f, finalDist = 0f;
            for (int frame = 1; frame <= 20; frame++)
            {
                UndertowTick.Invoke(undertow, new object[] { dt });
                float d = Vector3.Distance(undertow.StreamEndPoint, origin);
                sb.Append(frame).Append(':').Append(d.ToString("0.00")).Append(' ');
                if (frame == 6) at6 = d;
                maxDist = Mathf.Max(maxDist, d);
                finalDist = d;
            }
            Debug.Log(sb.ToString());

            Assert.GreaterOrEqual(at6, 0.95f * full,
                $"tip must be >=95% of full length ({full:0.00}m) by frame 6; was {at6:0.00}m. {sb}");
            Assert.LessOrEqual(maxDist, full + 0.3f + 1.5f, $"overshoot along the beam stays small. {sb}");
            Assert.AreEqual(full, finalDist, 1.6f, $"tip stays near full length once settled (free sway only). {sb}");
        }
    }
}
