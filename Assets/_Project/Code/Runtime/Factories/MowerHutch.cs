using UnityEngine;
using UnityEngine.UI;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.UI;

namespace MaxWorlds.Factories
{
    /// <summary>
    /// Mower Hutch — the destructible robot factory (YT-37), the teaching loop the whole
    /// game is built on: <em>clear pressure by killing the source, not the symptoms.</em>
    /// While alive it drives an <see cref="EnemySpawner"/> that emits domestic robots on a
    /// tunable cadence; it takes Water-Blaster damage (<see cref="IDamageable"/>, Team.Enemy)
    /// and carries its own world-space health bar. Destroying it stops the spawns, opens the
    /// <see cref="SubZoneGate"/>, and pops a clear "source is gone" feedback. Greybox — the
    /// primitive body is a stand-in until Phase C art.
    /// </summary>
    [RequireComponent(typeof(EnemySpawner))]
    public sealed class MowerHutch : MonoBehaviour, IDamageable
    {
        // ~4 s of focused fire to kill, so destroying it lands as a decisive beat (YT-65). Tunable.
        // Renamed from maxHealth so the lower value takes effect on the existing scene instance.
        [SerializeField] private float factoryHealth = 140f;
        [Tooltip("Gate opened when the factory dies. Optional — the path/gate is placed by YT-38.")]
        [SerializeField] private SubZoneGate gate;
        // Industrial hazard-orange so the factory reads as the objective, not another grey fence
        // in the greybox path (YT-38 QA: players couldn't tell the Mower Hutch from the scenery).
        [SerializeField] private Color bodyColor = new Color(0.72f, 0.34f, 0.10f);
        [SerializeField] private Color barColor = new Color(0.85f, 0.55f, 0.15f);
        [Tooltip("The pulsing 'vulnerable core' — the thing to shoot (spec §7 Robot Factory).")]
        [SerializeField] private Color coreColor = new Color(0.35f, 0.9f, 1f);

        // --- World bar (YT-71): sizes in metres, so the bar reads as a label on the factory rather
        // than a banner across the sky. The bar is deliberately narrower than the 3 m body: it's a
        // readout of the damage you're doing, not a piece of architecture. ---
        private const float BarWorldWidth = 1.8f;        // metres, vs a 3 m body
        private const float BarHeightAboveCentre = 1.7f; // metres above the body's centre (top is 1 m up)
        private const float BarPixelWidth = 180f;
        private const float BarPixelHeight = 22f;
        private const float LabelPixelWidth = 260f;
        private const float LabelPixelHeight = 28f;
        private const int LabelFontSize = 22;

        private DestructibleHealth _health;
        private EnemySpawner _spawner;
        private Camera _camera;
        private Transform _barPivot;
        private RectTransform _barCanvas;
        private Image _barFill;
        private Renderer _core;
        private MaterialPropertyBlock _coreMpb;

        public bool IsAlive => _health != null && _health.IsAlive;
        public Team Team => Team.Enemy; // Water Blaster (Team.Player) can damage it; robots can't
        public float Normalized => _health?.Normalized ?? 0f;

        private void Awake()
        {
            _health = new DestructibleHealth(factoryHealth);
            _health.Destroyed += OnDestroyed;
            _spawner = GetComponent<EnemySpawner>();
            _camera = Camera.main;

            // The hutch breaks sight-lines (YT-83), and it has to — the shed (YT-75) is 2.4 m of
            // plank wall built AROUND it as pure scenery with no collider of its own. It is the most
            // obviously hideable-behind object in the yard, and without this it would be the one
            // thing that didn't work. Players would call that a bug and they'd be right. Note that
            // LineOfSight lets you see the thing you're aiming AT, so putting the factory on the
            // cover layer doesn't make it immune to the blaster that has to destroy it.
            CoverLayer.Assign(gameObject);

            TintBody();
            BuildHealthBar();
            BuildCore();
            BuildLabel();
        }

        private void Start()
        {
            // Emit in Start so the HUD (subscribed in OnEnable) reliably catches it, switching
            // its arena tracker off the kill stand-in and onto real factory-destruction signals.
            HudSignals.EmitFactoryRegistered();
        }

        public void TakeDamage(in DamageInfo info)
        {
            if (!IsAlive) return;
            if (!DamageRules.Applies(info.Attacker, Team)) return; // robots can't wreck their own factory
            HudSignals.EmitDamage(transform.position + Vector3.up * 2f, info.Amount);
            _health.TakeDamage(info.Amount);
        }

        private void OnDestroyed()
        {
            if (_spawner != null) _spawner.enabled = false;       // spawns stop
            if (gate != null) gate.Open();                        // path opens
            // The destruction VFX hangs off this signal (CombatVfx, YT-48).
            HudSignals.EmitFactoryDestroyed(transform.position);
            HudSignals.EmitPickup(transform.position + Vector3.up * 2.4f, "GATE OPEN", barColor);

            // The source is gone: hide the body, collider, and bar — but keep the GameObject
            // ALIVE, because the robots it already spawned are parented here and must keep
            // fighting until the player clears them (deactivating the GO would freeze them).
            var rend = GetComponent<Renderer>();
            if (rend != null) rend.enabled = false;
            var col = GetComponent<Collider>();
            if (col != null) col.enabled = false;
            if (_barPivot != null) _barPivot.gameObject.SetActive(false);
            if (_core != null) _core.gameObject.SetActive(false);
        }

