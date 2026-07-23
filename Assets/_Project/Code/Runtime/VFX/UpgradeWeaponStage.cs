using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using MaxWorlds.Core;
using MaxWorlds.Rendering;
using MaxWorlds.Upgrades;

namespace MaxWorlds.VFX
{
    /// <summary>
    /// The hero weapon on the upgrade screen (YT-140) — the garden hose "in all its glory," rendered
    /// live in 3D into a texture the screen shows on its right half.
    ///
    /// YT-132's screen revealed the part as a flat blue square flying to a green ellipse. This replaces
    /// that with the REAL thing: a base hose sprayer with every part Max has already installed bolted
    /// on, and the part he just picked up flying in and locking to its mount — the actual YT-134 props,
    /// not stand-ins. It reads as "look what your weapon has become," which is the reward the moment is
    /// meant to be.
    ///
    /// It builds a tiny stage far below the world (nothing else is down there) and points a dedicated
    /// orthographic camera at it, rendering to a <see cref="RenderTexture"/>. The scene's own directional
    /// sun reaches it (a directional light has no position), so the low-poly parts read with form without
    /// a second light double-lighting the actual game. The camera runs only while the screen is up.
    /// Everything animates on UNSCALED time — the game is paused under the screen.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UpgradeWeaponStage : MonoBehaviour
    {
        private const float StageY = -1000f;      // far below the world; the camera's tight frustum sees only this
        private const int TexSize = 640;

        // Attached parts render at HALF size so they read as components bolted onto the sprayer, not as
        // equal-sized blocks piled next to it (the first cut looked like a parts bin).
        private const float PartScale = 0.5f;

        // Where each kind of part bolts onto the sprayer (local to the weapon root, muzzle pointing +X).
        // Kept CLOSE so each hugs the body and reads as bolted on, not floating beside it: nozzle at the
        // muzzle, harness + Hydro stacked on the back, engine slung under the barrel.
        private static readonly Dictionary<PartKind, Vector3> Mounts = new Dictionary<PartKind, Vector3>
        {
            { PartKind.BeamNozzle, new Vector3(0.44f, 0.0f, 0f) },
            { PartKind.PowerNozzle, new Vector3(0.46f, 0.0f, 0f) },
            { PartKind.RangeExtender, new Vector3(0.48f, 0.0f, 0f) },
            { PartKind.WideBore, new Vector3(0.50f, 0.0f, 0f) },
            { PartKind.AugmentationHarness, new Vector3(-0.26f, 0.0f, 0f) },
            { PartKind.AccelerationEngine, new Vector3(0.05f, -0.19f, 0f) },
            { PartKind.Hydro, new Vector3(-0.24f, 0.24f, 0f) },
        };

        /// <summary>How each part sits on its mount. The nozzles lie ALONG the barrel (their authored
        /// up-axis rotated to +X) so they extend the muzzle instead of standing up off it; the rest sit
        /// upright as authored.</summary>
        private static Quaternion RotationFor(PartKind kind)
        {
            switch (kind)
            {
                case PartKind.BeamNozzle:
                case PartKind.PowerNozzle:
                case PartKind.RangeExtender:
                case PartKind.WideBore: return Quaternion.Euler(0f, 0f, -90f);
                default: return Quaternion.identity;
            }
        }

        private Camera _cam;
        private RenderTexture _rt;
        private Transform _weaponRoot;            // the sprayer + bolted-on parts, slowly turning
        private Transform _newPart;               // the part flying on this reveal
        private PartKind _newPartKind;
        private Vector3 _newPartMount, _newPartStart;
        private readonly List<GameObject> _attached = new List<GameObject>();

        /// <summary>The live weapon render, for the screen's RawImage.</summary>
        public RenderTexture Texture => _rt;

        /// <summary>Whether a part is currently flying on, and where it sits — for a test to prove the
        /// fit animation actually travels it onto its mount.</summary>
        public bool HasNewPart => _newPart != null;
        public Vector3 NewPartLocalPosition => _newPart != null ? _newPart.localPosition : Vector3.zero;

        public static UpgradeWeaponStage Create(Transform parent)
        {
            var go = new GameObject("UpgradeWeaponStage");
            go.transform.SetParent(parent, false);
            var stage = go.AddComponent<UpgradeWeaponStage>();
            stage.Build();
            return stage;
        }

        private void Build()
        {
            _rt = new RenderTexture(TexSize, TexSize, 24, RenderTextureFormat.ARGB32)
            {
                name = "UpgradeWeaponRT",
                antiAliasing = 4,
                hideFlags = HideFlags.HideAndDontSave,
            };
            _rt.Create();

            var pivot = new GameObject("StagePivot").transform;
            pivot.SetParent(transform, false);
            pivot.position = new Vector3(0f, StageY, 0f);

            _weaponRoot = new GameObject("Weapon").transform;
            _weaponRoot.SetParent(pivot, false);
            BuildSprayer(_weaponRoot);

            // The camera: a 3/4 hero angle onto the weapon, orthographic so the read is clean and stable.
            var camGo = new GameObject("StageCam");
            camGo.transform.SetParent(pivot, false);
            _cam = camGo.AddComponent<Camera>();
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0f, 0f, 0f, 0f);   // transparent — the panel shows behind it
            _cam.orthographic = true;
            _cam.orthographicSize = 0.72f;   // tight on the assembled sprayer so it fills the frame as a hero
            _cam.nearClipPlane = 0.05f;
            _cam.farClipPlane = 8f;
            _cam.targetTexture = _rt;
            _cam.enabled = false;   // only while the screen is open
            var lookFrom = Quaternion.Euler(18f, -32f, 0f) * new Vector3(0f, 0f, -3f);
            camGo.transform.position = pivot.position + lookFrom;
            camGo.transform.rotation = Quaternion.LookRotation(-lookFrom, Vector3.up);
        }

