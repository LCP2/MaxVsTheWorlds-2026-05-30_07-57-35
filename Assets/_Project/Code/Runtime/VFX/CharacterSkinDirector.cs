using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.UI;
using MaxWorlds.Enemies;
using MaxWorlds.Bosses;
using MaxWorlds.Player;
using MaxWorlds.Factories;

namespace MaxWorlds.VFX
{
    /// <summary>
    /// Finds every character body and gives it a <see cref="CharacterSkin"/> (YT-61), then routes
    /// damage events to the body that took them.
    ///
    /// It scans every <see cref="ScanIntervalSeconds"/>, not every frame (YT-186). Once a frame was
    /// affordable when the field held a handful of bodies; <c>FindObjectsByType&lt;MeshRenderer&gt;</c>
    /// walks EVERY renderer in the scene (world geometry, VFX quads, every pooled robot ever created,
    /// active or not) and with four factories able to hold ~20 concurrent robots that sweep, 60
    /// times a second, was one of the biggest single frame costs in the 60→30fps regression. Enemies
    /// are still pooled and a robot is still created and activated on the SAME frame, but a body a
    /// few frames late to its skin is invisible where the sweep itself was not — the interval is
    /// short enough that nothing ever reads as wearing Unity's magenta default.
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

        /// <summary>Seconds between scene sweeps (YT-186). Short enough that a freshly-activated
        /// robot is dressed well within the time it takes to notice, far cheaper than every frame.</summary>
        private const float ScanIntervalSeconds = 0.15f;
        private float _scanTimer;

        private void OnEnable()
        {
            HudSignals.DamageDealt += OnDamage;
            _scanTimer = 0f;
            DressCharacters(); // catch whatever already exists the instant this director wakes up
        }

        private void OnDisable() => HudSignals.DamageDealt -= OnDamage;

        private void Update()
        {
            _scanTimer += Time.deltaTime;
            if (_scanTimer < ScanIntervalSeconds) return;
            _scanTimer = 0f;
            DressCharacters();
        }

        /// <summary>
        /// Include INACTIVE objects: pooled enemies sit deactivated between lives, and catching them
        /// there means they are already skinned the moment they are switched on.
        /// </summary>
        private void DressCharacters()
        {
            foreach (var r in FindObjectsByType<MeshRenderer>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
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

            if (r.GetComponentInParent<IDamageable>() == null) return null;

            if (r.GetComponentInParent<PlayerHealth>() != null) return CharacterRole.Player;
            if (r.GetComponentInParent<BigBermudaBoss>() != null) return CharacterRole.Boss;

            // A robot is not just "a robot" any more (YT-86): the rusher and the bruiser are opposites
            // — one is small, fast and dies quickly, the other is a fridge that takes three seconds of
            // held spray — and if they wear the same colour the player has to work out which is which
            // from the shape of a twenty-pixel blob while being chased by both. They get their own.
            var robot = r.GetComponentInParent<RobotEnemy>();
            if (robot != null)
            {
                return robot.Kind == EnemyKind.Bruiser ? CharacterRole.Bruiser : CharacterRole.Robot;
            }

            if (r.GetComponentInParent<MowerHutch>() != null) return CharacterRole.Structure;

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