        private void LateUpdate()
        {
            if (_barFill != null) _barFill.fillAmount = Normalized;
            if (_barPivot != null && _camera != null)
            {
                // Billboard the health bar toward the fixed camera.
                _barPivot.rotation = Quaternion.LookRotation(
                    _barPivot.position - _camera.transform.position, Vector3.up);
            }
            PulseCore();
        }

        private void PulseCore()
        {
            if (_core == null) return;
            // Bright, breathing emission so the eye is pulled to "shoot here". Beats faster as the
            // factory nears death to sell that it's about to blow.
            float urgency = Mathf.Lerp(2f, 7f, 1f - Normalized);
            float pulse = 0.5f + 0.5f * Mathf.Abs(Mathf.Sin(Time.time * urgency));
            _core.GetPropertyBlock(_coreMpb);
            _coreMpb.SetColor("_BaseColor", coreColor);
            _coreMpb.SetColor("_EmissionColor", coreColor * (1.5f + 2.5f * pulse));
            _core.SetPropertyBlock(_coreMpb);
        }

        private void TintBody()
        {
            var rend = GetComponent<Renderer>();
            if (rend == null) return;
            var mpb = new MaterialPropertyBlock();
            rend.GetPropertyBlock(mpb);
            mpb.SetColor("_BaseColor", bodyColor);
            rend.SetPropertyBlock(mpb);
        }

        private void BuildHealthBar()
        {
            var pivotGo = new GameObject("FactoryHealthBar");
            _barPivot = pivotGo.transform;
            _barPivot.SetParent(transform, false);

            // The body is a cube scaled (3, 2, 3), and a child inherits that. Cancel it here so
            // everything below is authored in plain metres — otherwise the bar's own scale gets
            // multiplied by the body's and it renders many metres wide (YT-71).
            Vector3 body = transform.lossyScale;
            _barPivot.localScale = WorldBar.Unscale(body);
            _barPivot.localPosition = new Vector3(
                0f, WorldBar.LocalOffsetY(BarHeightAboveCentre, body.y), 0f);

            var canvasGo = new GameObject("Canvas", typeof(Canvas));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var crt = (RectTransform)canvasGo.transform;
            crt.SetParent(_barPivot, false);
            crt.sizeDelta = new Vector2(BarPixelWidth, BarPixelHeight);
            crt.localScale = Vector3.one * WorldBar.CanvasScaleFor(BarWorldWidth, BarPixelWidth);
            _barCanvas = crt;

            var bg = NewImage(crt, HudTextures.RoundedBox(24, 0.5f), new Color(0f, 0f, 0f, 0.6f));
            bg.type = Image.Type.Sliced;
            var bgRect = bg.rectTransform;
            bgRect.anchorMin = Vector2.zero; bgRect.anchorMax = Vector2.one;
            bgRect.offsetMin = Vector2.zero; bgRect.offsetMax = Vector2.zero;

            _barFill = NewImage(crt, HudTextures.RoundedBox(24, 0.5f), barColor);
            _barFill.type = Image.Type.Filled;
            _barFill.fillMethod = Image.FillMethod.Horizontal;
            _barFill.fillOrigin = (int)Image.OriginHorizontal.Left;
            _barFill.fillAmount = 1f;
            var fRect = _barFill.rectTransform;
            fRect.anchorMin = Vector2.zero; fRect.anchorMax = Vector2.one;
            fRect.offsetMin = new Vector2(3f, 3f); fRect.offsetMax = new Vector2(-3f, -3f);
        }

        private static Image NewImage(Transform parent, Sprite sprite, Color color)
        {
            var go = new GameObject("Bar", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        /// <summary>The glowing 'vulnerable core' on the front face — the thing to shoot. It has
        /// NO collider, so the Water Blaster passes through it to the body's collider behind (a
        /// collider here with no IDamageable would silently eat shots without dealing damage).</summary>
        private void BuildCore()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "VulnerableCore";
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 0.05f, -0.52f); // on the player-facing face
            go.transform.localScale = new Vector3(0.55f, 0.6f, 0.1f);

            // The core is driven by PulseCore every frame, so the skin director must leave it alone;
            // without this it gets skinned as a character and its cyan is overwritten with the
            // structure's grey, which is what put a dead grey panel on the build instead of a tell.
            go.AddComponent<SelfDrivenTint>();

            _core = go.GetComponent<Renderer>();
            _coreMpb = new MaterialPropertyBlock();
        }

        /// <summary>Floating name so the objective is unmistakable in the greybox path.</summary>
        private void BuildLabel()
        {
            if (_barCanvas == null) return;
            var go = new GameObject("Label", typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(_barCanvas, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.sizeDelta = new Vector2(LabelPixelWidth, LabelPixelHeight);
            rect.anchoredPosition = new Vector2(0f, 5f);
            var t = go.AddComponent<Text>();
            t.font = HudFont.Get();
            t.text = "MOWER HUTCH";
            t.fontSize = LabelFontSize;
            t.fontStyle = FontStyle.Bold;
            t.alignment = TextAnchor.MiddleCenter;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.color = new Color(1f, 0.85f, 0.4f);
            t.raycastTarget = false;
        }
    }
}
