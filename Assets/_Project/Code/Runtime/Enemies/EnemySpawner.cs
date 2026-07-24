using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Core;

namespace MaxWorlds.Enemies
{
    /// <summary>
    /// Pools and spawns <see cref="RobotEnemy"/> instances for the slice (YT-36).
    /// Pooling (reuse on death via SetActive) keeps ~20–30 concurrent enemies free
    /// of per-spawn GC churn and leaks. Greybox enemy prefab is assigned in the editor; if none is
    /// set the spawner builds a primitive stand-in so it runs headless.
    ///
    /// Robots leave through the factory's <see cref="FactoryMouth"/> — out of the face pointing at
    /// Max — and then chase him down the lawn (YT-70). They used to appear on a ring centred on
    /// whatever this spawner was attached to, which read as teleporting-in rather than as a stream
    /// pouring out of a source.
    /// </summary>
    public sealed class EnemySpawner : MonoBehaviour
    {
        [SerializeField] private RobotEnemy prefab;

        [Tooltip("Who the stream flows toward (Max). Found by the 'Player' tag if left empty.")]
        [SerializeField] private Transform target;

        [Header("Swarm tuning (YT-63 kiteability — dense but survivable)")]
        [Tooltip("Max robots alive at once, FROM THIS FACTORY. Kept modest so the player can kite " +
                 "instead of drown — and a level with two factories has two of these (YT-92).")]
        // 8, not the 12 a single factory ran: the yard now has two sources, and the number that has to
        // stay survivable is the number of robots on the field, not the number one shed made. Two at 8
        // is a real escalation over one at 12 — more pressure, from two directions — without becoming
        // the wall of bodies that kiteability tuning (YT-63/YT-80) exists to prevent.
        [SerializeField] private int maxLiveEnemies = 8;
        [Tooltip("Seconds between spawns at run start (breathable).")]
        [SerializeField] private float spawnIntervalStart = 1.8f;
        [Tooltip("Seconds between spawns at steady state (peak pressure).")]
        [SerializeField] private float spawnIntervalMin = 1.2f;
        [Tooltip("Seconds over which the cadence ramps from start to min.")]
        [SerializeField] private float rampSeconds = 45f;

        [Header("Mouth (YT-70) — robots pour OUT of the factory, toward Max")]
        [Tooltip("How far in front of the factory a robot lands. Must clear the factory body.")]
        [SerializeField] private float spawnRadius = 3.5f;
        [Tooltip("Half-width of the emission fan, in degrees. Wide enough to read as a stream, " +
                 "narrow enough that nothing appears behind the shed.")]
        [SerializeField] private float mouthHalfAngle = 55f;

        [Header("Mix (YT-66) — two enemy types, so the fight has texture")]
        [Tooltip("Every Nth robot is a bruiser. 1 in 4 keeps them a punctuation mark, not the norm.")]
        [SerializeField] private int bruiserEvery = 4;
        [Tooltip("No bruisers until this many robots have come out — the opening teaches the rusher " +
                 "first, and the bruiser lands as an escalation.")]
        [SerializeField] private int firstBruiserAt = 3;

        // --- Death-throes surge (YT-182) — the wreck's last wave. A shed dying shouldn't just go
        // quiet: it spits out a short burst, and on a roll one Bruiser standing in as the "elite"
        // crawling out of the wreck, so each kill is a spike of danger rather than the quietest
        // moment in the fight. Both numbers scale with the Invasion Level (YT-181) — the same
        // source that died calmly at minute one erupts at minute five. ---
        private const int DeathSurgeBurstMin = 2;          // robots at Invasion Level 0 (run start)

        /// <summary>Robots the death-throes surge spits out at full Invasion Level escalation — the
        /// Settings panel's reference default for a pinned <see cref="DevTuning.DeathSurgeBurstSize"/>.</summary>
        public const int DeathSurgeBurstMax = 5;

        /// <summary>Chance [0,1] the surge includes an elite at full escalation (0 at the run's
        /// start) — the Settings panel's reference default for a pinned
        /// <see cref="DevTuning.DeathSurgeEliteChance"/>.</summary>
        public const float DeathSurgeEliteChanceMax = 0.5f;

        // One pool PER KIND. A single pool would hand a dead bruiser back as the next rusher, still
        // wearing its box body and its collider — the classic pooling bug.
        private readonly Dictionary<EnemyKind, Stack<RobotEnemy>> _pools =
            new Dictionary<EnemyKind, Stack<RobotEnemy>>();
        private readonly List<RobotEnemy> _live = new List<RobotEnemy>(32);
        private float _timer;
        private float _elapsed;
        private int _emitted;
        private Transform _target;
        private Transform _bodies;          // metre-space container (YT-74)
        private Collider[] _playerColliders;

        public int LiveCount => _live.Count;

