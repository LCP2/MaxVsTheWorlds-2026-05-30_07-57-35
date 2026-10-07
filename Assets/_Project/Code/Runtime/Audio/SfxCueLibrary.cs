using System;
using System.Collections.Generic;

namespace MaxWorlds.Audio
{
    /// <summary>
    /// MV-1007's cue table: which <see cref="HudSignals"/> event (or component state, for the two
    /// that aren't HudSignals events) maps to which synthesised sound, and how often it may re-trigger
    /// — the contract from the ticket's "Cue map" section, expressed as data so
    /// <see cref="SfxDirector"/> is pure wiring and <c>ProcSfxTests</c> can render/inspect every preset
    /// without touching the director at all.
    /// </summary>
    public static class SfxCueLibrary
    {
        public enum Cue
        {
            DamageDealt,
            LppePulseFired,
            PlayerHit,
            Pickup,
            SupercellCollected,
            EnemyKilled,
            FactoryDestroyed,
            FittingDestroyed,
            RocketMuzzle,
            RocketImpact,
            MissileImpact,
            ShockPulseLanded,
            MaxTeleported,
            BlinkerTeleported,
            BossEngaged,
            BossDefeated,
            WeaponCoreCollected,
            FinaleGateCrossed,
            HoseLoop,        // WaterBlaster.IsEmitting — polled, not a HudSignals event
            ForceFieldUp,
            ForceFieldPop,
            UiClick,
        }

        public static readonly Cue[] AllCues = (Cue[])Enum.GetValues(typeof(Cue));

        /// <summary>Plain-word label for each cue, in this enum's declared order (MV-1007's cue-table
        /// order) — the Settings panel SOUND tab's source of truth for row names (MV-1009), so a cue
        /// added later gets a labelled toggle automatically as long as it's given a label here.</summary>
        public static readonly IReadOnlyDictionary<Cue, string> DisplayNames = new Dictionary<Cue, string>
        {
            { Cue.DamageDealt, "Hose hit" },
            { Cue.LppePulseFired, "Laser zap" },
            { Cue.PlayerHit, "Max hit" },
            { Cue.Pickup, "Pickup" },
            { Cue.SupercellCollected, "Supercell chime" },
            { Cue.EnemyKilled, "Robot destroyed" },
            { Cue.FactoryDestroyed, "Factory explosion" },
            { Cue.FittingDestroyed, "Fitting destroyed" },
            { Cue.RocketMuzzle, "Rocket launch" },
            { Cue.RocketImpact, "Rocket impact" },
            { Cue.MissileImpact, "Missile impact" },
            { Cue.ShockPulseLanded, "Shock pulse" },
            { Cue.MaxTeleported, "Max teleport" },
            { Cue.BlinkerTeleported, "Blinker teleport" },
            { Cue.BossEngaged, "Boss horn" },
            { Cue.BossDefeated, "Boss defeated" },
            { Cue.WeaponCoreCollected, "Weapon Core fanfare" },
            { Cue.FinaleGateCrossed, "Finale gate crossed" },
            { Cue.HoseLoop, "Hose loop" },
            { Cue.ForceFieldUp, "Force Field up" },
            { Cue.ForceFieldPop, "Force Field pop" },
            { Cue.UiClick, "UI click" },
        };

        /// <summary>Voices per second the table caps a cue at. A cue with no explicit table limit
        /// (the rows marked "—") gets a generous cap — real scarcity there comes from
        /// <see cref="SfxDirector"/>'s 16-voice pool, not a per-cue throttle.</summary>
        public static readonly IReadOnlyDictionary<Cue, int> RateLimitPerSecond = new Dictionary<Cue, int>
        {
            { Cue.DamageDealt, 10 },
            { Cue.LppePulseFired, 12 },
            { Cue.PlayerHit, 6 },
            { Cue.Pickup, 12 },
            { Cue.SupercellCollected, 20 },
            { Cue.EnemyKilled, 8 },
            { Cue.FactoryDestroyed, 16 },
            { Cue.FittingDestroyed, 16 },
            { Cue.RocketMuzzle, 10 },
            { Cue.RocketImpact, 10 },
            { Cue.MissileImpact, 6 },
            { Cue.ShockPulseLanded, 6 },
            { Cue.MaxTeleported, 16 },
            { Cue.BlinkerTeleported, 16 },
            { Cue.BossEngaged, 16 },
            { Cue.BossDefeated, 16 },
            { Cue.WeaponCoreCollected, 16 },
            { Cue.FinaleGateCrossed, 16 },
            { Cue.ForceFieldUp, 16 },
            { Cue.ForceFieldPop, 16 },
            { Cue.UiClick, 15 },
        };

