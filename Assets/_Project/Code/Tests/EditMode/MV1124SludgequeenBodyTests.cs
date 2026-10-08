using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Bosses;
using MaxWorlds.CameraRig;
using MaxWorlds.Core;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1124 (MV-465 Rule 1, one new test carrying every acceptance sub-check): the Sludgequeen's
    /// generated body must replace MV-699's plain drum with a rig that actually reads the robot design
    /// rules — six jointed legs, an eye the fixed camera can actually see, a hard rust/teal housing
    /// split, a health-reactive alarm lamp, a sane collider fitted to the housing (not the legs), and
    /// none of the retired valve-wheel/drip parts.
    ///
    /// Fails to compile (CS1061/CS0117) on 08c4e51, the base commit this ticket starts from:
    /// <c>SludgequeenRig</c> there exposes no <c>LegHip</c>/<c>LegKnee</c>/<c>LegFoot</c>,
    /// <c>EyeLens</c>/<c>SludgeSurface</c>/<c>AlarmLamp</c>, and <c>SludgequeenRig.LegCount</c> does not
    /// exist — same "compile failure is the proof" shape <c>MV699SludgequeenRigTests</c> already uses
    /// for its own base commit.
    ///
    /// EditMode only, reflection-driven for <c>Awake</c> (repo convention — a plain MonoBehaviour never
    /// runs it outside Play mode), same idiom <c>MV699SludgequeenRigTests</c> uses for the same boss
    /// type. The camera-ray checks (1b) use the REAL <see cref="FixedAngleCameraRig"/> entry point
    /// (its own serialized pitch/distance defaults, not a hard-coded degree number) so the test tracks
    /// whatever the shipped camera pose actually is, rather than a stale angle quoted in prose
    /// elsewhere in the codebase.
    /// </summary>
    public sealed class MV1124SludgequeenBodyTests
    {
        private GameObject _bossGo;
        private SludgequeenRig _rig;
        private GameObject _cameraGo;

        [SetUp]
        public void SetUp() => SludgequeenBoss.ResetRegistry();

        [TearDown]
        public void TearDown()
        {
            if (_rig != null) Object.DestroyImmediate(_rig.gameObject);
            if (_bossGo != null) Object.DestroyImmediate(_bossGo);
            if (_cameraGo != null) Object.DestroyImmediate(_cameraGo);
            SludgequeenBoss.ResetRegistry();
            BossCensus.Reset();
        }

        [Test]
        public void CreateFor_BuildsTheMV1124Body()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var stray = go.GetComponent<BoxCollider>();
            if (stray != null) Object.DestroyImmediate(stray);
            go.transform.localScale = Vector3.one * 6f;   // the authored body width the ticket's own ACs are stated against
            var boss = go.AddComponent<SludgequeenBoss>();
            _bossGo = go;

            // EditMode never calls Awake on a plain MonoBehaviour — invoke it explicitly, same idiom
            // MV699SludgequeenRigTests already uses for this same boss type.
            typeof(SludgequeenBoss).GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(boss, null);
            boss.SetArenaBounds(new Rect(0f, 0f, 44f, 44f));

            _rig = SludgequeenRig.CreateFor(boss);
            Assert.IsNotNull(_rig, "CreateFor must build a rig for a live boss");

            // OnEnable isn't reliably invoked for AddComponent outside Play mode either (same note
            // MV625CrossAreaBossDeathTests/MV613BossRigScaleTests carry for Awake) -- drive it directly
            // so the rig actually subscribes to HudSignals.BossHealthChanged the way it does for real.
            typeof(SludgequeenRig).GetMethod("OnEnable", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(_rig, null);

            // --- AC1a: six leg roots, each with a hip (the root itself), a knee and a foot transform.
            for (int i = 0; i < SludgequeenRig.LegCount; i++)
            {
                Assert.IsNotNull(_rig.LegHip(i), $"leg {i} must expose a hip transform");
                Assert.IsNotNull(_rig.LegKnee(i), $"leg {i} must expose a knee transform");
                Assert.IsNotNull(_rig.LegFoot(i), $"leg {i} must expose a foot transform");
            }
            Assert.AreEqual(6, SludgequeenRig.LegCount, "the ticket's own leg count");

            var allRenderers = _rig.GetComponentsInChildren<MeshRenderer>();
            Assert.Greater(allRenderers.Length, 0, "the rig must actually render something");

            // --- AC1c: the rendered bounds are 7..8.5 m wide.
            Bounds worldBounds = allRenderers[0].bounds;
            for (int i = 1; i < allRenderers.Length; i++) worldBounds.Encapsulate(allRenderers[i].bounds);
            float width = Mathf.Max(worldBounds.size.x, worldBounds.size.z);
            Assert.GreaterOrEqual(width, 7f, $"rendered bounds must be at least 7 m wide, measured {width:F2} m");
            Assert.LessOrEqual(width, 8.5f, $"rendered bounds must be at most 8.5 m wide, measured {width:F2} m");

            // --- AC1d: the housing uses two materials resolving to the rust and teal above.
            var rust = new Color(0.66f, 0.34f, 0.15f);
            var teal = new Color(0.25f, 0.46f, 0.48f);
            bool sawRust = false, sawTeal = false;
            foreach (var r in allRenderers)
            {
                if (!r.name.StartsWith("Housing")) continue;
                Color c = r.sharedMaterial.HasProperty("_BaseColor") ? r.sharedMaterial.GetColor("_BaseColor") : r.sharedMaterial.color;
                if (ColorsClose(c, rust, 0.03f)) sawRust = true;
                if (ColorsClose(c, teal, 0.03f)) sawTeal = true;
            }
            Assert.IsTrue(sawRust, "the housing must carry a material resolving to the authored rust colour");
            Assert.IsTrue(sawTeal, "the housing must carry a material resolving to the authored teal colour");

            // --- AC1e: no part is named for a valve wheel or a drip.
            foreach (var r in allRenderers)
            {
                StringAssert.DoesNotContain("Valve", r.name, "no part may be named for the retired valve wheel");
                StringAssert.DoesNotContain("Drip", r.name, "no part may be named for the retired drip");
            }

            // --- AC1g: the collider's resolved radius is 1.9..2.6 m (fitted to the housing and hatches,
            // not the legs -- a radius in this range is only reachable if the legs, which reach out to
            // 3.85 m, were excluded from the fit). CharacterController.radius is a LOCAL-space value;
            // the boss's own transform carries the authored-width scale, so convert to world space the
            // same way SludgequeenBoss's own (private) WorldRadius does before comparing against the
            // ticket's metres.
            var cc = boss.GetComponent<CharacterController>();
            Vector3 lossyScale = boss.transform.lossyScale;
            float worldRadius = cc.radius * Mathf.Max(Mathf.Abs(lossyScale.x), Mathf.Abs(lossyScale.z));
            Assert.GreaterOrEqual(worldRadius, 1.9f, $"collider radius must be at least 1.9 m, resolved {worldRadius:F2} m");
            Assert.LessOrEqual(worldRadius, 2.6f, $"collider radius must be at most 2.6 m, resolved {worldRadius:F2} m");

            // --- AC1f: the alarm lamp is dark red above half health, bright red at/below it.
            Assert.IsNotNull(_rig.AlarmLamp, "the alarm lamp must exist");
            Color aboveHalf = ResolvedLensColor(_rig.AlarmLamp);

            // First hit wakes her (Dormant -> Fight) without damaging; the second actually lands.
            boss.TakeDamage(new DamageInfo(1f, boss.transform.position, Vector3.up, Team.Player));
            float toWellBelowHalf = SludgequeenTuning.Health * 0.6f;   // leaves ~40%, comfortably <= the 50% threshold
            boss.TakeDamage(new DamageInfo(toWellBelowHalf, boss.transform.position, Vector3.up, Team.Player));
            Color atOrBelowHalf = ResolvedLensColor(_rig.AlarmLamp);

            Assert.AreNotEqual(aboveHalf, atOrBelowHalf, "the alarm lamp must change colour once health drops to/below half");
            var brightRed = new Color32(0xFF, 0x2A, 0x1A, 0xFF);
            Assert.IsTrue(ColorsClose(atOrBelowHalf, brightRed, 0.03f),
                $"the alarm lamp must resolve to #FF2A1A at/below half health, resolved {atOrBelowHalf}");

            // --- AC1b: from the real gameplay camera pose, a ray to the eye lens hits the lens first,
            // and a ray to the sludge surface hits it first.
            var camGo = new GameObject("MV1124 Camera Probe");
            var camRig = camGo.AddComponent<FixedAngleCameraRig>();
            _cameraGo = camGo;
            camRig.RestingPose(boss.transform.position, out Vector3 camPos, out _);

            AssertRayHitsRendererFirst(camPos, _rig.EyeLens, allRenderers, "eye lens");
            AssertRayHitsRendererFirst(camPos, _rig.SludgeSurface, allRenderers, "sludge surface");
        }

        private static Color ResolvedLensColor(MeshRenderer r)
        {
            var mpb = new MaterialPropertyBlock();
            r.GetPropertyBlock(mpb);
            return mpb.GetColor(Shader.PropertyToID("_BaseColor"));
        }

        private static bool ColorsClose(Color a, Color b, float tolerance) =>
            Mathf.Abs(a.r - b.r) <= tolerance && Mathf.Abs(a.g - b.g) <= tolerance && Mathf.Abs(a.b - b.b) <= tolerance;

        /// <summary>Casts a ray from <paramref name="from"/> to <paramref name="target"/>'s own bounds
        /// centre and fails if any OTHER renderer's axis-aligned bounds intersects that ray at a
        /// smaller distance -- i.e. if anything else in the model occludes it first. Geometric, not
        /// <c>Physics.Raycast</c>: a character part carries no collider by design
        /// (<c>CharacterPart</c>'s own "no collider, ever" rule), so a renderer's resolved world bounds
        /// is the actual value available to test visibility against.</summary>
        private static void AssertRayHitsRendererFirst(Vector3 from, MeshRenderer target, MeshRenderer[] all, string label)
        {
            Assert.IsNotNull(target, $"{label} renderer must exist");
            Vector3 to = target.bounds.center;
            Vector3 offset = to - from;
            float targetDistance = offset.magnitude;
            Vector3 dir = offset / targetDistance;

            foreach (var r in all)
            {
                if (r == target) continue;
                if (RayEntersBounds(from, dir, r.bounds, out float t) && t < targetDistance - 0.01f)
                    Assert.Fail($"{label}: '{r.name}' occludes the camera ray at t={t:F2} m, before the {label} itself at t={targetDistance:F2} m");
            }
        }

        /// <summary>Standard slab-method ray/AABB intersection. Returns the entry distance along the
        /// normalized <paramref name="dir"/> from <paramref name="origin"/>, or false if the ray misses
        /// the box entirely.</summary>
        private static bool RayEntersBounds(Vector3 origin, Vector3 dir, Bounds b, out float tEnter)
        {
            Vector3 min = b.min, max = b.max;
            float tMin = float.NegativeInfinity, tMax = float.PositiveInfinity;

            for (int axis = 0; axis < 3; axis++)
            {
                float o = origin[axis], d = dir[axis], lo = min[axis], hi = max[axis];
                if (Mathf.Abs(d) < 1e-8f)
                {
                    if (o < lo || o > hi) { tEnter = 0f; return false; }
                    continue;
                }
                float t1 = (lo - o) / d;
                float t2 = (hi - o) / d;
                if (t1 > t2) { float tmp = t1; t1 = t2; t2 = tmp; }
                tMin = Mathf.Max(tMin, t1);
                tMax = Mathf.Min(tMax, t2);
                if (tMin > tMax) { tEnter = 0f; return false; }
            }

            tEnter = Mathf.Max(tMin, 0f);
            return tMax >= 0f;
        }
    }
}
