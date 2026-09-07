using UnityEngine;
using MaxWorlds.Bosses;
using MaxWorlds.Core;
using MaxWorlds.Rendering;

namespace MaxWorlds.VFX
{
    /// <summary>
    /// Sludgequeen's placeholder body (MV-696 §1): a 6x6x4 drum, two side intake hatches, and a 2.5 m
    /// valve wheel on top that spins while she attacks. Deliberately primitives-only greybox — the
    /// ticket's own words, not the fully art-directed language <see cref="BigBermudaRig"/> has grown
    /// into since — a Phase C art pass is what upgrades this, the same way Big Bermuda's own body did.
    ///
    /// Unparented and FOLLOWS the boss each LateUpdate (yaw only), same reasoning as
    /// <see cref="BigBermudaRig.Follow"/>: the boss's own <see cref="CharacterController"/> stays fit to
    /// the hidden placeholder cube, and this rig is purely the thing the player sees.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SludgequeenRig : MonoBehaviour
    {
        /// <summary>Give <paramref name="boss"/> its own rig, bound to it alone — same per-instance
        /// reasoning as <see cref="BigBermudaRig.CreateFor"/> (MV-573): a multi-boss arena must not have
        /// only its first boss grow a body.</summary>
        public static SludgequeenRig CreateFor(SludgequeenBoss boss)
        {
            if (boss == null) return null;
            var rig = new GameObject("SludgequeenRig").AddComponent<SludgequeenRig>();
            rig.Bind(boss);
            return rig;
        }

        private const float DrumWidth = 6f;
        private const float DrumHeight = 4f;
        private const float HatchSize = 1.2f;
        private const float WheelRadius = 1.25f;   // 2.5 m across
        private const float WheelThickness = 0.35f;
        private const float WheelY = DrumHeight + 0.2f;

        /// <summary>Degrees/second the valve wheel spins while merely engaged, vs. flat-out while a
        /// brood wave is actually venting — the spin-up IS the attack tell, same "read the fight off the
        /// moving part" language <see cref="BigBermudaRig"/>'s hatches use for their own tell.</summary>
        private const float IdleSpinSpeed = 90f;
        private const float VentingSpinSpeed = 720f;

        private static readonly Color DrumColor = new Color(0.30f, 0.34f, 0.20f);
        private static readonly Color HatchColor = new Color(0.42f, 0.30f, 0.16f);
        private static readonly Color WheelColor = new Color(0.55f, 0.15f, 0.10f);

        private SludgequeenBoss _boss;
        private Transform _wheel;
        private float _wheelSpinDeg;

        private void Bind(SludgequeenBoss boss)
        {
            _boss = boss;
            gameObject.AddComponent<KeepsOwnMaterial>();

            // The greybox cube goes; its collider stays (the CharacterController is what Max and the
            // Water Blaster actually hit) — same split BigBermudaRig.Bind uses.
            var placeholder = _boss.GetComponent<MeshRenderer>();
            if (placeholder != null) placeholder.enabled = false;

            Build();
            Follow();

            _boss.FitColliderTo(RenderedBoundsRelativeTo(_boss.transform));
        }

        private void Build()
        {
            Part("Drum", PrimitiveType.Cylinder, new Vector3(0f, DrumHeight * 0.5f, 0f),
                 new Vector3(DrumWidth, DrumHeight * 0.5f, DrumWidth), Quaternion.identity, DrumColor);

            Part("HatchL", PrimitiveType.Cube, new Vector3(-DrumWidth * 0.5f, DrumHeight * 0.5f, 0f),
                 new Vector3(HatchSize, HatchSize, HatchSize * 1.6f), Quaternion.identity, HatchColor);
            Part("HatchR", PrimitiveType.Cube, new Vector3(DrumWidth * 0.5f, DrumHeight * 0.5f, 0f),
                 new Vector3(HatchSize, HatchSize, HatchSize * 1.6f), Quaternion.identity, HatchColor);

            // Cylinder primitives stand tall by default; laying it on its side (X 90°) makes the flat
            // face point up, which is what a wheel bolted to the deck looks like from the 72° camera.
            _wheel = Part("ValveWheel", PrimitiveType.Cylinder, new Vector3(0f, WheelY, 0f),
                 new Vector3(WheelRadius * 2f, WheelThickness * 0.5f, WheelRadius * 2f),
                 Quaternion.Euler(90f, 0f, 0f), WheelColor);
        }

        private Transform Part(string name, PrimitiveType shape, Vector3 localPos, Vector3 localScale,
                                Quaternion localRot, Color color)
        {
            var go = GameObject.CreatePrimitive(shape);
            go.name = name;
            Strip(go);

            go.transform.SetParent(transform, worldPositionStays: false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot;
            go.transform.localScale = localScale;

            Material mat = MaterialLibrary.Tinted(SurfaceKind.Metal, color);
            if (mat != null) go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return go.transform;
        }

        /// <summary>DestroyImmediate, not Destroy: this rig is built inside EditMode tests too, not only
        /// at runtime, and Destroy() logs an error there — same reasoning as
        /// <see cref="BigBermudaRig.Strip"/>.</summary>
        private static void Strip(GameObject go)
        {
            var col = go.GetComponent<Collider>();
            if (col != null) DestroyImmediate(col);
        }

        /// <summary>Same axis-aligned-in-another-frame rebuild as <see cref="BigBermudaRig.RenderedBoundsRelativeTo"/>
        /// (MV-613) — what <see cref="SludgequeenBoss.FitColliderTo"/> needs.</summary>
        private Bounds RenderedBoundsRelativeTo(Transform reference)
        {
            var renderers = GetComponentsInChildren<MeshRenderer>();
            Bounds world = renderers.Length > 0 ? renderers[0].bounds : new Bounds(transform.position, Vector3.one);
            for (int i = 1; i < renderers.Length; i++) world.Encapsulate(renderers[i].bounds);

            Vector3 c = world.center, e = world.extents;
            var local = new Bounds(reference.InverseTransformPoint(c), Vector3.zero);
            for (int xi = -1; xi <= 1; xi += 2)
                for (int yi = -1; yi <= 1; yi += 2)
                    for (int zi = -1; zi <= 1; zi += 2)
                        local.Encapsulate(reference.InverseTransformPoint(c + Vector3.Scale(e, new Vector3(xi, yi, zi))));
            return local;
        }

        private void LateUpdate()
        {
            if (_boss == null) return;
            Follow();

            if (!_boss.Engaged || _wheel == null) return;
            float spinSpeed = _boss.IsVenting ? VentingSpinSpeed : IdleSpinSpeed;
            _wheelSpinDeg += spinSpeed * Time.deltaTime;
            _wheel.localRotation = Quaternion.Euler(90f, _wheelSpinDeg, 0f);
        }

        private void Follow()
        {
            Vector3 p = _boss.transform.position;
            var at = new Vector3(p.x, 0f, p.z);
            var facing = Quaternion.Euler(0f, _boss.transform.eulerAngles.y, 0f);
            transform.SetPositionAndRotation(at, facing);
        }
    }
}
