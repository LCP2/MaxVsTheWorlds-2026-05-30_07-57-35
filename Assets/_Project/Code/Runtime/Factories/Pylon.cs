using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Rendering;
using MaxWorlds.UI;
using MaxWorlds.VFX;

namespace MaxWorlds.Factories
{
    /// <summary>
    /// A City substation pylon (MV-1294) — feeds a relay (<see cref="Replicator"/>) or a boss in its own
    /// area: while any pylon naming that target's id in <see cref="Feeds"/> is still alive, the target
    /// cannot be damaged. This ticket builds the pylon, its cable and its public <see cref="LiveFeedersOf"/>
    /// query only — MV-1304 (the relay dome) and MV-1309 (GRIDLOCK's shield) are the two consumers that
    /// wire a damage refusal off that query; neither is this ticket's to add.
    ///
    /// Deliberately NOT a <see cref="Replicator"/>/<see cref="MowerHutch"/> subclass — no lure/spawn/
    /// queue machinery applies to a pylon, same "do not re-raise" reasoning <see cref="Replicator"/>'s own
    /// doc comment gives for not subclassing <see cref="MowerHutch"/>. It reuses what actually is shared:
    /// <see cref="DestructibleHealth"/> for HP, <see cref="WorldHealthBar.Attach"/> for the bar, and
    /// <see cref="HudSignals.EmitFittingDestroyed"/> for the death spark — the same small
    /// "a turret coming apart, not a building" burst a shed corner fitting uses (MV-547), since a pylon
    /// drops no loot and counts in no economy either.
    /// </summary>
    [MaxWorlds.Core.PerfSection("factories")]
    public sealed class Pylon : MonoBehaviour, IDamageable, IHealthReadout
    {
        /// <summary>Ticket's own "120 HP".</summary>
        private const float PylonHealth = 120f;

        /// <summary>Metres above this mast's own centre the health bar floats — the mast's top sits
        /// <see cref="WorldMapLoader.PylonHeight"/>/2 above centre (this body's own pivot is its vertical
        /// centre, same convention as every other MapRuntime-built actor), so this clears the crown light
        /// with margin, same "above its own rendered top" reasoning as Replicator.ReplicatorBarHeightAboveCentre.</summary>
        private const float PylonBarHeightAboveCentre = 2.4f;

        private const float PylonBarWorldWidth = 1.0f;

        // --- Body proportions (MV-1294 ticket: "3.0 m tall on a 1.4 m square plinth") ---
        private const float PlinthHeight = 0.3f;
        private const float PlinthSize = 1.4f;
        private const float MastBaseSize = 1.0f;
        private const float MastTopSize = 0.5f;      // narrower at the crown than the base — the "tapered lattice" read
        private const float StubHeight = 0.6f;       // the husk OnDestroyed leaves standing
        private const float CrownLightSize = 0.3f;

        private static readonly Color SteelColor = new Color(0.72f, 0.74f, 0.76f);       // "white/grey steel"
        private static readonly Color CrownLightColor = new Color(1f, 0.12f, 0.1f);       // "red insulator crown light"

        // --- Cable (MV-1294 ticket: "a glowing red cable ... with small bright dots travelling along it
        // toward the target, >= 6 px, ~1 m/s") ---
        private static readonly Color CableColor = new Color(0.9f, 0.08f, 0.06f);
        private const float CableDotSpeed = 1f;            // metres/second
        private const float CableDotDiameter = 0.16f;      // >= 6 px at the play camera's ~48 px/m (docs/DECISIONS.md §3)
        private const float CableHeight = 0.05f;           // just above the floor, avoids z-fighting
        private const int CableDotCount = 3;
        private const float CableFadeSeconds = 0.3f;       // ticket: "cable goes dark over 0.3 s"

        private DestructibleHealth _health;
        private Transform _bodyRoot;
        private Transform _mast;      // hidden on death
        private Transform _stub;      // shown on death
        private Collider _collider;

        private Transform _feedTarget;
        private Transform _cableRoot;
        private LineRenderer _cableLine;
        private Material _cableMaterial;
        private MeshRenderer[] _cableDots;
        private float _cablePathLength;
        private Vector3 _cableStart;
        private Vector3 _cableBend;
        private Vector3 _cableEnd;
        private bool _hasCable;
        private float _cableFadeElapsed = -1f;  // < 0 until destroyed
        private float _cableDotElapsed;

