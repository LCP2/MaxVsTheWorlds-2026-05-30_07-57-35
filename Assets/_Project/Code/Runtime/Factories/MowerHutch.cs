using UnityEngine;
using UnityEngine.UI;
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
        [SerializeField] private float maxHealth = 240f;
        [Tooltip("Gate opened when the factory dies. Optional — the path/gate is placed by YT-38.")]
        [SerializeField] private SubZoneGate gate;
        [SerializeField] private Color bodyColor = new Color(0.45f, 0.42f, 0.40f);
        [SerializeField] private Color barColor = new Color(0.85f, 0.55f, 0.15f);

        private DestructibleHealth _health;
        private EnemySpawner _spawner;
        private Camera _camera;
        private Transform _barPivot;
        private Image _barFill;

        public bool IsAlive => _health != null && _health.IsAlive;
        public Team Team => Team.Enemy; // Water Blaster (Team.Player) can damage it; robots can't
        public float Normalized => _health?.Normalized ?? 0f;

        private void Awake()
        {
            _health = new DestructibleHealth(maxHealth);
            _health.Destroyed += OnDestroyed;
            _spawner = GetComponent<EnemySpawner>();
            _camera = Camera.main;
            TintBody();
            BuildHealthBar();
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
            _barPivot.localPosition = Vector3.up * 3.2f;

            var canvasGo = new GameObject("Canvas", typeof(Canvas));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var crt = (RectTransform)canvasGo.transform;
            crt.SetParent(_barPivot, false);
            crt.sizeDelta = new Vector2(220f, 28f);
            crt.localScale = Vector3.one * 0.02f; // world units per UI px

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

    }
}
