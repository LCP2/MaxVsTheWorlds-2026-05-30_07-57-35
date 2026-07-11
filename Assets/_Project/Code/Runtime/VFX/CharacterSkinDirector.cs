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
    /// It scans every frame, not on a timer. That matters: enemies are pooled and a robot is created
    /// and activated on the SAME frame, so a once-a-second sweep left it wearing Unity's magenta
    /// default material while it charged at you. The scan is cheap — a handful of bodies — and being
    /// a frame late is invisible where being a second late is not.
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

        private void Update() => DressCharacters();

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
            if (r.GetComponentInParent<IDamageable>() == null) return null;

            if (r.GetComponentInParent<PlayerHealth>() != null) return CharacterRole.Player;
            if (r.GetComponentInParent<BigBermudaBoss>() != null) return CharacterRole.Boss;
            if (r.GetComponentInParent<RobotEnemy>() != null) return CharacterRole.Robot;
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
            var skin = CharacterSkin.NearestTo(pos, hitMatchRadius, CharacterRole.Robot);
            if (skin != null) skin.Flash();
        }
    }
}