        private static SfxPreset P(SfxWaveform wf, float freq = 0f, float slide = 0f,
            float vibDepth = 0f, float vibRate = 0f,
            float attack = 0.005f, float sustain = 0.02f, float decay = 0.08f,
            float lowPass = 0f, float duty = 0.5f, float volume = 0.7f, int seed = 0)
            => new SfxPreset
            {
                Waveform = wf, BaseFreq = freq, FreqSlide = slide,
                VibratoDepth = vibDepth, VibratoRate = vibRate,
                Attack = attack, Sustain = sustain, Decay = decay,
                LowPassCutoff = lowPass, Duty = duty, Volume = volume, Seed = seed,
            };

        /// <summary>Every cue's preset sequence — one note for a plain hit/impact, several for a
        /// blip/chime/fanfare. See the ticket's own table for the character each is aiming for.</summary>
        public static readonly IReadOnlyDictionary<Cue, SfxPreset[]> Presets = new Dictionary<Cue, SfxPreset[]>
        {
            // "short soft noise 'tss'"
            [Cue.DamageDealt] = new[]
            {
                P(SfxWaveform.Noise, attack: 0.002f, sustain: 0.02f, decay: 0.05f, lowPass: 3000f, volume: 0.55f, seed: 1),
            },

            // "laser zap (square, falling slide)"
            [Cue.LppePulseFired] = new[]
            {
                P(SfxWaveform.Square, freq: 1600f, slide: -3500f, attack: 0.002f, sustain: 0.01f, decay: 0.05f, duty: 0.4f, volume: 0.6f),
            },

            // "low thud (sine 110 Hz, fast decay)"
            [Cue.PlayerHit] = new[]
            {
                P(SfxWaveform.Sine, freq: 110f, slide: -20f, attack: 0.001f, sustain: 0.01f, decay: 0.12f, volume: 0.8f),
            },

            // "rising two-note blip"
            [Cue.Pickup] = new[]
            {
                P(SfxWaveform.Sine, freq: 500f, attack: 0.002f, sustain: 0.01f, decay: 0.05f, volume: 0.5f),
                P(SfxWaveform.Sine, freq: 800f, attack: 0.002f, sustain: 0.01f, decay: 0.07f, volume: 0.65f),
            },

            // "bright 3-note chime"
            [Cue.SupercellCollected] = new[]
            {
                P(SfxWaveform.Sine, freq: 600f, attack: 0.002f, sustain: 0.02f, decay: 0.14f, volume: 0.55f),
                P(SfxWaveform.Sine, freq: 900f, attack: 0.002f, sustain: 0.02f, decay: 0.14f, volume: 0.65f),
                P(SfxWaveform.Sine, freq: 1200f, attack: 0.002f, sustain: 0.02f, decay: 0.18f, volume: 0.75f),
            },

            // "small noise burst + low pop"
            [Cue.EnemyKilled] = new[]
            {
                P(SfxWaveform.Noise, attack: 0.001f, sustain: 0.02f, decay: 0.05f, lowPass: 2500f, volume: 0.5f, seed: 2),
                P(SfxWaveform.Sine, freq: 180f, slide: -60f, attack: 0.001f, sustain: 0.01f, decay: 0.06f, volume: 0.55f),
            },

            // "big noise explosion, long decay"
            [Cue.FactoryDestroyed] = new[]
            {
                P(SfxWaveform.Noise, attack: 0.005f, sustain: 0.05f, decay: 0.6f, lowPass: 1200f, volume: 0.9f, seed: 3),
            },

            // shares FactoryDestroyed's "big noise explosion" character, scaled down — a fitting is a
            // small turret, not a building (same distinction CombatVfx.OnFittingDestroyed draws).
            [Cue.FittingDestroyed] = new[]
            {
                P(SfxWaveform.Noise, attack: 0.003f, sustain: 0.02f, decay: 0.25f, lowPass: 1500f, volume: 0.7f, seed: 4),
            },

            // "whoosh"
            [Cue.RocketMuzzle] = new[]
            {
                P(SfxWaveform.Noise, attack: 0.03f, sustain: 0.01f, decay: 0.05f, lowPass: 3500f, volume: 0.5f, seed: 5),
            },

            // "crunchy pop"
            [Cue.RocketImpact] = new[]
            {
                P(SfxWaveform.Noise, attack: 0.001f, sustain: 0.01f, decay: 0.12f, lowPass: 1800f, volume: 0.8f, seed: 6),
            },

            // "deep boom"
            [Cue.MissileImpact] = new[]
            {
                P(SfxWaveform.Sine, freq: 55f, slide: -10f, attack: 0.002f, sustain: 0.05f, decay: 0.4f, volume: 0.9f),
            },

            // "electric crackle"
            [Cue.ShockPulseLanded] = new[]
            {
                P(SfxWaveform.Saw, freq: 300f, vibDepth: 150f, vibRate: 40f, attack: 0.001f, sustain: 0.05f, decay: 0.1f, lowPass: 4000f, volume: 0.6f),
            },

            // "upward warp"
            [Cue.MaxTeleported] = new[]
            {
                P(SfxWaveform.Sine, freq: 200f, slide: 1800f, attack: 0.01f, sustain: 0.05f, decay: 0.12f, volume: 0.7f),
            },

            // "downward warp"
            [Cue.BlinkerTeleported] = new[]
            {
                P(SfxWaveform.Sine, freq: 900f, slide: -1800f, attack: 0.01f, sustain: 0.05f, decay: 0.1f, volume: 0.6f),
            },

            // "low two-tone horn"
            [Cue.BossEngaged] = new[]
            {
                P(SfxWaveform.Saw, freq: 90f, attack: 0.02f, sustain: 0.25f, decay: 0.15f, volume: 0.8f),
                P(SfxWaveform.Saw, freq: 110f, attack: 0.02f, sustain: 0.25f, decay: 0.2f, volume: 0.8f),
            },

            // "long explosion + rising tone"
            [Cue.BossDefeated] = new[]
            {
                P(SfxWaveform.Noise, attack: 0.01f, sustain: 0.1f, decay: 0.9f, lowPass: 1000f, volume: 0.9f, seed: 7),
                P(SfxWaveform.Sine, freq: 200f, slide: 900f, attack: 0.02f, sustain: 0.2f, decay: 0.3f, volume: 0.7f),
            },

            // "5-note arpeggio fanfare"
            [Cue.WeaponCoreCollected] = new[]
            {
                P(SfxWaveform.Sine, freq: 500f, attack: 0.002f, sustain: 0.04f, decay: 0.1f, volume: 0.5f),
                P(SfxWaveform.Sine, freq: 650f, attack: 0.002f, sustain: 0.04f, decay: 0.1f, volume: 0.55f),
                P(SfxWaveform.Sine, freq: 800f, attack: 0.002f, sustain: 0.04f, decay: 0.1f, volume: 0.6f),
                P(SfxWaveform.Sine, freq: 1000f, attack: 0.002f, sustain: 0.04f, decay: 0.12f, volume: 0.65f),
                P(SfxWaveform.Sine, freq: 1300f, attack: 0.002f, sustain: 0.04f, decay: 0.18f, volume: 0.75f),
            },

            // "whoosh" — a rise-then-fall amplitude envelope (attack then decay, no sustain) is what
            // reads as a sweep rather than a flat burst.
            [Cue.FinaleGateCrossed] = new[]
            {
                P(SfxWaveform.Noise, attack: 0.25f, sustain: 0f, decay: 0.25f, lowPass: 2200f, volume: 0.55f, seed: 8),
            },

            // Looped by SfxDirector, not played one-shot — fade in/out is SfxDirector's own volume
            // ramp against WaterBlaster.IsEmitting, not baked into the clip.
            [Cue.HoseLoop] = new[]
            {
                P(SfxWaveform.Noise, attack: 0.01f, sustain: 0.3f, decay: 0.01f, lowPass: 1600f, volume: 0.35f, seed: 9),
            },

            // "rising shimmer"
            [Cue.ForceFieldUp] = new[]
            {
                P(SfxWaveform.Sine, freq: 400f, slide: 500f, vibDepth: 30f, vibRate: 12f, attack: 0.05f, sustain: 0.1f, decay: 0.1f, volume: 0.5f),
            },

            // "glassy burst"
            [Cue.ForceFieldPop] = new[]
            {
                P(SfxWaveform.Noise, attack: 0.001f, sustain: 0.02f, decay: 0.15f, lowPass: 5000f, volume: 0.8f, seed: 10),
            },

            // "soft click"
            [Cue.UiClick] = new[]
            {
                P(SfxWaveform.Square, freq: 1000f, attack: 0.001f, sustain: 0.005f, decay: 0.01f, duty: 0.3f, volume: 0.35f),
            },
        };

        /// <summary>Renders one cue to a playable clip — <see cref="SfxDirector"/> calls this once per
        /// cue at boot.</summary>
        public static UnityEngine.AudioClip RenderClip(Cue cue) => ProcSfx.RenderClip(cue.ToString(), Presets[cue]);

        /// <summary>MV-1134: prefers a generated clip under <c>Resources/Audio/Sfx/&lt;cue&gt;</c> when
        /// one has been committed, falling back to the synthesised <see cref="RenderClip"/> for every
        /// cue that hasn't been generated yet — the synthesised sound is the permanent fallback, not a
        /// placeholder being phased out.</summary>
        public static UnityEngine.AudioClip ResolveClip(Cue cue)
        {
            var generated = UnityEngine.Resources.Load<UnityEngine.AudioClip>("Audio/Sfx/" + cue);
            return generated != null ? generated : RenderClip(cue);
        }
    }
}