        /// <summary>The base garden-hose sprayer — a pistol-grip nozzle with a hose curling in the back.
        /// Laid along +X so the 3/4 camera sees its flank. This is the weapon everything bolts onto.</summary>
        private void BuildSprayer(Transform root)
        {
            Material green = MaterialLibrary.Tinted(SurfaceKind.Metal, new Color(0.24f, 0.55f, 0.30f));
            Material brass = MaterialLibrary.Tinted(SurfaceKind.Metal, new Color(0.72f, 0.55f, 0.22f));
            Material steel = MaterialLibrary.Tinted(SurfaceKind.Metal, new Color(0.55f, 0.58f, 0.63f));

            root.gameObject.AddComponent<KeepsOwnMaterial>();

            Part(root, "Barrel", PrimitiveType.Cylinder, new Vector3(0.12f, 0f, 0f),
                 new Vector3(0.14f, 0.28f, 0.14f), Quaternion.Euler(0f, 0f, 90f), green);
            Part(root, "Muzzle", PrimitiveType.Cylinder, new Vector3(0.36f, 0f, 0f),
                 new Vector3(0.16f, 0.05f, 0.16f), Quaternion.Euler(0f, 0f, 90f), brass);
            Part(root, "Grip", PrimitiveType.Cube, new Vector3(-0.05f, -0.2f, 0f),
                 new Vector3(0.1f, 0.3f, 0.12f), Quaternion.Euler(0f, 0f, 16f), steel);
            Part(root, "Trigger", PrimitiveType.Cube, new Vector3(0.02f, -0.08f, 0f),
                 new Vector3(0.04f, 0.12f, 0.04f), Quaternion.Euler(0f, 0f, -20f), brass);
            // The hose curling in at the back.
            Part(root, "HoseIn", PrimitiveType.Cylinder, new Vector3(-0.24f, -0.06f, 0f),
                 new Vector3(0.09f, 0.12f, 0.09f), Quaternion.Euler(0f, 0f, 50f), green);
            Part(root, "HoseCoil", PrimitiveType.Cylinder, new Vector3(-0.34f, -0.16f, 0f),
                 new Vector3(0.2f, 0.03f, 0.2f), Quaternion.Euler(70f, 0f, 0f), green);
        }