        public bool IsAlive => _health != null && _health.IsAlive;
        public Team Team => Team.Enemy; // Water Blaster (Team.Player) can damage it; robots can't (ticket: "player side only")
        public float Normalized => _health?.Normalized ?? 0f;

        // --- IHealthReadout — the same world health bar every gate/sentinel/robot/Max/Replicator carries. ---
        public float HealthNormalized => Normalized;
        public float HealthCurrent => _health?.Current ?? 0f;
        public string ReadoutName => "PYLON";

        /// <summary>This pylon's stable id (MV-1294) — same "a checkpoint can persist/restore by id"
        /// reasoning as <see cref="Replicator.Id"/>/<see cref="MowerHutch.Id"/>. Empty until <see cref="SetId"/>
        /// is called.</summary>
        public string Id { get; private set; } = string.Empty;

        /// <summary>Public so an EditMode test can drive identity directly, same convention as every other
        /// post-AddComponent configure call in this codebase.</summary>
        public void SetId(string id) => Id = id ?? string.Empty;

        /// <summary>The id of the relay or boss this pylon feeds (MV-1294), from <see cref="WorldPylon.feeds"/>/
        /// <see cref="MapEntity.feeds"/> — what <see cref="LiveFeedersOf"/> matches against.</summary>
        public string Feeds { get; private set; } = string.Empty;

        /// <summary>Public so an EditMode test can drive it directly, same convention as every other
        /// post-AddComponent configure call in this file.</summary>
        public void Configure(string feeds) => Feeds = feeds ?? string.Empty;

        // --- Static registry (MV-1294) — same "the map builds them, Build() registers them, Reset() wipes
        // them on the next level" shape as FactoryCensus, kept local to Pylon rather than folded into that
        // class: nothing here needs FactoryCensus's own shed/replicator/cradle-specific HUD/loot/difficulty
        // wiring, and a pylon is neither a shed-equivalent factory nor counted in any of those systems. ---
        private static readonly List<Pylon> Registered = new List<Pylon>(8);

        /// <summary>Wipe the registry. Called by <see cref="MapRuntime.Build"/> when a level starts
        /// building, same reasoning as <see cref="FactoryCensus.Reset"/> — a level loaded a second time
        /// (the game, or a test run) must count its own pylons, not the previous level's (or the previous
        /// test's) ghosts. Named <c>ResetRegistry</c>, not <c>Reset</c> — <see cref="Pylon"/> is a
        /// <see cref="MonoBehaviour"/>, and a bare static <c>Reset()</c> collides with Unity's own
        /// reserved MonoBehaviour message of that name (the Editor's "Reset" command): Unity's reflection
        /// finds it, tries to invoke it as an instance message, and fails with "Failed to call static
        /// function Reset because an object was provided" — the same reason <c>RobotEnemy</c>'s own
        /// registry-clear method is <c>ResetRegistry</c>, not <c>Reset</c>.</summary>
        public static void ResetRegistry() => Registered.Clear();

        /// <summary>How many currently-registered, currently-ALIVE pylons feed <paramref name="targetId"/>
        /// (MV-1294's own public query, for MV-1304's relay dome and MV-1309's GRIDLOCK shield) — a relay
        /// or boss with no live feeding pylon left reads 0.</summary>
        public static int LiveFeedersOf(string targetId)
        {
            if (string.IsNullOrEmpty(targetId)) return 0;
            int count = 0;
            for (int i = 0; i < Registered.Count; i++)
            {
                Pylon p = Registered[i];
                if (p != null && p.IsAlive && p.Feeds == targetId) count++;
            }
            return count;
        }

        /// <summary>The stable ids of every registered pylon already destroyed this run (MV-1294) — what
        /// a mid-run checkpoint capture persists so a resume can re-apply the exact same destroyed set
        /// onto the fresh instances a level rebuild creates. Same shape as
        /// <see cref="FactoryCensus.DestroyedReplicatorIds"/>.</summary>
        public static string[] DestroyedIds()
        {
            var ids = new List<string>();
            foreach (Pylon p in Registered)
                if (p != null && !p.IsAlive && !string.IsNullOrEmpty(p.Id)) ids.Add(p.Id);
            return ids.ToArray();
        }

