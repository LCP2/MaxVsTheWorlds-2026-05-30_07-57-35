using UnityEngine;
using UnityEngine.UI;
using MaxWorlds.Factories;
using MaxWorlds.Rendering;
using MaxWorlds.VFX;

namespace MaxWorlds.UI
{
    /// <summary>
    /// MV-1404: tells the player what a vault key is FOR. While a <see cref="VaultKeyState"/> key is held: a
    /// gold key icon on the HUD and a gold arrow at Max's feet pointing at that key's vault; a banner on
    /// pickup ("VAULT KEY - open the vault in this area", 3 s) and one when the vault opens ("VAULT OPENED",
    /// 2 s). Polled from <see cref="Tick"/> (a few string compares a frame, nothing built until a key is
    /// first held), so a checkpoint-restored key behaves exactly like a freshly collected one.
    /// </summary>
    [MaxWorlds.Core.PerfSection("hud")]
    public sealed class VaultKeyGuide : MonoBehaviour
    {
        public const float PickupBannerSeconds = 3f;
        public const float OpenedBannerSeconds = 2f;

        private static readonly Color Gold = new Color(1f, 0.82f, 0.25f, 1f);
        private const float ArrowLength = 1.4f;
        private const float ArrowAheadOfMax = 1.5f; // arrow centre this far from Max's feet, toward the vault

        private static VaultKeyGuide s_instance;

        private RectTransform _icon;
        private Text _banner;
        private RectTransform _bannerBox;
        private Transform _arrow;
        private Transform _player;
        private string _lastHeld;
        private float _bannerTimer;
        private string _bannerText = string.Empty;

        public static VaultKeyGuide EnsureInstalled()
        {
            if (s_instance != null) return s_instance;
            var go = new GameObject("VaultKeyGuide");
            s_instance = go.AddComponent<VaultKeyGuide>();
            return s_instance;
        }

        /// <summary>Tier 2 readouts: the resolved state of the HUD key icon, banner and arrow.</summary>
        public bool KeyIconActive => _icon != null && _icon.gameObject.activeSelf;
        public bool BannerActive => _bannerBox != null && _bannerBox.gameObject.activeSelf;
        public string BannerText => _bannerText;
        public bool ArrowActive => _arrow != null && _arrow.gameObject.activeSelf;

        /// <summary>The arrow's resolved world-space direction on the ground plane (unit), zero when hidden.</summary>
        public Vector3 ArrowDirection
        {
            get
            {
                if (!ArrowActive) return Vector3.zero;
                Vector3 f = _arrow.forward; f.y = 0f;
                return f.sqrMagnitude > 1e-6f ? f.normalized : Vector3.zero;
            }
        }

        private void OnDestroy() { if (s_instance == this) s_instance = null; }

        private void Update() => Tick(Time.unscaledDeltaTime);

        public void Tick(float dt)
        {
            string held = VaultKeyState.HeldVaultId;
            if (held != _lastHeld)
            {
                if (held != null) ShowBanner("VAULT KEY - open the vault in this area", PickupBannerSeconds);
                else if (_lastHeld != null && Vault.TryGetPosition(_lastHeld, out _, out bool closed) && !closed)
                    ShowBanner("VAULT OPENED", OpenedBannerSeconds);
                _lastHeld = held;
            }

            if (_bannerTimer > 0f)
            {
                _bannerTimer -= dt;
                if (_bannerTimer <= 0f) { _bannerTimer = 0f; if (_bannerBox != null) _bannerBox.gameObject.SetActive(false); }
            }

            if (held == null && _icon == null && _arrow == null) return; // nothing built, nothing to hide

            if (held != null && _icon == null) BuildHud();
            if (_icon != null) _icon.gameObject.SetActive(held != null);
            TickArrow(held);
        }

        private void TickArrow(string held)
        {
            bool show = false;
            if (held != null && Vault.TryGetPosition(held, out Vector3 vaultPos, out bool closed) && closed)
            {
                if (_player == null)
                {
                    GameObject p = GameObject.FindGameObjectWithTag("Player");
                    _player = p != null ? p.transform : null;
                }
                if (_player != null)
                {
                    Vector3 to = vaultPos - _player.position; to.y = 0f;
                    if (to.sqrMagnitude > 0.01f)
                    {
                        if (_arrow == null) BuildArrow();
                        Vector3 dir = to.normalized;
                        float lift = GroundMarkHeights.SurfaceAt(_player.position).y + 0.06f;
                        _arrow.position = new Vector3(_player.position.x, lift, _player.position.z) + dir * ArrowAheadOfMax;
                        _arrow.rotation = Quaternion.LookRotation(dir, Vector3.up);
                        show = true;
                    }
                }
            }
            if (_arrow != null) _arrow.gameObject.SetActive(show);
        }

