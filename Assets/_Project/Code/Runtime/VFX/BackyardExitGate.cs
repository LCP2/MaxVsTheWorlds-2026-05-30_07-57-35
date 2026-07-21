using UnityEngine;
using UnityEngine.Rendering;
using MaxWorlds.Arena;
using MaxWorlds.Bosses;
using MaxWorlds.Core;
using MaxWorlds.Rendering;
using MaxWorlds.UI;

namespace MaxWorlds.VFX
{
    /// <summary>
    /// The way out (YT-153): a big garden gate in the far wall of the boss arena that swings open when
    /// the boss dies, spilling light from the path to the next area.
    ///
    /// It is the payoff to the fight the same way the blow-up is — the wall you have been penned
    /// against the whole fight breaks open the instant the machine does, and the yard stops being a
    /// box. Built shut, set into the arena's back wall (<see cref="BackyardPathLayout.ArenaEndZ"/>),
    /// and it reacts to the one signal the boss already raises: <see cref="HudSignals.BossDefeated"/>.
    ///
    /// REVEAL, NOT TRAVERSAL. This is art. The arena's back wall keeps its collider — actually walking
    /// through to a next zone is the gameplay stream's, and there is no next zone in the Phase B slice
    /// yet — so the gate promises the path without pretending to be it: the leaves swing, warm light
    /// floods the doorway, a lit strip runs out across the threshold. Whenever a next zone is built,
    /// this is where it plugs in.
    ///
    /// UNSCALED time: the run ends and the game freezes the frame the boss dies (see
    /// <see cref="BossSpectacle"/>), so the swing has to run on realtime or it never moves. Its screen
    /// time is still bounded by how long the result card takes to land — the same gameplay-owned timing
    /// BossSpectacle flags — so this is built to read in well under a second.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BackyardExitGate : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (FindFirstObjectByType<BackyardExitGate>() != null) return;
            if (FindFirstObjectByType<BackyardPath>() == null) return;      // not the Backyard
            if (FindFirstObjectByType<BigBermudaBoss>() == null) return;    // no boss, no exit beat
            new GameObject("BackyardExitGate").AddComponent<BackyardExitGate>();
        }

        private static readonly Color GateWood = new Color(0.30f, 0.21f, 0.13f);
        private static readonly Color GateIron = new Color(0.16f, 0.16f, 0.18f);
        private static readonly Color BeyondGlow = new Color(1f, 0.86f, 0.55f);   // golden-hour light past the gate

        private const float OpenAngle = 106f;    // how far each leaf swings
        private const float OpenTime = 0.7f;      // realtime seconds — inside the result-card window

        private Transform _leftHinge, _rightHinge;
        private MeshRenderer _beyond;             // the light in the doorway, faded in as it opens
        private MeshRenderer _threshold;          // a lit strip out across the sill
        private MaterialPropertyBlock _mpb;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private bool _opening;
        private float _t;

        private void Awake()
        {
            var path = FindFirstObjectByType<BackyardPath>();
            if (path == null) return;

            BackyardPathLayout L = path.Layout;
            gameObject.AddComponent<KeepsOwnMaterial>();
            _mpb = new MaterialPropertyBlock();

            float half = L.GateHalfWidth;                 // half the doorway
            float wallH = L.WallHeight;
            float gateZ = L.ArenaEndZ - 0.3f;             // just inside the back wall, facing the arena
            float leafH = wallH * 0.92f;
            float leafW = half;                           // two leaves fill the 2*half opening

            Material wood = MaterialLibrary.Tinted(SurfaceKind.Wood, GateWood);
            Material iron = MaterialLibrary.Tinted(SurfaceKind.Metal, GateIron);

            // Posts either side of the opening, and a lintel across the top — the frame the leaves hang in.
            Block("PostL", new Vector3(-half, wallH * 0.5f, gateZ), new Vector3(0.36f, wallH, 0.36f), wood);
            Block("PostR", new Vector3(half, wallH * 0.5f, gateZ), new Vector3(0.36f, wallH, 0.36f), wood);
            Block("Lintel", new Vector3(0f, wallH + 0.12f, gateZ), new Vector3(half * 2f + 0.8f, 0.42f, 0.42f), wood);

            // Two leaves on hinges at the posts. Each hinge is a pivot AT its post; the leaf hangs inward
            // from it, so rotating the pivot about Y swings the whole leaf from shut (pointing at the
            // centre) to open (swung back into the arena), where the doorway clears.
            _leftHinge = Hinge("HingeL", new Vector3(-half, 0f, gateZ));
            Leaf(_leftHinge, +1f, leafW, leafH, wood, iron);
            _rightHinge = Hinge("HingeR", new Vector3(half, 0f, gateZ));
            Leaf(_rightHinge, -1f, leafW, leafH, wood, iron);

            // The light beyond — an additive panel filling the doorway, just behind the shut leaves so
            // they hide it until they swing. Faded up as the gate opens.
            _beyond = Glow("Beyond",
                new Vector3(0f, leafH * 0.5f, gateZ + 0.06f),
                Quaternion.Euler(0f, 180f, 0f),                    // face the arena/camera at -Z
                new Vector3(half * 2f - 0.2f, leafH, 1f));

            // A lit strip out across the sill — the path, drawn on the ground so the eye follows it out.
            _threshold = Glow("Threshold",
                new Vector3(0f, 0.03f, gateZ - 2.4f),
                Quaternion.Euler(90f, 0f, 0f),                     // lie flat on the lawn
                new Vector3(half * 2f - 0.4f, 5.5f, 1f));

            SetGlow(0f);   // dark until the boss falls
        }

        private void OnEnable() => HudSignals.BossDefeated += OnDefeated;
        private void OnDisable() => HudSignals.BossDefeated -= OnDefeated;

        private void OnDefeated() { _opening = true; _t = 0f; }

        private void Update()
        {
            if (!_opening) return;

            _t += Time.unscaledDeltaTime;
            float k = OpenTime > 0f ? Mathf.Clamp01(_t / OpenTime) : 1f;
            // Ease-out: the leaves fling open and settle, rather than crawling at a constant rate.
            float e = 1f - (1f - k) * (1f - k);
            float angle = OpenAngle * e;

            if (_leftHinge != null) _leftHinge.localRotation = Quaternion.Euler(0f, angle, 0f);
            if (_rightHinge != null) _rightHinge.localRotation = Quaternion.Euler(0f, -angle, 0f);

            SetGlow(e);

            if (k >= 1f) _opening = false;
        }

        // ---- build helpers ----

        private void Block(string name, Vector3 pos, Vector3 scale, Material mat)
        {
            var go = MakeCube(name, mat);
            go.transform.SetParent(transform, worldPositionStays: false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
        }

        private Transform Hinge(string name, Vector3 pos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, worldPositionStays: false);
            go.transform.localPosition = pos;
            return go.transform;
        }

        /// <summary>One gate leaf, hanging inward from a hinge. <paramref name="side"/> is +1 for the
        /// left hinge (leaf reaches in +X to the centre) and -1 for the right.</summary>
        private void Leaf(Transform hinge, float side, float w, float h, Material wood, Material iron)
        {
            var leaf = MakeCube("Leaf", wood);
            leaf.transform.SetParent(hinge, worldPositionStays: false);
            leaf.transform.localPosition = new Vector3(side * w * 0.5f, h * 0.5f, 0f);
            leaf.transform.localScale = new Vector3(w, h, 0.14f);

            // Two iron bands across the planks — the bit of hardware that reads as "heavy gate" not
            // "billboard". Children of the leaf, scaled back out of the leaf's own local scale.
            for (int i = 0; i < 2; i++)
            {
                var band = MakeCube("Band", iron);
                band.transform.SetParent(leaf.transform, worldPositionStays: false);
                band.transform.localPosition = new Vector3(0f, i == 0 ? -0.28f : 0.28f, -0.55f);
                band.transform.localScale = new Vector3(1.02f, 0.14f / h, 1.6f);
            }
        }

        private MeshRenderer Glow(string name, Vector3 pos, Quaternion rot, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name;
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
            go.transform.SetParent(transform, worldPositionStays: false);
            go.transform.localPosition = pos;
            go.transform.localRotation = rot;
            go.transform.localScale = scale;

            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = VfxMaterials.Additive(VfxMaterials.Glow());
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            return r;
        }

        private void SetGlow(float amount)
        {
            Paint(_beyond, BeyondGlow * amount);
            Paint(_threshold, BeyondGlow * (amount * 0.7f));   // the sill strip a touch softer than the doorway
        }

        private void Paint(MeshRenderer r, Color c)
        {
            if (r == null) return;
            c.a = 1f;
            r.GetPropertyBlock(_mpb);
            _mpb.SetColor(BaseColorId, c);
            r.SetPropertyBlock(_mpb);
        }

        private GameObject MakeCube(string name, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);                 // reveal only — the real wall keeps its collider
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            go.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            return go;
        }
    }
}
