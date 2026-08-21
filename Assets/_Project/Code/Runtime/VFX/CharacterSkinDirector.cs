using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.UI;
using MaxWorlds.Enemies;
using MaxWorlds.Bosses;
using MaxWorlds.Player;
using MaxWorlds.Factories;
using MaxWorlds.Arena;

namespace MaxWorlds.VFX
{
    /// <summary>
    /// Finds every character body and gives it a <see cref="CharacterSkin"/> (YT-61), then routes
    /// damage events to the body that took them.
    ///
    /// MV-527: used to re-scan every <see cref="MeshRenderer"/> in the scene every frame, including
    /// inactive ones. In practice that swept nothing worth the cost: a robot's parts all carry
    /// <see cref="SelfDrivenTint"/> the instant <see cref="RobotRig"/> builds them (in
    /// <see cref="MaxWorlds.Enemies.RobotEnemy.Awake"/>, before this director could ever see them
    /// undressed), and Max's and the boss's real bodies (<c>MaxRig</c>, <c>BigBermudaRig</c>) sit
    /// outside their owner's <see cref="IDamageable"/> hierarchy specifically to dodge this director
    /// (see those classes' own doc comments) — so the only things this ever actually dressed were the
    /// disabled greybox placeholders and the <see cref="MowerHutch"/>/<see cref="AreaGate"/> structure
    /// bodies, all of which are built exactly once, synchronously, during the scene's Awake wave
    /// (<c>BackyardPath.Awake</c> → <c>MapRuntime.Build</c>) and never rebuilt afterward. One sweep in
    /// <see cref="Start"/> — guaranteed by Unity to run after every object's Awake has already fired —
    /// dresses everything a continuous per-frame poll ever caught, at a one-time cost instead of a
    /// forever one.
    ///
    /// Reads state and signals; writes nothing back to gameplay.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CharacterSkinDirector : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            if (FindFirstObjectByType<CharacterSkinDirector>() != null) return;
            new GameObject("CharacterSkins").AddComponent<CharacterSkinDirector>();
        }

        [Tooltip("How close a damage event must be to a body to count as a hit on it. The signal " +
                 "carries a position, not a reference, so this is how a hit finds its victim.")]
        [SerializeField] private float hitMatchRadius = 1.6f;

        private void OnEnable() => HudSignals.DamageDealt += OnDamage;
        private void OnDisable() => HudSignals.DamageDealt -= OnDamage;

        // MV-527: Start, not Update — see the class doc comment for why one sweep is enough.
        private void Start() => DressCharacters();

        /// <summary>
        /// Active objects only (MV-527): nothing this sweep still dresses is ever pooled/deactivated at
        /// scene-build time (see class doc comment), so there's no tan-robot-style case here to guard
        /// against by including inactive renderers.
        /// </summary>
        private void DressCharacters()
        {
            foreach (var r in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                if (r.GetComponent<CharacterSkin>() != null) continue;

                var role = RoleOf(r);
                if (!role.HasValue) continue;

                r.gameObject.AddComponent<CharacterSkin>().Bind(role.Value);
            }
        }

        /// <summary>Null for anything that isn't a character — the world is dressed by
        /// WorldMaterials and must not be claimed here.</summary>
        private static CharacterRole? RoleOf(Renderer r)
        {
            // Gameplay already owns this renderer's property block (the Hutch's pulsing core is the
            // one that matters). Skinning it would put two LateUpdates on the same block and let
            // script order decide whether the "shoot here" tell glows. See SelfDrivenTint.
            if (r.GetComponent<SelfDrivenTint>() != null) return null;

            // includeInactive: true (MV-350 audit) — this sweep deliberately includes inactive renderers
            // (pooled enemies), so the IDamageable lookup has to be able to see an inactive parent too or
            // it silently stops classifying anything on a robot the moment it's pooled. Not the sticky
            // one-way-door bug RuntimeSurfaceDirector had (an untagged renderer here is just retried next
            // frame), but the same mistake in the same shape, so it gets the same fix.
            if (r.GetComponentInParent<IDamageable>(true) == null) return null;

            if (r.GetComponentInParent<PlayerHealth>() != null) return CharacterRole.Player;
            if (r.GetComponentInParent<BigBermudaBoss>() != null) return CharacterRole.Boss;

            // A robot is not just "a robot" any more (YT-86, MV-303): the four ground tiers want
            // different responses — kite a rusher, spend three seconds of held spray on a bruiser, and
            // so on up the ladder — and if they wear the same colour the player has to work out which
            // is which from the shape of a twenty-pixel blob while being chased by all of them. Each
            // gets its own; CharacterSkin.RoleFor is the one place that mapping lives.
            var robot = r.GetComponentInParent<RobotEnemy>();
            if (robot != null) return CharacterSkin.RoleFor(robot.Kind);

            if (r.GetComponentInParent<MowerHutch>() != null) return CharacterRole.Structure;

            // Falling through to here without this check is MV-246: an AreaGate is IDamageable (so it
            // isn't a WorldMaterials surface) but matched none of the roles above, so it fell all the
            // way to the Robot default below and wore the rusher's turquoise as a flat, unlit-looking
            // placeholder. It's a breakable barrier, not a character — Structure gives it the Hutch's
            // painted-steel look and skips the per-frame flash/tint machinery machines don't need.
            if (r.GetComponentInParent<AreaGate>() != null) return CharacterRole.Structure;

            return CharacterRole.Robot;
        }

        /// <summary>
        /// The damage signal carries a world position, not the thing that was hit, so the flash is
        /// routed to the nearest enemy body. That is a compromise, and it's a deliberate one: the
        /// alternative is reaching into RobotEnemy to raise a proper "I was hit" event, and enemy
        /// code belongs to the gameplay stream.
        ///
        /// It only ever matches enemies, so a hit can never flash Max.
        /// </summary>
        private void OnDamage(Vector3 pos, float amount, bool crit)
        {
            // Any ENEMY, not just a rusher. Asking for one specific role was survivable while there was
            // only one enemy role in the game; the moment the bruiser got its own (YT-86) it would have
            // meant the toughest thing in the fight took three seconds of spray without ever once
            // flashing to say the water was landing.
            var skin = CharacterSkin.NearestEnemy(pos, hitMatchRadius);
            if (skin != null) skin.Flash();
        }
    }
}