        // --- The door (YT-108). Optional: with none set this spawner behaves exactly as it did. ---
        private IFactoryDoor _door;

        /// <summary>Route this factory's robots through a real door. Called by the art layer, which
        /// owns the door; gameplay never constructs one.</summary>
        public void UseDoor(IFactoryDoor door) => _door = door;

        /// <summary>A robot is due — the cadence has come round and there is room on the field. The
        /// door watches this to know when to start hauling itself up, so that it is open by the time
        /// the robot is ready rather than the robot waiting on a door that had no reason to move.</summary>
        public bool WantsToEmit =>
            _running && _timer >= CurrentInterval && _live.Count < maxLiveEnemies;

        /// <summary>How many robots this factory has ever put on the field. Only ever goes up, so a
        /// test can prove a dead factory emitted NOTHING — which <see cref="LiveCount"/> can't, since
        /// it also falls as robots die.</summary>
        public int Emitted => _emitted;

        /// <summary>Whether this source is still producing. Goes false exactly once, at
        /// <see cref="Stop"/>, and never comes back.</summary>
        public bool IsRunning => _running;

        private bool _running = true;

        /// <summary>
        /// This source is gone — stop producing, permanently (YT-100).
        ///
        /// This is sticky ON PURPOSE, and it does not use <c>enabled</c>. The factory used to stop its
        /// spawns by switching this component off, which looks right and isn't: <c>enabled</c> is a
        /// shared channel with another writer. <see cref="MaxWorlds.Dev.DevModeController"/> re-asserts
        /// <c>spawner.enabled = !IsSpawnPaused</c> over EVERY spawner EVERY frame, so a destroyed
        /// factory switched itself off and dev mode switched it back on one frame later — and kept
        /// producing robots for the rest of the run. Only reproducible with <c>?dev=1</c>, which is
        /// how the game is play-tested.
        ///
        /// So death is recorded as state this object owns outright, and the spawn path asks that
        /// rather than asking whether someone left the component enabled. Whoever else writes
        /// <c>enabled</c> is now free to keep doing so; it cannot resurrect a dead source.
        /// </summary>
        public void Stop() => _running = false;

        /// <summary>Live count of one kind — lets a test prove the mix actually reaches the field.</summary>
        public int LiveCountOf(EnemyKind kind)
        {
            int n = 0;
            for (int i = 0; i < _live.Count; i++) if (_live[i].Kind == kind) n++;
            return n;
        }

        /// <summary>The authored steady-state interval, for the Settings panel's 100% reference
        /// (YT-170) — same pattern as <see cref="MaxWorlds.Factories.MowerHutch.AuthoredMax"/>.</summary>
        public float AuthoredSpawnIntervalMin => spawnIntervalMin;

        /// <summary>Current seconds-between-spawns for the run time so far. Read live every call, so
        /// a Settings-panel override takes effect on the very next check rather than needing a push
        /// (YT-170) — a flat DevTuning rate replaces the whole start→min ramp outright, since a rate
        /// the player dialled in is the rate they should get, not one more input the ramp blends away.
        ///
        /// The Invasion Level (YT-181) is layered OUTSIDE that override, not inside it: a manual
        /// SpawnInterval pin is Lee dialling in an exact number, and escalation silently speeding
        /// past it would break the one guarantee a pinned slider makes. Left alone, the computed
        /// start→min ramp is further scaled down as the level climbs, so the same shed pumps out
        /// robots faster late in a run without anyone having to touch a slider.</summary>
        public float CurrentInterval =>
            DevTuning.SpawnInterval.HasValue
                ? DevTuning.SpawnInterval.Value
                : SpawnCadence.IntervalAt(_elapsed, spawnIntervalStart, spawnIntervalMin, rampSeconds)
                    * DifficultyDirector.SpawnIntervalMultiplier;

        private void Update()
        {
            if (!_running) return;
            float dt = Time.deltaTime;
            _elapsed += dt;
            _timer += dt;
            if (_timer < CurrentInterval || _live.Count >= maxLiveEnemies) return;

            // Wait for the door, WITHOUT resetting the timer (YT-108) — so the robot comes out on the
            // frame the door finishes opening, not a full interval after it. Holding the timer is
            // what makes the door read as the thing releasing them rather than a shutter that
            // happens to be up.
            if (_door != null && !_door.CanEmit) return;

            _timer = 0f;
            SpawnOne();
        }

        private void SpawnOne()
        {
            // Guarded here too, not just in Update: SpawnOne is also called from outside (the press-kit
            // director reflects it to populate a shot). A dead factory must not emit down ANY path.
            if (!_running) return;
            SpawnKind(EnemyMix.KindFor(_emitted, bruiserEvery, firstBruiserAt));
        }

