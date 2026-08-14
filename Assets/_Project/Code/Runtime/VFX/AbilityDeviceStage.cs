using UnityEngine;
using UnityEngine.Rendering;

namespace MaxWorlds.VFX
{
    /// <summary>
    /// The BUILD ABILITY button's live render (MV-382): the same <see cref="WeaponPartArt.Keys.HydroDevice"/>
    /// prop the shed's ability pickup wears, spinning in a small render texture — replacing the button's
    /// old flat coloured rectangle + plain text ("looks like accounting software, not a fun game", Lee's
    /// playtest note). Same idiom as <see cref="UpgradeWeaponStage"/>: a tiny stage far below the world,
    /// lit by the scene's own directional sun, with a dedicated orthographic camera rendering to a
    /// RenderTexture the button's RawImage shows — just one static prop with a spin, no part-fitting
    /// animation. Offset well clear of <see cref="UpgradeWeaponStage"/>'s own far-below stage so neither
    /// camera's tight frustum ever picks up the other's props.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AbilityDeviceStage : MonoBehaviour
    {
        private static readonly Vector3 StagePosition = new Vector3(80f, -1000f, 0f);
        private const int TexSize = 256;
        private const float SpinDegreesPerSecond = 70f;

        private Camera _cam;
        private RenderTexture _rt;
        private Transform _device;

        /// <summary>The live device render, for the button's RawImage.</summary>
        public RenderTexture Texture => _rt;

        public static AbilityDeviceStage Create(Transform parent)
        {
            var go = new GameObject("AbilityDeviceStage");
            go.transform.SetParent(parent, false);
            var stage = go.AddComponent<AbilityDeviceStage>();
            stage.Build();
            return stage;
        }

        private void Build()
        {
            _rt = new RenderTexture(TexSize, TexSize, 16, RenderTextureFormat.ARGB32)
            {
                name = "AbilityDeviceRT",
                antiAliasing = 4,
                hideFlags = HideFlags.HideAndDontSave,
            };
            _rt.Create();

            var pivot = new GameObject("StagePivot").transform;
            pivot.SetParent(transform, false);
            pivot.position = StagePosition;

            var deviceGo = WeaponPartArt.Build(WeaponPartArt.Keys.HydroDevice, pivot);
            // Authored base-at-zero (WeaponPartArt convention); drop it so it hovers centred in frame
            // rather than floating above the camera's aim point — same offset PickupArtDirector uses to
            // centre this same prop on its ground pickup.
            if (deviceGo != null)
            {
                deviceGo.transform.localPosition = new Vector3(0f, -0.22f, 0f);
                _device = deviceGo.transform;
            }

            var camGo = new GameObject("StageCam");
            camGo.transform.SetParent(pivot, false);
            _cam = camGo.AddComponent<Camera>();
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0f, 0f, 0f, 0f);   // transparent — the button's own BG shows behind it
            _cam.orthographic = true;
            _cam.orthographicSize = 0.3f;   // tight on the single prop so it fills the button as a hero
            _cam.nearClipPlane = 0.05f;
            _cam.farClipPlane = 8f;
            _cam.targetTexture = _rt;
            _cam.enabled = false;   // only while the owning screen is open
            var lookFrom = Quaternion.Euler(18f, -32f, 0f) * new Vector3(0f, 0f, -3f);
            camGo.transform.position = pivot.position + lookFrom;
            camGo.transform.rotation = Quaternion.LookRotation(-lookFrom, Vector3.up);
        }

        /// <summary>Start rendering. MV-251's headless-run guard (same as <see cref="UpgradeWeaponStage.Show"/>):
        /// under a -nographics automated test run there is no real graphics device, and enabling a camera
        /// pointed at a RenderTexture logs an engine error that fails whatever test happens to be
        /// running.</summary>
        public void Show()
        {
            if (_cam != null) _cam.enabled = SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null;
        }

        public void Hide()
        {
            if (_cam != null) _cam.enabled = false;
        }

        /// <summary>Keep the device turning while the screen is up — driven by the screen's own clock,
        /// unscaled since the game is paused underneath.</summary>
        public void Tick()
        {
            if (_device != null) _device.Rotate(0f, SpinDegreesPerSecond * Time.unscaledDeltaTime, 0f, Space.Self);
        }

        private void OnDestroy()
        {
            if (_cam != null) _cam.targetTexture = null;
            if (_rt != null) { _rt.Release(); Destroy(_rt); }
        }
    }
}