        /// <summary>Set the weapon up for a reveal: bolt on everything already installed, and stand the
        /// newly-picked-up part off to the side ready to fly in.</summary>
        public void Show(PartKind newPart)
        {
            foreach (var go in _attached) if (go != null) Destroy(go);
            _attached.Clear();
            _newPart = null;

            // Everything already on the weapon (the new one installs on Continue, so it is NOT yet here).
            foreach (var kind in Mounts.Keys)
            {
                if (kind == newPart || !UpgradeState.IsInstalled(kind)) continue;
                Attach(kind, Mounts[kind]);
            }

            // The new part, staged up-and-out from its mount, to glide in.
            _newPartKind = newPart;
            _newPartMount = Mounts.TryGetValue(newPart, out var m) ? m : new Vector3(0.44f, 0f, 0f);
            _newPartStart = _newPartMount + new Vector3(0.24f, 0.4f, 0.14f);
            var go2 = WeaponPartArt.Build(KeyFor(newPart), _weaponRoot);
            if (go2 != null)
            {
                _newPart = go2.transform;
                _newPart.localPosition = _newPartStart;
                _newPart.localScale = Vector3.one * PartScale;
                _newPart.localRotation = RotationFor(newPart);
                _attached.Add(go2);
            }

            _cam.enabled = true;
        }

        /// <summary>Set the weapon up for a status view (YT-178): bolt on everything already installed,
        /// with nothing flying in. Used when the WEAPONS button opens the weapons area on demand rather
        /// than off a fresh pickup, so there is no new part to reveal.</summary>
        public void ShowInstalled()
        {
            foreach (var go in _attached) if (go != null) Destroy(go);
            _attached.Clear();
            _newPart = null;

            foreach (var kind in Mounts.Keys)
                if (UpgradeState.IsInstalled(kind)) Attach(kind, Mounts[kind]);

            _cam.enabled = true;
        }

        /// <summary>Animate the reveal: the new part glides from its staged spot down onto its mount,
        /// and the whole weapon turns slowly so it reads as a hero object. Driven by the screen's clock.</summary>
        public void Tick(float unscaledT, float fitStart, float fitTime)
        {
            if (_weaponRoot != null)
                _weaponRoot.localRotation = Quaternion.Euler(0f, 14f * Mathf.Sin(unscaledT * 0.7f), 0f);

            if (_newPart == null) return;
            float fit = Mathf.Clamp01((unscaledT - fitStart) / Mathf.Max(0.01f, fitTime));
            float glide = fit * fit * (3f - 2f * fit);   // smoothstep
            _newPart.localPosition = Vector3.Lerp(_newPartStart, _newPartMount, glide);
            // A touch of spin as it comes in, easing to its seated orientation on the mount.
            _newPart.localRotation = RotationFor(_newPartKind) * Quaternion.Euler(0f, 220f * (1f - glide), 0f);
        }

        public void Hide()
        {
            if (_cam != null) _cam.enabled = false;
        }

        private void Attach(PartKind kind, Vector3 at)
        {
            var go = WeaponPartArt.Build(KeyFor(kind), _weaponRoot);
            if (go == null) return;
            go.transform.localPosition = at;
            go.transform.localScale = Vector3.one * PartScale;
            go.transform.localRotation = RotationFor(kind);
            _attached.Add(go);
        }

        private static string KeyFor(PartKind kind)
        {
            switch (kind)
            {
                case PartKind.BeamNozzle: return WeaponPartArt.Keys.BeamNozzle;
                case PartKind.PowerNozzle:
                case PartKind.RangeExtender:
                case PartKind.WideBore: return WeaponPartArt.Keys.PowerNozzle;
                case PartKind.AugmentationHarness: return WeaponPartArt.Keys.AugmentationHarness;
                case PartKind.AccelerationEngine: return WeaponPartArt.Keys.AccelerationEngine;
                case PartKind.Hydro: return WeaponPartArt.Keys.HydroDevice;
                default: return WeaponPartArt.Keys.BeamNozzle;
            }
        }

        private static void Part(Transform root, string name, PrimitiveType shape, Vector3 pos,
                                 Vector3 scale, Quaternion rot, Material mat)
        {
            var go = GameObject.CreatePrimitive(shape);
            go.name = name;
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
            go.transform.SetParent(root, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = rot;
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            go.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        }

        private void OnDestroy()
        {
            if (_cam != null) _cam.targetTexture = null;
            if (_rt != null) { _rt.Release(); Destroy(_rt); }
        }
    }
}