        /// <summary>
        /// Death-throes surge (YT-182): called once, at the instant this factory's health hits zero —
        /// BEFORE <see cref="Stop"/> latches production off for good, so it rides the same <c>_running</c>
        /// guard as every other spawn path rather than needing one of its own. A short burst emerges
        /// (and, on a roll, one Bruiser standing in as the "elite"), so breaking the source reads as a
        /// spike of danger, not the quietest moment in the fight.
        ///
        /// Burst size and elite chance scale with the Invasion Level (<see cref="DifficultyDirector.Normalized"/>)
        /// unless a Settings-panel override pins them flat — same contract as <see cref="CurrentInterval"/>'s
        /// <see cref="DevTuning.SpawnInterval"/> override. The loop still respects <see cref="maxLiveEnemies"/>:
        /// a factory already near the cap gets a smaller (or no) burst rather than blowing past it.
        /// </summary>
        public void SpawnSurge()
        {
            if (!_running) return;

            int burst = Mathf.RoundToInt(DevTuning.Or(DevTuning.DeathSurgeBurstSize,
                Mathf.Lerp(DeathSurgeBurstMin, DeathSurgeBurstMax, DifficultyDirector.Normalized)));
            float eliteChance = DevTuning.Or(DevTuning.DeathSurgeEliteChance,
                Mathf.Lerp(0f, DeathSurgeEliteChanceMax, DifficultyDirector.Normalized));

            bool eliteSpawned = false;
            for (int i = 0; i < burst && _live.Count < maxLiveEnemies; i++)
            {
                // At most one elite per surge — a wreck coughing up a single tough unit reads as a
                // beat; a wreck coughing up a wall of them reads as a bug.
                bool spawnElite = !eliteSpawned && eliteChance > 0f && Random.value < eliteChance;
                SpawnKind(spawnElite ? EnemyKind.Bruiser : EnemyMix.KindFor(_emitted, bruiserEvery, firstBruiserAt));
                if (spawnElite) eliteSpawned = true;
            }
        }

        /// <summary>The shared spawn machinery: builds/pools, doors it out the mouth, and starts the
        /// emergence walk for one robot of the given <paramref name="kind"/>. <see cref="SpawnOne"/> is
        /// the timer-driven path (asks the mix what's next); <see cref="SpawnSurge"/> is the
        /// death-throes burst (picks the kind itself, so it can force in an elite).</summary>
        private void SpawnKind(EnemyKind kind)
        {
            // Toughened by the Invasion Level (YT-181): read live, so a robot spawned late in a run
            // is tankier and hits harder than the one that came out at the opening bell — the swarm
            // itself is what gets harder, not just how fast it arrives.
            EnemyArchetype archetype = EnemyArchetype.Of(kind).Toughened(DifficultyDirector.ToughnessMultiplier);
            RobotEnemy e = Take(kind, archetype);

            // Out of the mouth, fanned, facing the way it's about to run. With a real door (YT-108)
            // the door decides which way that is — a robot leaving by a doorway on the west wall
            // cannot also be leaving toward a player standing east of it.
            Vector3 mouth = _door != null ? _door.OutwardDirection : ToTarget();
            float halfAngle = _door != null ? _door.FanHalfAngleDeg : mouthHalfAngle;
            Vector3 dir = FactoryMouth.ExitDirection(
                mouth, -transform.forward, _emitted++, halfAngle);

            // It APPEARS at the door and WALKS OUT to the exit point (YT-100). It used to appear at
            // the exit point already — a full body-length clear of the shed, out on the open lawn,
            // fully formed. Nothing was between it and the player at any instant, so there was no
            // frame in which it read as having come from the building; it read as switched on next
            // to it. Born in the doorway with the hutch still between it and the camera, walking
            // itself out, is the whole difference between a source and a spawn point.
            Vector3 exit = FactoryMouth.ExitPoint(
                transform.position, dir, spawnRadius, archetype.SpawnHeight);
            Vector3 door = FactoryMouth.DoorPoint(
                transform.position, dir, transform.lossyScale, archetype.ColliderRadius,
                spawnRadius, archetype.SpawnHeight);

            e.transform.position = door;
            e.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
            e.gameObject.SetActive(true);

            // AFTER SetActive: OnEnable runs ResetState, which puts a pooled robot back into Chase.
            // Told to emerge first, it would be told to chase a frame later and step out of the door
            // by beelining at Max instead.
            e.BeginEmergence(exit);

            // Re-applied on every spawn, not just on creation: Unity drops an ignored collider pair
            // when the collider is disabled, and pooling disables it on every death (YT-74).
            LetThePlayerThrough(e.gameObject);

            _live.Add(e);
        }

