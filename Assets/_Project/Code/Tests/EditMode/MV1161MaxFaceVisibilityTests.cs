using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.CameraRig;
using MaxWorlds.Player;
using MaxWorlds.VFX;
using MaxWorlds.Weapons;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1161 (Lee, 10 Oct: "it looks like a lot of his face is covered... see if you can adjust the
    /// design so that more of his face is available"). AC1: with Max built at rest, facing the camera's
    /// down-screen direction, and the play camera placed exactly as <c>FixedAngleCameraRig.RestingPose</c>
    /// places it, a line from the camera to each eye white's centre must not hit the real mesh (not a
    /// coarse AABB — see <c>TryRayMeshDistance</c> below) of any hair or brow renderer before reaching
    /// the eye.
    ///
    /// <c>RestingPose</c> sits the camera at -Z from its target (<c>ComputeOffset</c>), so Max must be
    /// yawed 180 degrees — face toward -Z — to look into it; yaw 0 (face toward +Z, his authored eye z)
    /// puts the camera behind his skull instead, which blocked an earlier draft of this test for the
    /// wrong reason (the crown, not the fringe or brow).
    ///
    /// Fails on the commit before this ticket (main 7db54db): with no chin-up pitch, the brow prism and
    /// cap's front overhang sit close enough to the camera's line to the eye to graze it, and the
    /// fringe locks hang down across the brow (<c>MaxHair.FringeHug0</c> = (0,-1,0.25)) — expected to
    /// block at least one eye.
    /// </summary>
    public sealed class MV1161MaxFaceVisibilityTests
    {
        private GameObject _playerGo;
        private GameObject _rigGo;
        private GameObject _camGo;
        private PlayerController _player;
        private MaxRig _rig;

        [SetUp]
        public void SetUp()
        {
            _playerGo = new GameObject("Player-MV1161-Test", typeof(CharacterController));
            var cc = _playerGo.GetComponent<CharacterController>();
            _playerGo.transform.position = new Vector3(0f, cc.height * 0.5f - cc.center.y, 0f);
            _player = _playerGo.AddComponent<PlayerController>();
            InvokeAwake(_player);

            _rigGo = new GameObject("MaxRig-MV1161-Test");
            _rig = _rigGo.AddComponent<MaxRig>();
            InvokeAwake(_rig);
        }

        [TearDown]
        public void TearDown()
        {
            if (_camGo != null) Object.DestroyImmediate(_camGo);
            if (_rigGo != null) Object.DestroyImmediate(_rigGo);
            if (_playerGo != null) Object.DestroyImmediate(_playerGo);
            WeaponSystemState.Reset();
        }

        private static void InvokeAwake(object target) =>
            target.GetType().GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(target, null);

        private static object Invoke(object target, string methodName, params object[] args) =>
            target.GetType().GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(target, args);

        private static T GetField<T>(object target, string name) =>
            (T)target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(target);

        /// <summary>Nearest ray/triangle hit distance against a renderer's real mesh, in world space —
        /// copied from <c>MV1133MaxOutfitV3Tests.TryRayMeshDistance</c>. A single merged renderer's AABB
        /// (<c>Bounds.IntersectRay</c>) is too coarse here: all 25 hair locks share one renderer, so its
        /// box swallows both eyes outright and the test fails identically on base and on the fix.</summary>
        private static bool TryRayMeshDistance(Ray ray, MeshRenderer r, out float distance)
        {
            distance = float.PositiveInfinity;
            var mf = r.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) return false;

            Mesh mesh = mf.sharedMesh;
            Vector3[] verts = mesh.vertices;
            int[] tris = mesh.triangles;
            Transform t = r.transform;
            bool hit = false;

            for (int i = 0; i < tris.Length; i += 3)
            {
                Vector3 a = t.TransformPoint(verts[tris[i]]);
                Vector3 b = t.TransformPoint(verts[tris[i + 1]]);
                Vector3 c = t.TransformPoint(verts[tris[i + 2]]);

                if (RayTriangle(ray, a, b, c, out float d) && d < distance)
                {
                    distance = d;
                    hit = true;
                }
            }
            return hit;
        }

        /// <summary>Standard Moller-Trumbore ray/triangle intersection, both winding directions — copied
        /// from <c>MV1133MaxOutfitV3Tests.RayTriangle</c> (these meshes are smooth/flat shaded for
        /// lighting, not reliably single-sided for a raycast).</summary>
        private static bool RayTriangle(Ray ray, Vector3 a, Vector3 b, Vector3 c, out float t)
        {
            t = 0f;
            Vector3 edge1 = b - a, edge2 = c - a;
            Vector3 h = Vector3.Cross(ray.direction, edge2);
            float det = Vector3.Dot(edge1, h);
            if (Mathf.Abs(det) < 1e-8f) return false;

            float inv = 1f / det;
            Vector3 s = ray.origin - a;
            float u = Vector3.Dot(s, h) * inv;
            if (u < -1e-5f || u > 1f + 1e-5f) return false;

            Vector3 q = Vector3.Cross(s, edge1);
            float v = Vector3.Dot(ray.direction, q) * inv;
            if (v < -1e-5f || u + v > 1f + 1e-5f) return false;

            float dist = Vector3.Dot(edge2, q) * inv;
            if (dist < 1e-5f) return false;

            t = dist;
            return true;
        }

        [Test]
        public void EyesAreClearOfHairAndBrowFromThePlayCamera()
        {
            // Max faces the camera's down-screen direction. FixedAngleCameraRig.ComputeOffset puts the
            // camera at -Z from its target (pitch about X, distance along -cos/+sin), so a Max at yaw 0
            // (face toward +Z, MaxBody.cs's authored eye z) has the camera looking at the BACK of his
            // head, not his face — that is MV1133MaxOutfitV3Tests' own convention (a +Z camera in front
            // of the face), which does not match RestingPose. Yaw 180 turns his face to -Z, into the
            // camera's actual down-screen direction (Apply() sets camera rotation Euler(60,0,0), whose
            // local up/down-screen axis flattens to -Z) — see Lee's triage on MV-1161.
            _playerGo.transform.eulerAngles = new Vector3(0f, 180f, 0f);
            Invoke(_rig, "Follow");

            // Let the hair settle into its idle resting pose before judging it — a fresh, un-simulated
            // frame is not what the camera actually sees. Ticked directly against MaxHairRig.Tick
            // (public) rather than through the private TickHair wrapper, with windStrength pinned to 0
            // and speed01 pinned to 0: TickHair reads the instance's hairWindStrength field (default
            // MaxHair.WindStrengthDefault = 0.55, not zero) and the real Time.time, and with either
            // nonzero the resolved pose depends on wall-clock time the editor happened to hold when the
            // test started — nondeterministic from run to run. With windStrength = 0 and speed01 = 0,
            // MaxHair.ComputePushWorld's gust term and MaxHair.Tick's per-node flutter term both collapse
            // to exactly zero regardless of the time value passed in, so the resting pose this converges
            // to is fully deterministic.
            var hairLocks = GetField<MaxHairRig>(_rig, "_hairLocks");
            const float dt = 1f / 60f;
            for (int i = 0; i < 120; i++)
                hairLocks.Tick(dt, Vector3.forward, 0f, 0f, i * dt, 0f, MaxHair.WindDirWorld);

            // The play camera, placed exactly the way the game places it — never a hand-picked angle.
            _camGo = new GameObject("MV1161 Camera Probe");
            var camRig = _camGo.AddComponent<FixedAngleCameraRig>();
            camRig.RestingPose(_playerGo.transform.position, out Vector3 camPos, out _);

            var head = GetField<Transform>(_rig, "_head");
            var eyeMat = GetField<Material>(_rig, "_eyeMat");
            var hairMat = GetField<Material>(_rig, "_hairMat");
            var hairRibbonMat = GetField<Material>(_rig, "_hairRibbonMat");

            var allRenderers = head.GetComponentsInChildren<MeshRenderer>(true);

            int eyesChecked = 0;
            foreach (var eyeRenderer in allRenderers)
            {
                if (eyeRenderer.sharedMaterial != eyeMat) continue;
                eyesChecked++;

                Vector3 eyeCentre = eyeRenderer.transform.position;
                Vector3 toEye = eyeCentre - camPos;
                float eyeDist = toEye.magnitude;
                var ray = new Ray(camPos, toEye / eyeDist);

                foreach (var occluder in allRenderers)
                {
                    bool isHairOrBrow = occluder.sharedMaterial == hairMat || occluder.sharedMaterial == hairRibbonMat;
                    if (!isHairOrBrow) continue;

                    bool blocked = TryRayMeshDistance(ray, occluder, out float hitDist) && hitDist < eyeDist - 1e-3f;
                    Assert.IsFalse(blocked,
                        $"the camera's line to {eyeRenderer.name} at {eyeCentre} is blocked by " +
                        $"'{occluder.name}' (nearest mesh hit at {hitDist:0.000} m, eye at {eyeDist:0.000} m) " +
                        "before it reaches the eye.");
                }
            }

            Assert.That(eyesChecked, Is.EqualTo(2), $"expected 2 eye renderers under Head, found {eyesChecked}.");
        }
    }
}