        /// <summary>Re-applies a checkpoint's already-recorded pylon destruction (MV-1294) onto whichever
        /// currently-registered, still-alive pylons carry a matching <see cref="Id"/> — called once by
        /// <see cref="MaxWorlds.Save.SaveSystem.RestoreCheckpoint"/> on RESUME, after the level's own
        /// pylons have already registered themselves alive (the ordinary build). Same shape as
        /// <see cref="FactoryCensus.ApplyCheckpointDestroyedIds"/>. A no-op for an empty/null set.</summary>
        public static void ApplyCheckpointDestroyedIds(IReadOnlyList<string> destroyedIds)
        {
            if (destroyedIds == null || destroyedIds.Count == 0) return;

            foreach (Pylon p in Registered)
            {
                if (p == null || !p.IsAlive || string.IsNullOrEmpty(p.Id)) continue;
                for (int i = 0; i < destroyedIds.Count; i++)
                {
                    if (destroyedIds[i] != p.Id) continue;
                    p.ApplyCheckpointDestroyed();
                    break;
                }
            }
        }

        private void Awake() => Build();

        /// <summary>Construct the health model and the greybox body. Exposed publicly, same reasoning as
        /// <see cref="Replicator.Build"/>/<see cref="MowerHutch.Build"/> — Awake never runs as a side
        /// effect of AddComponent outside Play mode, so an EditMode test calls this directly.</summary>
        public void Build()
        {
            if (_health != null) return; // already built — AddComponent's Awake may or may not have run first

            _health = new DestructibleHealth(PylonHealth);
            _health.Destroyed += OnDestroyed;

            BuildBody();
            BuildHealthBar();
            TryBuildCable();

            if (!Registered.Contains(this)) Registered.Add(this);
        }

        private void OnDestroy() => Registered.Remove(this);

        private void BuildBody()
        {
            _collider = GetComponent<Collider>();
            var rend = GetComponent<Renderer>();
            if (rend != null)
            {
                rend.enabled = false;
                MapStaticBatchRoot.Active?.MarkPermanentlyHidden(rend);
            }

            _bodyRoot = ParentScale.MakeMetreSpace(new GameObject("Body").transform, transform);

            // This body's own pivot sits at its vertical centre (MapData.GroundedCenter), same convention
            // every other MapRuntime-built actor uses — so the plinth starts half the full height down.
            float halfHeight = WorldMapLoader.PylonHeight * 0.5f;
            float baseY = -halfHeight;

            // Plinth — always visible, alive or destroyed.
            BuildPart(_bodyRoot, "Plinth", new Vector3(0f, baseY + PlinthHeight * 0.5f, 0f),
                new Vector3(PlinthSize, PlinthHeight, PlinthSize), SteelColor);

            // Mast — a tapered two-segment lattice read, hidden on death.
            var mastGo = new GameObject("Mast");
            _mast = mastGo.transform;
            _mast.SetParent(_bodyRoot, false);
            _mast.localPosition = Vector3.zero;

            float mastHeight = WorldMapLoader.PylonHeight - PlinthHeight;
            float segmentHeight = mastHeight * 0.5f;
            float mastFootY = baseY + PlinthHeight;
            BuildPart(_mast, "MastLower", new Vector3(0f, mastFootY + segmentHeight * 0.5f, 0f),
                new Vector3(MastBaseSize, segmentHeight, MastBaseSize), SteelColor);
            BuildPart(_mast, "MastUpper", new Vector3(0f, mastFootY + segmentHeight * 1.5f, 0f),
                new Vector3(MastTopSize, segmentHeight, MastTopSize), SteelColor);
            BuildCrownLight(_mast, new Vector3(0f, halfHeight - CrownLightSize * 0.5f, 0f));

            // Stub — the husk OnDestroyed leaves standing instead of the mast, inactive until then.
            var stubGo = new GameObject("Stub");
            _stub = stubGo.transform;
            _stub.SetParent(_bodyRoot, false);
            _stub.localPosition = Vector3.zero;
            BuildPart(_stub, "StubBody", new Vector3(0f, mastFootY + StubHeight * 0.5f, 0f),
                new Vector3(MastBaseSize, StubHeight, MastBaseSize), SteelColor);
            _stub.gameObject.SetActive(false);
        }

        private static void BuildPart(Transform parent, string partName, Vector3 localPosition, Vector3 size, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = partName;
            var col = go.GetComponent<Collider>();
            if (col != null) Object.DestroyImmediate(col); // cosmetic only — the body's own root primitive carries the real collider
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = size;

            var rend = go.GetComponent<MeshRenderer>();
            Material mat = MaterialLibrary.Tinted(SurfaceKind.Metal, color);
            if (mat != null) rend.sharedMaterial = mat;
        }

