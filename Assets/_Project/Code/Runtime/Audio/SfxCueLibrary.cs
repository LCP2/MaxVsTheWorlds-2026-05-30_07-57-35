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
            UndertowLoop,    // MV-1136: Undertow.IsEmitting — polled, same as HoseLoop.
            SentinelArrived, // MV-1159: must stay before every cue added after it (SfxDirector.IsCueMuted
                              // masks by 1 << (int)cue; inserting a cue anywhere but the end would shift
                              // every existing player's mute switches) — appending new cues after it is
                              // fine, only inserting one BEFORE it would shift its own ordinal.

            // MV-1237: the Mothership's own ship/robot cues — appended, never inserted, per the note above.
            LightMissileLaunch,
            LightMissileHit,
            OrbitalsHit,
            CradleBuildTick,
            CradleStepOut,
            CradleDestroyed,
            SkitterHop,
            BreakerChainSnap,
            BreakerPunch,
            BreakerRam,
            TractorBeam,
            VentFire,
            HelmsmanLensSweep,
            ViewportRising,
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
            { Cue.UndertowLoop, "Beam loop" },
            { Cue.SentinelArrived, "Sentinel arrival" },

            // MV-1237: the Mothership's own ship/robot cues.
            { Cue.LightMissileLaunch, "Light Missile launch" },
            { Cue.LightMissileHit, "Light Missile hit" },
            { Cue.OrbitalsHit, "Orbitals hit" },
            { Cue.CradleBuildTick, "Cradle build tick" },
            { Cue.CradleStepOut, "Cradle step-out" },
            { Cue.CradleDestroyed, "Cradle destroyed" },
            { Cue.SkitterHop, "Skitter hop" },
            { Cue.BreakerChainSnap, "Breaker chain snap" },
            { Cue.BreakerPunch, "Breaker punch" },
            { Cue.BreakerRam, "Breaker ram" },
            { Cue.TractorBeam, "Tractor beam" },
            { Cue.VentFire, "Vent fire" },
            { Cue.HelmsmanLensSweep, "Helmsman lens sweep" },
            { Cue.ViewportRising, "Viewport rising" },
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
            { Cue.SentinelArrived, 4 }, // MV-1159: lets two Sentinels arriving close together both be heard

            // MV-1237: the Mothership's own ship/robot cues.
            { Cue.LightMissileLaunch, 10 },
            { Cue.LightMissileHit, 8 },
            { Cue.OrbitalsHit, 12 },
            { Cue.CradleBuildTick, 4 },
            { Cue.CradleStepOut, 4 },
            { Cue.CradleDestroyed, 16 },
            { Cue.SkitterHop, 8 },
            { Cue.BreakerChainSnap, 6 },
            { Cue.BreakerPunch, 8 },
            { Cue.BreakerRam, 6 },
            { Cue.VentFire, 10 },
            { Cue.HelmsmanLensSweep, 6 },
            { Cue.ViewportRising, 4 },
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

            // MV-1136: HoseLoop's preset, lowpassed darker for the beam's hum — only the fallback for
            // when the generated file is missing, same as HoseLoop's own comment.
            [Cue.UndertowLoop] = new[]
            {
                P(SfxWaveform.Noise, attack: 0.01f, sustain: 0.3f, decay: 0.01f, lowPass: 800f, volume: 0.35f, seed: 9),
            },

            // MV-1159: "rising whoosh swelling into a bright vroom burst and a deep bass thump" — the
            // fallback for when the generated file is missing, same as every other cue's. Element [0]'s
            // Volume is also what ElevenLabsAudioGenerator.GenerateOne scales the GENERATED clip's peak
            // to (see its ScaleToPeak call) - set to 0.95 so the real asset lands at or above
            // FactoryDestroyed's 0.9, matching the ticket's AC4 ("one of the loudest one-shots").
            [Cue.SentinelArrived] = new[]
            {
                P(SfxWaveform.Sine, freq: 150f, slide: 2200f, attack: 0.05f, sustain: 0.4f, decay: 0.3f, volume: 0.95f),
                P(SfxWaveform.Noise, attack: 0.01f, sustain: 0.1f, decay: 0.4f, lowPass: 2000f, volume: 0.95f, seed: 11),
                P(SfxWaveform.Sine, freq: 60f, slide: -15f, attack: 0.01f, sustain: 0.05f, decay: 0.5f, volume: 0.95f),
            },

            // MV-1237: the Mothership's own ship/robot cues.

            // "whoosh with a magenta-toned rising edge" — a brighter, faster slide than the
            // Shoulder Rack's plain RocketMuzzle whoosh, so Light Missile reads as its own weapon.
            [Cue.LightMissileLaunch] = new[]
            {
                P(SfxWaveform.Noise, attack: 0.02f, sustain: 0.01f, decay: 0.07f, lowPass: 4200f, volume: 0.5f, seed: 12),
            },

            // "sharp magenta impact" — shares RocketImpact's crunchy-pop shape, brighter low-pass.
            [Cue.LightMissileHit] = new[]
            {
                P(SfxWaveform.Noise, attack: 0.001f, sustain: 0.01f, decay: 0.1f, lowPass: 2400f, volume: 0.75f, seed: 13),
            },

            // "bright metallic tink" — the gold orbs' own contact beat.
            [Cue.OrbitalsHit] = new[]
            {
                P(SfxWaveform.Sine, freq: 1400f, slide: -200f, attack: 0.001f, sustain: 0.01f, decay: 0.08f, volume: 0.5f),
            },

            // "soft mechanical tick" — the gantry's own build-stage advance, not a hit.
            [Cue.CradleBuildTick] = new[]
            {
                P(SfxWaveform.Square, freq: 700f, attack: 0.002f, sustain: 0.005f, decay: 0.03f, duty: 0.2f, volume: 0.4f),
            },

            // "clank + servo whine" — the finished robot stepping off the bay onto the out pad.
            [Cue.CradleStepOut] = new[]
            {
                P(SfxWaveform.Square, freq: 220f, attack: 0.002f, sustain: 0.02f, decay: 0.05f, duty: 0.35f, volume: 0.5f),
                P(SfxWaveform.Sine, freq: 900f, slide: -400f, attack: 0.01f, sustain: 0.02f, decay: 0.08f, volume: 0.4f),
            },

            // shares FactoryDestroyed's "big noise explosion" character — the cradle is the Mothership's
            // own factory tier (same CradleHealth as MowerHutch/Replicator).
            [Cue.CradleDestroyed] = new[]
            {
                P(SfxWaveform.Noise, attack: 0.005f, sustain: 0.05f, decay: 0.55f, lowPass: 1300f, volume: 0.9f, seed: 14),
            },

            // "quick skittering hop" — a short noise burst with a rising pitch, insect-like.
            [Cue.SkitterHop] = new[]
            {
                P(SfxWaveform.Noise, attack: 0.002f, sustain: 0.01f, decay: 0.04f, lowPass: 5000f, volume: 0.45f, seed: 15),
            },

            // "metallic chain rattle + snap" — the Breaker's collar breaking free.
            [Cue.BreakerChainSnap] = new[]
            {
                P(SfxWaveform.Noise, attack: 0.001f, sustain: 0.03f, decay: 0.1f, lowPass: 6000f, volume: 0.6f, seed: 16),
                P(SfxWaveform.Square, freq: 500f, slide: -250f, attack: 0.001f, sustain: 0.01f, decay: 0.06f, duty: 0.3f, volume: 0.55f),
            },

            // "heavy fist thud" — lower and shorter than BreakerRam.
            [Cue.BreakerPunch] = new[]
            {
                P(SfxWaveform.Sine, freq: 140f, slide: -40f, attack: 0.001f, sustain: 0.01f, decay: 0.1f, volume: 0.75f),
            },

            // "big body-slam crash" — louder and longer than BreakerPunch, with a noise crunch layered in.
            [Cue.BreakerRam] = new[]
            {
                P(SfxWaveform.Sine, freq: 90f, slide: -25f, attack: 0.002f, sustain: 0.03f, decay: 0.25f, volume: 0.85f),
                P(SfxWaveform.Noise, attack: 0.002f, sustain: 0.02f, decay: 0.2f, lowPass: 1800f, volume: 0.7f, seed: 17),
            },

            // "sustained electromagnetic hum" — a pulsing saw drone, meant to be looped by whatever plays
            // it, same "fallback preset, loop behaviour lives on the caller" shape as HoseLoop/UndertowLoop.
            [Cue.TractorBeam] = new[]
            {
                P(SfxWaveform.Saw, freq: 180f, vibDepth: 20f, vibRate: 6f, attack: 0.05f, sustain: 0.3f, decay: 0.05f, lowPass: 2200f, volume: 0.4f),
            },

            // "gas vent hiss/burst" — shares DamageDealt's noise-tss shape, longer and lower.
            [Cue.VentFire] = new[]
            {
                P(SfxWaveform.Noise, attack: 0.01f, sustain: 0.15f, decay: 0.2f, lowPass: 3500f, volume: 0.5f, seed: 18),
            },

            // "lens sweep" — a slow rising-then-falling sine sweep, a scanner reading the room.
            [Cue.HelmsmanLensSweep] = new[]
            {
                P(SfxWaveform.Sine, freq: 300f, slide: 260f, attack: 0.1f, sustain: 0.08f, decay: 0.2f, volume: 0.35f),
            },

            // "mechanical rising groan" — the viewport glass lifting into place, dull and slow.
            [Cue.ViewportRising] = new[]
            {
                P(SfxWaveform.Square, freq: 70f, slide: 50f, attack: 0.1f, sustain: 0.15f, decay: 0.15f, lowPass: 900f, duty: 0.3f, volume: 0.55f),
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
