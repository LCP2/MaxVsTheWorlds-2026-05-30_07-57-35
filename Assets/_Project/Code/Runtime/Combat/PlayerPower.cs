using UnityEngine;
using MaxWorlds.UI;

namespace MaxWorlds.Combat
{
    /// <summary>
    /// Turns a level-up into actual power (YT-67). Rides on the Water Blaster — attached by it in
    /// Awake, like the VFX, so there is nothing to wire in a scene.
    ///
    /// It re-derives the gadget's numbers from <see cref="PowerRamp"/> at the CURRENT level rather
    /// than nudging them upward each time. So a double level-up still lands the right power, a
    /// missed signal self-heals on the next one, and re-applying is never compounding.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(WaterBlaster))]
    public sealed class PlayerPower : MonoBehaviour
    {
        private WaterBlaster _blaster;

        /// <summary>Max's current level. 1 = the run's starting power.</summary>
        public int Level { get; private set; } = 1;

        public float DamageMultiplier => PowerRamp.DamageMultiplier(Level);
        public float FireRateMultiplier => PowerRamp.FireRateMultiplier(Level);

        private void Awake() => _blaster = GetComponent<WaterBlaster>();

        private void OnEnable() => HudSignals.LevelUp += OnLevelUp;

        // HudSignals is static: a missed unsubscribe would keep this alive across a scene reload
        // and quietly double-apply the ramp on the next run.
        private void OnDisable() => HudSignals.LevelUp -= OnLevelUp;

        private void OnLevelUp(int level, Vector3 _) => SetLevel(level);

        /// <summary>Set the level and push the resulting power into the gadget.</summary>
        public void SetLevel(int level)
        {
            Level = Mathf.Max(1, level);
            if (_blaster == null) _blaster = GetComponent<WaterBlaster>();
            if (_blaster != null) _blaster.ApplyPower(DamageMultiplier, FireRateMultiplier);
        }
    }
}
