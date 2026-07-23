using UnityEngine;
using UnityEngine.Rendering;
using MaxWorlds.Core;
using MaxWorlds.Rendering;

namespace MaxWorlds.VFX
{
    /// <summary>
    /// The Upgrade screen's Max portrait (YT-176) — a live low-poly bust, rendered the same way
    /// <see cref="UpgradeWeaponStage"/> renders the hero weapon, replacing a 2D painted headshot that
    /// Lee flagged twice as off-style (it didn't match the game's low-poly 3D language at all).
    ///
    /// This is a standalone bust built from primitives, not a spawn of the live gameplay
    /// <see cref="MaxRig"/> — the same choice <see cref="UpgradeWeaponStage"/> made for the weapon
    /// rather than reaching into a live <c>WaterBlaster</c>. It shares MaxRig's palette (hoodie via
    /// <see cref="CharacterSkin"/>, the amber goggle lenses, the hood/hair silhouette) so the two read
    /// as the same kid, just built once here instead of depending on a live <c>PlayerController</c>
    /// existing on this screen.
    ///
    /// Sits far below the world on its own stage, same idiom as the weapon: a tiny orthographic camera
    /// pointed at a rig nothing else can see, rendering into a <see cref="RenderTexture"/> the screen
    /// shows in a <c>RawImage</c>. The camera only runs while the screen is open.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MaxPortraitStage : MonoBehaviour
    {
        private const float StageY = -2000f;   // a different altitude to UpgradeWeaponStage's -1000, so the two never overlap
        private const int TexW = 480, TexH = 566;   // ~0.848 aspect, matching the screen's portrait card

        private static Color Hoodie => CharacterSkin.BaseColorFor(CharacterRole.Player);
        private static Color HoodieShade => Hoodie * 0.80f;
        private static readonly Color Skin = new Color(0.87f, 0.63f, 0.46f);
        private static readonly Color Hair = new Color(0.33f, 0.20f, 0.12f);
        private static readonly Color Rubber = new Color(0.13f, 0.13f, 0.15f);
        private static readonly Color CanvasTone = new Color(0.29f, 0.25f, 0.20f);
        private static readonly Color LensGlass = new Color(1f, 0.72f, 0.24f);

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private Camera _cam;
        private RenderTexture _rt;
        private Transform _bust;

        /// <summary>The live portrait render, for the screen's RawImage.</summary>
        public RenderTexture Texture => _rt;

        public static MaxPortraitStage Create(Transform parent)
        {
            var go = new GameObject("MaxPortraitStage");
            go.transform.SetParent(parent, false);
            var stage = go.AddComponent<MaxPortraitStage>();
            stage.Build();
            return stage;
        }

        private void Build()
        {
            _rt = new RenderTexture(TexW, TexH, 24, RenderTextureFormat.ARGB32)
            {
                name = "MaxPortraitRT",
                antiAliasing = 4,
                hideFlags = HideFlags.HideAndDontSave,
            };
            _rt.Create();

            var pivot = new GameObject("PortraitPivot").transform;
            pivot.SetParent(transform, false);
            pivot.position = new Vector3(0f, StageY, 0f);

            _bust = new GameObject("Bust").transform;
            _bust.SetParent(pivot, false);
            _bust.gameObject.AddComponent<KeepsOwnMaterial>();
            BuildBust(_bust);

            var camGo = new GameObject("PortraitCam");
            camGo.transform.SetParent(pivot, false);
            _cam = camGo.AddComponent<Camera>();
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0f, 0f, 0f, 0f);   // transparent — the card shows behind it
            _cam.orthographic = true;
            _cam.orthographicSize = 0.62f;
            _cam.nearClipPlane = 0.05f;
            _cam.farClipPlane = 8f;
            _cam.targetTexture = _rt;
            _cam.enabled = false;   // only while the screen is open

            // Mostly front-on so the goggles and the hood read, with just enough turn and height to
            // feel like a hero shot rather than a mugshot.
            var lookAt = new Vector3(0f, 0.56f, 0f);
            var lookFrom = Quaternion.Euler(6f, 18f, 0f) * new Vector3(0f, 0f, -2.2f);
            camGo.transform.position = pivot.position + lookAt + lookFrom;
            camGo.transform.rotation = Quaternion.LookRotation(-lookFrom, Vector3.up);
        }