        private void ShowBanner(string text, float seconds)
        {
            _bannerText = text;
            _bannerTimer = seconds;
            if (_bannerBox == null) BuildHud();
            _banner.text = text;
            _bannerBox.gameObject.SetActive(true);
        }

        private void BuildHud()
        {
            if (_icon != null && _bannerBox != null) return;

            Canvas hud = HudController.ActiveCanvas;
            Transform parent;
            if (hud != null) parent = hud.transform;
            else
            {
                var cgo = new GameObject("VaultKeyGuide Canvas", typeof(Canvas), typeof(CanvasScaler));
                var canvas = cgo.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 140;
                var scaler = cgo.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                scaler.matchWidthOrHeight = 0.5f;
                cgo.transform.SetParent(transform, false);
                parent = canvas.transform;
            }

            if (_icon == null)
            {
                var igo = new GameObject("VaultKeyIcon", typeof(RectTransform), typeof(Image));
                _icon = (RectTransform)igo.transform;
                _icon.SetParent(parent, false);
                _icon.anchorMin = _icon.anchorMax = new Vector2(0f, 0.5f);
                _icon.pivot = new Vector2(0f, 0.5f);
                _icon.sizeDelta = new Vector2(84f, 84f);
                _icon.anchoredPosition = new Vector2(150f, 120f);
                var img = igo.GetComponent<Image>(); img.color = Gold; img.raycastTarget = false;
                Text k = NewText(_icon, "K", 52, new Color(0.15f, 0.1f, 0f));
                Stretch((RectTransform)k.transform);
                Text lbl = NewText(_icon, "KEY", 30, Gold);
                var lr = (RectTransform)lbl.transform;
                lr.anchorMin = lr.anchorMax = new Vector2(0.5f, 0f); lr.pivot = new Vector2(0.5f, 1f);
                lr.sizeDelta = new Vector2(160f, 40f); lr.anchoredPosition = new Vector2(0f, -4f);
                igo.SetActive(false);
            }

            if (_bannerBox == null)
            {
                var bgo = new GameObject("VaultKeyBanner", typeof(RectTransform), typeof(Image), typeof(Outline));
                _bannerBox = (RectTransform)bgo.transform;
                _bannerBox.SetParent(parent, false);
                _bannerBox.anchorMin = _bannerBox.anchorMax = new Vector2(0.5f, 1f);
                _bannerBox.pivot = new Vector2(0.5f, 1f);
                _bannerBox.sizeDelta = new Vector2(1000f, 110f);
                _bannerBox.anchoredPosition = new Vector2(0f, -150f);
                var bimg = bgo.GetComponent<Image>(); bimg.color = new Color(0.1f, 0.09f, 0.16f, 0.92f); bimg.raycastTarget = false;
                var ol = bgo.GetComponent<Outline>(); ol.effectColor = Gold; ol.effectDistance = new Vector2(3f, 3f);
                _banner = NewText(_bannerBox, string.Empty, 44, Gold);
                Stretch((RectTransform)_banner.transform);
                bgo.SetActive(false);
            }
        }

        private static Text NewText(Transform parent, string text, int size, Color color)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.font = HudFont.Get();
            t.fontSize = size; t.fontStyle = FontStyle.Bold;
            t.alignment = TextAnchor.MiddleCenter;
            t.color = color; t.text = text;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        /// <summary>A flat gold arrow from primitives: shaft + two head bars, pointing along the root's +Z.</summary>
        private void BuildArrow()
        {
            var root = new GameObject("VaultKeyArrow").transform;
            root.SetParent(transform, false);
            Material gold = MaterialLibrary.Tinted(SurfaceKind.Metal, new Color(1f, 0.82f, 0.25f));
            AddBar(root, new Vector3(0f, 0f, -0.1f), Quaternion.identity, new Vector3(0.22f, 0.04f, ArrowLength - 0.4f), gold);
            AddBar(root, new Vector3(-0.2f, 0f, ArrowLength * 0.5f - 0.3f), Quaternion.Euler(0f, 40f, 0f), new Vector3(0.2f, 0.04f, 0.7f), gold);
            AddBar(root, new Vector3(0.2f, 0f, ArrowLength * 0.5f - 0.3f), Quaternion.Euler(0f, -40f, 0f), new Vector3(0.2f, 0.04f, 0.7f), gold);
            _arrow = root;
            root.gameObject.SetActive(false);
        }

        private static void AddBar(Transform parent, Vector3 pos, Quaternion rot, Vector3 scale, Material mat)
        {
            var part = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var c = part.GetComponent<Collider>();
            if (Application.isPlaying) Destroy(c); else DestroyImmediate(c);
            part.transform.SetParent(parent, false);
            part.transform.localPosition = pos; part.transform.localRotation = rot; part.transform.localScale = scale;
            var mr = part.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }
}
