using System;
using UnityEngine;

namespace MaxWorlds.UI
{
    /// <summary>
    /// XP / level track behind the top-centre XP strip (YT-30 HUD). Pure logic so the
    /// level-rollover maths is unit-testable. Awarding XP fills the strip; crossing the
    /// per-level threshold rolls over (carrying the remainder) and bumps the level. The
    /// per-level requirement grows linearly for the slice — a real curve is a later tune.
    /// </summary>
    public sealed class XpTrack
    {
        private readonly int _baseRequirement;
        private readonly int _perLevelGrowth;

        public int Level { get; private set; }
        public int XpIntoLevel { get; private set; }

        /// <summary>Fired on any XP change (level, into-level). HUD subscribes to repaint.</summary>
        public event Action Changed;

        public XpTrack(int baseRequirement = 20, int perLevelGrowth = 10, int startLevel = 1)
        {
            _baseRequirement = Mathf.Max(1, baseRequirement);
            _perLevelGrowth = Mathf.Max(0, perLevelGrowth);
            Level = Mathf.Max(1, startLevel);
            XpIntoLevel = 0;
        }

        /// <summary>XP needed to clear the current level.</summary>
        public int RequirementForLevel => _baseRequirement + (Level - 1) * _perLevelGrowth;

        /// <summary>Fill of the XP strip, 0..1.</summary>
        public float Normalized =>
            RequirementForLevel > 0 ? Mathf.Clamp01((float)XpIntoLevel / RequirementForLevel) : 0f;

        /// <summary>Award XP, rolling levels over as thresholds are crossed.</summary>
        /// <returns>Number of levels gained (0 if none).</returns>
        public int Add(int amount)
        {
            if (amount <= 0) return 0;
            int levelsGained = 0;
            XpIntoLevel += amount;
            while (XpIntoLevel >= RequirementForLevel)
            {
                XpIntoLevel -= RequirementForLevel;
                Level++;
                levelsGained++;
            }
            Changed?.Invoke();
            return levelsGained;
        }
    }
}