        /// <summary>
        /// Robots must never be able to WALL MAX IN (YT-74). They still collide with the world and
        /// with each other — so cover still breaks them up and they still jostle — but the player
        /// walks through their bodies. Contact damage is a distance check, not a collision, so they
        /// keep hurting him exactly as before; what they lose is the ability to pin him in a corner
        /// and hold him there while the rest of the swarm eats him.
        ///
        /// This is a rule about bodies, not about sizes: even correctly-sized robots can box a
        /// player in when a dozen of them converge, and being unable to move is never fun.
        /// </summary>
        private void LetThePlayerThrough(GameObject enemy)
        {
            if (_playerColliders == null || _playerColliders.Length == 0)
            {
                var p = GameObject.FindGameObjectWithTag("Player");
                if (p == null) return;
                _playerColliders = p.GetComponents<Collider>();   // CharacterController is a Collider
            }

            var enemyColliders = enemy.GetComponents<Collider>();
            foreach (var ec in enemyColliders)
            {
                if (ec == null) continue;
                foreach (var pc in _playerColliders)
                {
                    if (pc == null) continue;
                    Physics.IgnoreCollision(ec, pc, true);
                }
            }
        }

        private RobotEnemy Take(EnemyKind kind, in EnemyArchetype archetype)
        {
            if (_pools.TryGetValue(kind, out var pool) && pool.Count > 0)
            {
                // Re-stamp the pooled robot with THIS spawn's archetype (YT-181): the Invasion Level
                // means the archetype changes over the course of a run, and a pooled robot last built
                // when the level was low would otherwise keep carrying stale, softer stats forever.
                RobotEnemy pooled = pool.Pop();
                pooled.Apply(archetype);
                return pooled;
            }
            return CreateInstance(archetype);
        }

        /// <summary>Direction from the factory to Max — the way the stream flows. Zero if there's
        /// nobody to flow toward, which makes the mouth fall back to the factory's own front face.</summary>
        private Vector3 ToTarget()
        {
            if (_target == null) _target = target;
            if (_target == null)
            {
                var p = GameObject.FindGameObjectWithTag("Player");
                if (p != null) _target = p.transform;
            }
            return _target != null ? _target.position - transform.position : Vector3.zero;
        }

        /// <summary>
        /// The container the robots live in. It exists to CANCEL this spawner's own scale: the
        /// spawner rides on the Mower Hutch, whose body is a cube scaled (3, 2, 3), and anything
        /// parented to it inherits that. A 1.9 m bruiser spawned as a 5.7 m cube with a collider to
        /// match, and walled the player in (YT-74). Robots are authored in metres and must stay in
        /// metres, whatever they happen to be emitted from.
        /// </summary>
        private Transform Bodies()
        {
            if (_bodies == null)
                _bodies = ParentScale.MakeMetreSpace(new GameObject("Robots").transform, transform);
            return _bodies;
        }

        /// <summary>Builds one robot of the given kind. The body is the archetype's silhouette, and
        /// the CharacterController is sized to match what you can see — a controller multiplies its
        /// height/radius by the transform's scale, so a scaled body with a default controller fights
        /// a collider that is the wrong shape for it.</summary>
        private RobotEnemy CreateInstance(in EnemyArchetype a)
        {
            RobotEnemy e;
            if (prefab != null)
            {
                // Stats still differ, but both kinds wear the prefab's body. Fine while the prefab
                // is unset (the greybox path below is what ships); revisit when Phase C art lands
                // and each kind needs its own prefab.
                e = Instantiate(prefab, Bodies());
            }
            else
            {
                var go = GameObject.CreatePrimitive(
                    a.Shape == EnemyShape.Box ? PrimitiveType.Cube : PrimitiveType.Capsule);
                go.name = $"RobotEnemy {a.Kind} (stand-in)";
                go.transform.SetParent(Bodies(), false);   // metre space — see Bodies()
                go.transform.localScale = a.BodyScale;

                var cc = go.AddComponent<CharacterController>();
                // Undo the BODY's scale so the metres asked for are the metres you get. The parent
                // contributes nothing now, by construction.
                float lateral = Mathf.Max(a.BodyScale.x, a.BodyScale.z);
                cc.height = a.ColliderHeight / Mathf.Max(a.BodyScale.y, 1e-4f);
                cc.radius = a.ColliderRadius / Mathf.Max(lateral, 1e-4f);
                cc.center = Vector3.zero;   // primitives are centred on their origin

                e = go.AddComponent<RobotEnemy>();
            }

            e.Apply(a);                 // stats — after Awake, which seeded the defaults
            e.Died += OnEnemyDied;
            e.gameObject.SetActive(false);
            return e;
        }

        private void OnEnemyDied(RobotEnemy e)
        {
            _live.Remove(e);
            if (!_pools.TryGetValue(e.Kind, out var pool))
            {
                pool = new Stack<RobotEnemy>();
                _pools[e.Kind] = pool;
            }
            pool.Push(e); // back to its OWN pool; reused on the next spawn of this kind (no leak)
        }
    }
}