        private static void BuildCrownLight(Transform parent, Vector3 localPosition)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "CrownLight";
            var col = go.GetComponent<Collider>();
            if (col != null) Object.DestroyImmediate(col);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = Vector3.one * CrownLightSize;

            var rend = go.GetComponent<MeshRenderer>();
            Material mat = VfxMaterials.AdditiveTinted(CrownLightColor);
            if (mat != null) rend.sharedMaterial = mat;
        }

        /// <summary>MV-1294: the same <see cref="WorldHealthBar.Attach"/> every gate/sentinel/robot/Max/
        /// Replicator already carries — hidden at full health until first damaged, visible while alive,
        /// off the instant <see cref="IsAlive"/> goes false. No raw HP number, matching Replicator's own
        /// bar.</summary>
        private void BuildHealthBar()
        {
            WorldHealthBar.Attach(gameObject, this, PylonBarHeightAboveCentre, PylonBarWorldWidth, showNumber: false);
        }

        public void TakeDamage(in DamageInfo info)
        {
            if (!IsAlive) return;
            if (!DamageRules.Applies(info.Attacker, Team)) return; // robots can't wreck their own power feed
            HudSignals.EmitDamage(transform.position + Vector3.up * 1.5f, info.Amount);
            _health.TakeDamage(info.Amount);
        }

        private void OnDestroyed()
        {
            ApplyDestructionEffects();
            // The small spark+debris burst (MV-547's own "a small turret, not a building" read) — a
            // pylon drops no loot and counts in no economy, so HudSignals.EmitFactoryDestroyed (which
            // carries loot/difficulty/finale-gate side effects a pylon must never trigger) is wrong here.
            HudSignals.EmitFittingDestroyed(transform.position);
        }

        /// <summary>Everything a death does to this pylon EXCEPT the spark-burst signal — pulled out so
        /// <see cref="ApplyCheckpointDestroyed"/> can silently re-apply a checkpoint's already-recorded
        /// destruction (a resume rebuilding the level from scratch) without replaying it, same split
        /// <see cref="Replicator"/>'s own ApplyDestructionEffects/OnDestroyed keeps.</summary>
        private void ApplyDestructionEffects()
        {
            if (_mast != null) _mast.gameObject.SetActive(false);
            if (_stub != null) _stub.gameObject.SetActive(true);
            if (_collider != null) _collider.enabled = false;

            if (_hasCable) _cableFadeElapsed = 0f; // starts the ticket's own 0.3 s cable fade-to-dark
        }

        /// <summary>MV-1294: silently applies a checkpoint's already-recorded destruction — called once,
        /// on RESUME, by <see cref="ApplyCheckpointDestroyedIds"/>. Skips the live-combat
        /// <see cref="OnDestroyed"/> path's spark/HUD signal entirely (via <see cref="DestructibleHealth.SilentlyDestroy"/>
        /// instead of <see cref="TakeDamage"/>) — a resume is restoring history, not scoring a new kill.
        /// The cable is left already dark (no 0.3 s fade to watch on a level that is only just loading).</summary>
        public void ApplyCheckpointDestroyed()
        {
            if (_health == null || !_health.IsAlive) return;
            _health.SilentlyDestroy();
            ApplyDestructionEffects();
            _cableFadeElapsed = CableFadeSeconds;
            SetCableVisible(false);
        }

        // ---------------------------------------------------------------- Cable (MV-1294 Change 3)

        /// <summary>Points this pylon's cable at the GameObject it feeds — read once by
        /// <see cref="MapRuntime.BuildPylon"/> right after AddComponent, same "post-build configure call"
        /// convention as <see cref="Replicator.SetFacing"/>. Order-independent of <see cref="Build"/>
        /// (either may run first, depending on whether AddComponent's own Awake fires): whichever of the
        /// two runs second is the one that actually builds the cable (<see cref="TryBuildCable"/>).</summary>
        public void SetFeedTarget(Transform target)
        {
            _feedTarget = target;
            TryBuildCable();
        }

        private void TryBuildCable()
        {
            if (_hasCable || _feedTarget == null || _bodyRoot == null) return;
            BuildCable(_feedTarget.position);
        }

