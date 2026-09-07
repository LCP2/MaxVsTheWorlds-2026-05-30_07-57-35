using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Rendering;

namespace MaxWorlds.Enemies
{
    /// <summary>
    /// A Sludge Drone's death puddle (MV-705) — a temporary, runtime-spawned version of MV-692's
    /// map-authored <c>sludge[]</c> slow zones: anything standing inside it while it lives is slowed to
    /// <see cref="SpeedMultiplier"/> of normal speed, the same idea a map-authored sludge rect already
    /// applies, but timed rather than permanent. Consulted by
    /// <see cref="MaxWorlds.Arena.MapSlowZones.SpeedMultiplierAt"/> alongside the map's own static
    /// zones, so both <see cref="MaxWorlds.Player.PlayerController"/> and <see cref="RobotEnemy"/> slow
    /// inside it through the one shared hook — except a Sludger itself, which
    /// <see cref="RobotEnemy.EffectiveMoveSpeed"/> exempts outright (the ticket's own "immune to sludge
    /// slow").
    ///
    /// Deliberately does NOT rely on OnEnable/OnDestroy to maintain <see cref="_active"/> — Unity does
    /// not reliably run those outside Play mode (the same lesson <c>RobotEnemy.Active</c>'s own doc
    /// comment and half this project's EditMode tests already learned) — <see cref="Spawn"/> and
    /// <see cref="Tick"/> add/remove directly instead, so the registry is correct under a synchronous
    /// EditMode test too.
    /// </summary>
    public sealed class SludgePuddle : MonoBehaviour
    {
        /// <summary>The ticket's own slow amount — same value <see cref="MaxWorlds.Arena.WorldDials.sludgeSpeedMultiplier"/>
        /// defaults to, kept as its own constant rather than threaded through from a live world config:
        /// this puddle is a self-contained timed hazard, not an authored map feature.</summary>
        public const float SpeedMultiplier = 0.6f;

        private static readonly List<SludgePuddle> _active = new List<SludgePuddle>(4);

        private float _radius;
        private float _remaining;

        public float Radius => _radius;
        public float RemainingLife => _remaining;

        public static SludgePuddle Spawn(Vector3 position, float radius, float duration)
        {
            var go = new GameObject("SludgePuddle (stand-in)");
            go.transform.position = position;
            BuildVisual(go.transform, radius);

            var puddle = go.AddComponent<SludgePuddle>();
            puddle.Init(radius, duration);
            return puddle;
        }

        /// <summary>The ticket's own acid-green puddle colour — the same hue family as
        /// <see cref="MaxWorlds.VFX.CharacterSkin"/>'s Sludger body, so the hazard reads as one thing
        /// (drone → puddle), not two unrelated effects.</summary>
        private static readonly Color PuddleColor = new Color(0.42f, 0.58f, 0.05f);

        private static void BuildVisual(Transform parent, float radius)
        {
            // Same reason as every other free-flying hazard's visual in this roster (MV-350).
            parent.gameObject.AddComponent<KeepsOwnMaterial>();

            var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            disc.name = "Puddle";
            var col = disc.GetComponent<Collider>();
            if (col != null)
            {
                if (Application.isPlaying) Destroy(col);
                else DestroyImmediate(col);
            }
            disc.transform.SetParent(parent, false);
            disc.transform.localPosition = new Vector3(0f, 0.02f, 0f);
            // Unity's cylinder primitive is 1 unit in radius at scale 1 (2 units across) — a flat
            // puddle, so height (Y) stays tiny while X/Z scale to the authored radius.
            disc.transform.localScale = new Vector3(radius * 2f, 0.02f, radius * 2f);

            Material mat = MaterialLibrary.Tinted(SurfaceKind.Metal, PuddleColor);
            if (mat != null) disc.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }

        private void Init(float radius, float duration)
        {
            _radius = radius;
            _remaining = duration;
            _active.Add(this);
        }

        private void Update() => Tick(Time.deltaTime);

        /// <summary>One evaluation: count down this puddle's own lifetime, self-destroying (and
        /// unregistering) once it expires. Public and dt-parameterized (same idiom as
        /// <see cref="CorrosionPuddle.Tick"/>) so an EditMode test can drive it directly — <c>Update()</c>
        /// never runs outside Play mode and this project authors no PlayMode tests.</summary>
        public void Tick(float dt)
        {
            _remaining -= dt;
            if (_remaining <= 0f)
            {
                _active.Remove(this);
                if (Application.isPlaying) Destroy(gameObject);
                else DestroyImmediate(gameObject);
            }
        }

        /// <summary>Pure so the puddle's own dwell check is testable without a scene or a clock.</summary>
        public static bool InRadius(Vector3 puddlePosition, Vector3 receiverPosition, float radius)
        {
            float dx = puddlePosition.x - receiverPosition.x, dz = puddlePosition.z - receiverPosition.z;
            return dx * dx + dz * dz <= radius * radius;
        }

        /// <summary>The slowest multiplier among every live puddle containing <paramref name="worldPosition"/>
        /// (1 = unaffected) — consulted by <see cref="MaxWorlds.Arena.MapSlowZones.SpeedMultiplierAt"/>
        /// alongside the map's own static sludge zones.</summary>
        public static float SpeedMultiplierAt(Vector3 worldPosition)
        {
            float best = 1f;
            for (int i = 0; i < _active.Count; i++)
            {
                SludgePuddle puddle = _active[i];
                if (puddle == null) continue;
                if (InRadius(puddle.transform.position, worldPosition, puddle._radius))
                    best = Mathf.Min(best, SpeedMultiplier);
            }
            return best;
        }

        /// <summary>Test/level-reset hygiene, same idiom as <see cref="RobotEnemy.ResetRegistry"/> —
        /// belt-and-braces against a puddle whose <see cref="Tick"/> hasn't expired it yet when the next
        /// level (or test) starts.</summary>
        public static void ResetRegistry() => _active.Clear();
    }
}