        /// <summary>
        /// Chest-up only — a bust, not the whole kid, the way a portrait card only ever shows a
        /// headshot. Built from the same masses <see cref="MaxRig"/> reads from thirty metres up: the
        /// hoodie's chest and shoulders, the hood wrapping the back of the head, messy hair pushed back
        /// off a bare brow, and the goggles pushed up on it, lit rather than painted.
        /// </summary>
        private void BuildBust(Transform root)
        {
            Part(root, "Chest", PrimitiveType.Cube, new Vector3(0f, 0.32f, 0f),
                 new Vector3(0.62f, 0.38f, 0.34f), Quaternion.identity, Tinted(Hoodie));

            for (int i = 0; i < 2; i++)
            {
                float side = i == 0 ? -1f : 1f;
                Part(root, "Shoulder", PrimitiveType.Sphere, new Vector3(side * 0.30f, 0.53f, 0f),
                     new Vector3(0.26f, 0.24f, 0.30f), Quaternion.identity, Tinted(Hoodie));

                // A strap peeking over the shoulder — the one hint of the backpack a front-on portrait
                // can actually see (the pack itself rides on his back, out of frame).
                Part(root, "Strap", PrimitiveType.Cube, new Vector3(side * 0.20f, 0.56f, -0.05f),
                     new Vector3(0.075f, 0.07f, 0.30f), Quaternion.Euler(0f, 0f, side * -12f),
                     Tinted(CanvasTone));
            }

            Part(root, "Neck", PrimitiveType.Cube, new Vector3(0f, 0.585f, 0.02f),
                 new Vector3(0.15f, 0.11f, 0.15f), Quaternion.identity, Tinted(Skin));

            // The hood, wrapping the back and sides of the head — the single shape that turns a head
            // and two shoulders into "a kid in a hoodie" (MaxRig's own reasoning, and it holds here too).
            Part(root, "Hood", PrimitiveType.Cube, new Vector3(0f, 0.72f, -0.20f),
                 new Vector3(0.46f, 0.30f, 0.28f), Quaternion.Euler(22f, 0f, 0f), Tinted(HoodieShade));

            Part(root, "Skull", PrimitiveType.Sphere, new Vector3(0f, 0.80f, 0.02f),
                 new Vector3(0.31f, 0.31f, 0.30f), Quaternion.identity, Tinted(Skin));

            // Messy hair, pushed back to leave the brow bare — that bare strip is where the goggles go.
            Part(root, "Hair", PrimitiveType.Sphere, new Vector3(0f, 0.885f, -0.055f),
                 new Vector3(0.335f, 0.215f, 0.33f), Quaternion.identity, Tinted(Hair));
            Part(root, "TuftL", PrimitiveType.Cube, new Vector3(-0.075f, 0.955f, -0.02f),
                 new Vector3(0.09f, 0.13f, 0.09f), Quaternion.Euler(11f, 0f, -25f), Tinted(Hair));
            Part(root, "TuftR", PrimitiveType.Cube, new Vector3(0.065f, 0.965f, -0.10f),
                 new Vector3(0.08f, 0.145f, 0.08f), Quaternion.Euler(-16f, 0f, 18f), Tinted(Hair));

            Part(root, "GoggleStrap", PrimitiveType.Cube, new Vector3(0f, 0.845f, 0.185f),
                 new Vector3(0.31f, 0.06f, 0.26f), Quaternion.identity, Tinted(Rubber));

            for (int i = 0; i < 2; i++)
            {
                float side = i == 0 ? -1f : 1f;
                BuildLens(root, new Vector3(side * 0.08f, 0.85f, 0.285f));
            }
        }

        /// <summary>A goggle lens — additive and unlit, like the boss's lamps and MaxRig's own lenses:
        /// glass catches light rather than absorbing it, so it reads as the one bright thing on a body
        /// that is otherwise in its own shadow.</summary>
        private static void BuildLens(Transform parent, Vector3 at)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "Lens";
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);

            go.transform.SetParent(parent, false);
            go.transform.localPosition = at;
            go.transform.localScale = new Vector3(0.12f, 0.105f, 0.08f);

            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = VfxMaterials.Additive(VfxMaterials.Glow());
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;

            var mpb = new MaterialPropertyBlock();
            r.GetPropertyBlock(mpb);
            mpb.SetColor(BaseColorId, LensGlass);
            r.SetPropertyBlock(mpb);
        }

        /// <summary>A shared, cached tint — see <see cref="MaterialLibrary.Tinted"/>: one material per
        /// distinct colour on this bust, not one per primitive, so the whole thing SRP-batches.</summary>
        private static Material Tinted(Color color) => MaterialLibrary.Tinted(SurfaceKind.Prop, color);

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

            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
        }

        /// <summary>Idle life while the screen sits paused on him — a slow head turn, so a reward
        /// moment doesn't freeze into a still photo.</summary>
        public void Tick(float unscaledT)
        {
            if (_bust != null) _bust.localRotation = Quaternion.Euler(0f, Mathf.Sin(unscaledT * 0.6f) * 6f, 0f);
        }

        public void Show() { if (_cam != null) _cam.enabled = true; }
        public void Hide() { if (_cam != null) _cam.enabled = false; }

        private void OnDestroy()
        {
            if (_cam != null) _cam.targetTexture = null;
            if (_rt != null) { _rt.Release(); Destroy(_rt); }
        }
    }
}