        private void BuildCable(Vector3 targetWorldPos)
        {
            Vector3 start = transform.position; start.y = CableHeight;
            Vector3 end = targetWorldPos; end.y = CableHeight;
            // An "L" route (ticket: "routed in straight runs ... an L is fine") — bend horizontally
            // first, then vertically, toward the target.
            Vector3 bend = new Vector3(end.x, CableHeight, start.z);

            _cableStart = start; _cableBend = bend; _cableEnd = end;
            _cablePathLength = Vector3.Distance(start, bend) + Vector3.Distance(bend, end);
            if (_cablePathLength < 0.01f) return; // the pylon and its target share a position — nothing to draw

            var cableGo = new GameObject($"{(string.IsNullOrEmpty(Id) ? name : Id)}_Cable");
            _cableRoot = cableGo.transform;
            _cableRoot.SetParent(transform.parent, worldPositionStays: true);

            // A private clone, never VfxMaterials' own shared cache: TickCable mutates this material's
            // colour every frame of the death fade, and a shared-by-colour-key instance would fade every
            // other pylon's cable wearing the same tone right along with this one.
            Material template = VfxMaterials.AdditiveTinted(CableColor);
            _cableMaterial = template != null ? new Material(template) { hideFlags = HideFlags.HideAndDontSave } : null;

            _cableLine = cableGo.AddComponent<LineRenderer>();
            _cableLine.sharedMaterial = _cableMaterial;
            _cableLine.useWorldSpace = true;
            _cableLine.positionCount = 3;
            _cableLine.SetPositions(new[] { start, bend, end });
            _cableLine.startWidth = _cableLine.endWidth = 0.08f;
            _cableLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _cableLine.receiveShadows = false;

            _cableDots = new MeshRenderer[CableDotCount];
            for (int i = 0; i < CableDotCount; i++)
            {
                var dot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                dot.name = $"Dot{i}";
                var col = dot.GetComponent<Collider>();
                if (col != null) Object.DestroyImmediate(col);
                dot.transform.SetParent(_cableRoot, worldPositionStays: true);
                dot.transform.localScale = Vector3.one * CableDotDiameter;

                var dotRend = dot.GetComponent<MeshRenderer>();
                dotRend.sharedMaterial = _cableMaterial;
                dotRend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                dotRend.receiveShadows = false;
                _cableDots[i] = dotRend;
            }

            _hasCable = true;
        }

        /// <summary>Where along the cable's own L-route a dot carrying <paramref name="progress"/> metres
        /// (wrapped to the path's own total length) sits — public so a test can read a resolved dot
        /// position back without re-deriving this formula, same reasoning as Replicator.QueueSlotPosition.</summary>
        public Vector3 PointAlongCable(float progress)
        {
            if (!_hasCable || _cablePathLength <= 0f) return transform.position;
            float t = Mathf.Repeat(progress, _cablePathLength);
            float firstLeg = Vector3.Distance(_cableStart, _cableBend);
            return t <= firstLeg
                ? Vector3.Lerp(_cableStart, _cableBend, firstLeg > 0f ? t / firstLeg : 0f)
                : Vector3.Lerp(_cableBend, _cableEnd, (t - firstLeg) / Mathf.Max(0.0001f, _cablePathLength - firstLeg));
        }

        /// <summary>Advances the cable's travelling dots and, once destroyed, the 0.3 s fade-to-dark —
        /// dt-parameterized like every other beat in this codebase (Replicator.TickConsumption,
        /// MowerHutch.TickMobility) so an EditMode test can drive it directly.</summary>
        public void TickCable(float dt)
        {
            if (!_hasCable) return;

            if (_cableFadeElapsed >= 0f)
            {
                _cableFadeElapsed += dt;
                float alpha = Mathf.Clamp01(1f - _cableFadeElapsed / CableFadeSeconds);
                SetCableAlpha(alpha);
                if (_cableFadeElapsed >= CableFadeSeconds) SetCableVisible(false);
                return;
            }

            _cableDotElapsed += dt;
            float spacing = _cablePathLength / CableDotCount;
            for (int i = 0; i < _cableDots.Length; i++)
            {
                if (_cableDots[i] == null) continue;
                float progress = _cableDotElapsed * CableDotSpeed + spacing * i;
                _cableDots[i].transform.position = PointAlongCable(progress);
            }
        }

        private void SetCableAlpha(float alpha)
        {
            if (_cableMaterial == null) return;
            Color c = CableColor; c.a = alpha;
            _cableMaterial.SetColor("_BaseColor", c);
            _cableMaterial.SetColor("_Color", c);
        }

        private void SetCableVisible(bool visible)
        {
            if (_cableRoot != null) _cableRoot.gameObject.SetActive(visible);
        }

        private void Update()
        {
            TickCable(Time.deltaTime);
        }
    }
}
