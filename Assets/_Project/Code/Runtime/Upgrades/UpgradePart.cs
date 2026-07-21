using UnityEngine;

namespace MaxWorlds.Upgrades
{
    /// <summary>
    /// One hose upgrade part, as far as the upgrade SCREEN needs to know it (YT-132): a name to
    /// reveal, a short code to stamp on its icon, and an accent colour. The five concrete parts and
    /// the effect each one applies to the weapon are YT-133 — this ticket builds the reveal-and-fit
    /// flow, and drives it with a generic placeholder until those land.
    ///
    /// A value type so a collected part can be handed around and shown without allocating.
    /// </summary>
    public readonly struct UpgradePart
    {
        /// <summary>Full name revealed on the upgrade screen, e.g. "BEAM NOZZLE".</summary>
        public readonly string Name;

        /// <summary>Short code stamped on the part icon (a couple of letters).</summary>
        public readonly string Code;

        /// <summary>Accent colour for the part's icon and reveal glow.</summary>
        public readonly Color Accent;

        public UpgradePart(string name, string code, Color accent)
        {
            Name = name;
            Code = code;
            Accent = accent;
        }

        /// <summary>Whether this is a real part (has a name). A default(UpgradePart) is "none".</summary>
        public bool IsValid => !string.IsNullOrEmpty(Name);

        /// <summary>The placeholder shown for YT-132 before YT-133 defines the real five — gold, the
        /// same colour the world pickup and the HUD "PART" chip use.</summary>
        public static readonly UpgradePart Generic =
            new UpgradePart("HOSE PART", "PART", new Color(0.98f, 0.72f, 0.22f));
    }
}
