using System;
using System.Collections;
using UnityEngine;

namespace MaxWorlds.Audio
{
    /// <summary>Which world's loop/intensity layer to render (MV-1008) — matches
    /// <see cref="MaxWorlds.Arena.Map.WorldLibrary.Keys"/>'s 0-based ordering, so a caller holding
    /// <c>BackyardPath.ResolvedWorldIndex</c> converts with a plain cast/clamp.</summary>
    public enum MusicWorld { Backyard = 0, Stormdrain = 1, Reef = 2 }

    /// <summary>
    /// MV-1008: procedural music, same "nothing originated by Lee, no outside tools" idiom as
    /// <see cref="ProcSfx"/> (MV-1007) — a table of authored knobs per world, rendered by simple
    /// additive oscillator math (sine/square/saw partials, one-pole low-pass, noise for the hat) into
    /// a seamless 32-bar loop. Deterministic (seeded <see cref="System.Random"/> only, no
    /// <c>UnityEngine.Random</c>/<c>Time</c> reads in the render path) so the same world always
    /// renders the same buffer, which is what lets <c>MusicDirector</c> cache a rendered clip across
    /// scene reloads instead of re-rendering every visit.
    ///
    /// Every layer's pitch/rhythm is derived from sample index via closed-form modulo arithmetic
    /// against the bar/beat grid (an 8-bar chord progression repeating 4x across the 32-bar loop, a
    /// drum pattern repeating every bar, a lead note grid with a seeded random-walk over scale
    /// degrees) rather than a discrete event list — every layer is therefore already exactly periodic
    /// within the loop. That alone isn't enough to guarantee a click-free join, though: a naive
    /// sawtooth (<see cref="SawWave"/>) has its own hard, once-per-cycle phase-wrap discontinuity
    /// wherever it happens to fall, with no reason to avoid landing exactly on the loop boundary. So
    /// the actual join guarantee is <see cref="ApplyDeclick"/> — a short fade to silence at both the
    /// very start and very end of the buffer — which bounds the join to (near) zero regardless of what
    /// any layer's raw waveform is doing there.
    /// </summary>
    public static class ProcMusic
    {
        public const int SampleRate = 22050;
        public const int BarsPerLoop = 32;
        private const int BeatsPerBar = 4;
        private const int ProgressionBars = 8;   // divides BarsPerLoop — the progression itself loops seamlessly

        // Short fade to silence at the very start and very end of the loop (see ApplyDeclick) — this,
        // not phase-matching the oscillators, is what the AC's "join without a discontinuity" leans on.
        public const float DeclickSeconds = 0.02f;

        private const float PadWeight = 0.30f;
        private const float BassWeight = 0.32f;
        private const float KickWeight = 0.45f;
        private const float HatWeight = 0.22f;
        private const float LeadWeight = 0.26f;
        private const float IntensityMultiplier = 2f;   // "drums + bass doubled" — MV-1008's intensity layer

        private const float MinTargetPeak = 0.5f;
        private const float MaxTargetPeak = 0.92f;

        private struct WorldSpec
        {
            public float Bpm;
            public float BassRootHz;
            public int[] Scale;              // semitone offsets from the root, one octave
            public int[] Progression;        // ProgressionBars scale-degree indices (mod Scale.Length)
            public int PadDegreeOffset;      // second pad tone = chord degree + this, mod Scale.Length
            public float PadDetuneCents;
            public float PadLowpassHz;
            public bool PadSwell;            // slow amplitude LFO (Reef)
            public bool HasKick;
            public bool HasHat;
            public bool ShuffleHat;          // light swing on the off-beat hat (Backyard)
            public float HatLowpassHz;
            public SfxWaveform BassWaveform;
            public SfxWaveform LeadWaveform;
            public float LeadOctaveMultiplier;
            public float LeadSlotBeats;
            public float LeadNoteChance;
            public int Seed;
        }

        // C major pentatonic / D natural minor / A natural minor — degree 0 is always the root.
        private static readonly int[] MajorPentatonic = { 0, 2, 4, 7, 9 };
        private static readonly int[] NaturalMinor = { 0, 2, 3, 5, 7, 8, 10 };

        private static readonly WorldSpec[] Specs =
        {
            // World 1 Backyard: C major pentatonic, 96 BPM, warm pad, light shuffle drums.
            new WorldSpec
            {
                Bpm = 96f, BassRootHz = 130.81f /* C3 */, Scale = MajorPentatonic,
                Progression = new[] { 0, 0, 3, 3, 4, 4, 0, 0 },
                PadDegreeOffset = 2, PadDetuneCents = 6f, PadLowpassHz = 1400f, PadSwell = false,
                HasKick = true, HasHat = true, ShuffleHat = true, HatLowpassHz = 3200f,
                BassWaveform = SfxWaveform.Sine, LeadWaveform = SfxWaveform.Square,
                LeadOctaveMultiplier = 4f, LeadSlotBeats = 1f, LeadNoteChance = 0.45f, Seed = 19608,
            },
            // World 2 Stormdrain: D minor, 84 BPM, dub-industrial — heavy bass, echoey metallic hat.
            new WorldSpec
            {
                Bpm = 84f, BassRootHz = 73.42f /* D2 */, Scale = NaturalMinor,
                Progression = new[] { 0, 0, 0, 0, 3, 3, 0, 0 },
                PadDegreeOffset = 3, PadDetuneCents = 9f, PadLowpassHz = 900f, PadSwell = false,
                HasKick = true, HasHat = true, ShuffleHat = false, HatLowpassHz = 7000f,
                BassWaveform = SfxWaveform.Square, LeadWaveform = SfxWaveform.Saw,
                LeadOctaveMultiplier = 2f, LeadSlotBeats = 2f, LeadNoteChance = 0.30f, Seed = 20608,
            },
            // World 3 Reef: A minor, 70 BPM, ethereal — slow pad swells, sparse bell lead, no kick.
            new WorldSpec
            {
                Bpm = 70f, BassRootHz = 110.0f /* A2 */, Scale = NaturalMinor,
                Progression = new[] { 0, 0, 4, 4, 2, 2, 0, 0 },
                PadDegreeOffset = 4, PadDetuneCents = 4f, PadLowpassHz = 700f, PadSwell = true,
                HasKick = false, HasHat = false, ShuffleHat = false, HatLowpassHz = 0f,
                BassWaveform = SfxWaveform.Sine, LeadWaveform = SfxWaveform.Sine,
                LeadOctaveMultiplier = 4f, LeadSlotBeats = 3f, LeadNoteChance = 0.35f, Seed = 21608,
            },
        };

        private static float BarSeconds(MusicWorld w) => BeatsPerBar * 60f / Specs[(int)w].Bpm;

        /// <summary>Exact loop length in samples — 32 bars at this world's BPM, rounded to the nearest
        /// sample (the AC's "±1 sample" tolerance exists for this rounding).</summary>
        public static int LoopSampleCount(MusicWorld w) =>
            Mathf.RoundToInt(BarSeconds(w) * BarsPerLoop * SampleRate);

        /// <summary>Renders the base loop, synchronously — used by tests and by anything that doesn't
        /// need the frame-spread path. <see cref="RenderIncremental"/> is what a running game actually
        /// calls (spread across frames per the ticket's frame-budget requirement).</summary>
        public static float[] RenderLoop(MusicWorld w) => RenderMix(w, intensity: false);

        /// <summary>Renders the boss-intensity layer (drums + bass doubled, everything else silent),
        /// synchronously — meant to play additively over <see cref="RenderLoop"/>, not replace it.</summary>
        public static float[] RenderIntensityLayer(MusicWorld w) => RenderMix(w, intensity: true);

        public static AudioClip RenderLoopClip(MusicWorld w) => BuildStereoClip($"Music_{w}", RenderLoop(w));

        public static AudioClip RenderIntensityClip(MusicWorld w) =>
            BuildStereoClip($"MusicIntensity_{w}", RenderIntensityLayer(w));

        /// <summary>Same render as <see cref="RenderLoop"/>/<see cref="RenderIntensityLayer"/>, spread
        /// across frames so no single frame does more than <paramref name="chunkSamples"/> samples'
        /// worth of oscillator math — the ticket's "no frame over 16.6 ms" budget. Real background
        /// threads aren't an option here: WebGL (this project's other shipping target alongside iOS)
        /// has no <c>System.Threading</c> support, so frame-spreading is the one approach that works on
        /// every platform this project builds for.</summary>
        public static IEnumerator RenderIncremental(MusicWorld w, bool intensity, Action<float[]> onComplete, int chunkSamples = 8820)
        {
            int loopSamples = LoopSampleCount(w);
            var mix = new float[loopSamples];

            for (int start = 0; start < loopSamples; start += chunkSamples)
            {
                int end = Mathf.Min(start + chunkSamples, loopSamples);
                RenderRange(w, intensity, mix, start, end);
                yield return null;
            }

            ApplyDeclick(mix);
            NormalizePeak(mix);
            onComplete?.Invoke(mix);
        }

        private static float[] RenderMix(MusicWorld w, bool intensity)
        {
            int loopSamples = LoopSampleCount(w);
            var mix = new float[loopSamples];

            RenderRange(w, intensity, mix, 0, loopSamples);
            ApplyDeclick(mix);
            NormalizePeak(mix);
            return mix;
        }

        /// <summary>Fills <paramref name="mix"/>[start, end) with every enabled layer's contribution at
        /// those sample indices. Every layer is a closed-form function of sample time (bar/beat modulo
        /// arithmetic, a precomputed seeded lead sequence) rather than stateful oscillator phase, so
        /// range [start,end) can be rendered independently of any other range — which is exactly what
        /// lets <see cref="RenderIncremental"/> split the work across frames.</summary>
        private static void RenderRange(MusicWorld w, bool intensity, float[] mix, int start, int end)
        {
            var spec = Specs[(int)w];
            float bpm = spec.Bpm;
            float beatSeconds = 60f / bpm;
            float barSeconds = BeatsPerBar * beatSeconds;

            int[] leadSequence = BuildLeadSequence(spec);
            float leadSlotSeconds = spec.LeadSlotBeats * beatSeconds;

            for (int i = start; i < end; i++)
            {
                float t = i / (float)SampleRate;
                int barIndex = Mathf.FloorToInt(t / barSeconds);
                float tInBar = t - barIndex * barSeconds;
                int beatInBar = Mathf.FloorToInt(tInBar / beatSeconds);
                float tInBeat = tInBar - beatInBar * beatSeconds;

                int progIndex = spec.Progression[((barIndex % ProgressionBars) + ProgressionBars) % ProgressionBars];
                float chordRootHz = spec.BassRootHz * SemitoneRatio(spec.Scale[Mod(progIndex, spec.Scale.Length)]);

                float sample = 0f;

                if (!intensity)
                {
                    sample += PadSample(spec, chordRootHz, t, tInBar, barSeconds) * PadWeight;
                    sample += LeadSample(spec, leadSequence, t, leadSlotSeconds) * LeadWeight;
                }

                float bassWeight = intensity ? BassWeight * IntensityMultiplier : BassWeight;
                sample += BassSample(spec, chordRootHz, tInBar, barSeconds) * bassWeight;

                float kickWeight = intensity ? KickWeight * IntensityMultiplier : KickWeight;
                float hatWeight = intensity ? HatWeight * IntensityMultiplier : HatWeight;
                if (spec.HasKick) sample += KickSample(beatInBar, tInBeat) * kickWeight;
                if (spec.HasHat) sample += HatSample(spec, beatInBar, tInBeat, beatSeconds) * hatWeight;

                mix[i] = sample;
            }
        }

        // --- layers ---

        private static float PadSample(WorldSpec spec, float chordRootHz, float t, float tInBar, float barSeconds)
        {
            float secondDegreeHz = spec.BassRootHz * 2f *
                SemitoneRatio(spec.Scale[Mod(spec.PadDegreeOffset, spec.Scale.Length)]);

            float detune = spec.PadDetuneCents / 1200f;   // cents -> octave fraction
            float a = SawWave(chordRootHz * Mathf.Pow(2f, detune), t) + SawWave(chordRootHz * Mathf.Pow(2f, -detune), t);
            float b = SawWave(secondDegreeHz * Mathf.Pow(2f, detune), t) + SawWave(secondDegreeHz * Mathf.Pow(2f, -detune), t);
            float raw = (a + b) * 0.25f;

            float envelope = 1f;
            if (spec.PadSwell)
            {
                // Slow amplitude LFO, one full swell per bar — "slow pad swells" (Reef).
                envelope = 0.55f + 0.45f * Mathf.Sin(2f * Mathf.PI * (tInBar / barSeconds));
            }

            return Dull(raw, spec.PadLowpassHz, 4000f) * envelope;
        }

        private static float BassSample(WorldSpec spec, float chordRootHz, float tInBar, float barSeconds)
        {
            float bassHz = chordRootHz * 0.5f;   // one octave below the pad/chord root
            float phase = Frac(bassHz * tInBar);

            float raw = spec.BassWaveform switch
            {
                SfxWaveform.Square => phase < 0.5f ? 1f : -1f,
                _ => Mathf.Sin(2f * Mathf.PI * phase),
            };

            const float attack = 0.02f;
            const float release = 0.08f;
            float envelope = 1f;
            if (tInBar < attack) envelope = tInBar / attack;
            else if (tInBar > barSeconds - release) envelope = Mathf.Clamp01((barSeconds - tInBar) / release);

            return raw * envelope;
        }

        private static float KickSample(int beatInBar, float tInBeat)
        {
            // One kick per beat — a short pitched-down thump.
            const float duration = 0.22f;
            if (beatInBar != 0 && beatInBar != 2) return 0f;
            if (tInBeat >= duration) return 0f;

            const float attack = 0.004f;
            float envelope = tInBeat < attack ? tInBeat / attack : Mathf.Clamp01(1f - (tInBeat - attack) / (duration - attack));
            float freq = Mathf.Lerp(160f, 45f, Mathf.Clamp01(tInBeat / duration));
            return Mathf.Sin(2f * Mathf.PI * freq * tInBeat) * envelope;
        }

        private static float HatSample(WorldSpec spec, int beatInBar, float tInBeat, float beatSeconds)
        {
            // Two hats per beat (the off-beat 8th note); ShuffleHat delays the second by a fixed
            // fraction of the beat for a light swing feel (Backyard).
            float half = beatSeconds * 0.5f;
            float shuffleDelay = spec.ShuffleHat ? beatSeconds * 0.08f : 0f;

            float tSinceHit;
            if (tInBeat < half) tSinceHit = tInBeat;
            else tSinceHit = tInBeat - half - shuffleDelay;

            const float duration = 0.06f;
            if (tSinceHit < 0f || tSinceHit >= duration) return 0f;

            const float attack = 0.002f;
            float envelope = tSinceHit < attack ? tSinceHit / attack : Mathf.Clamp01(1f - (tSinceHit - attack) / (duration - attack));

            // Deterministic pseudo-noise (no UnityEngine.Random) — a couple of summed high-ish sines
            // reads as metallic hiss once low-passed, distinct per world via HatLowpassHz.
            float raw = Mathf.Sin(2f * Mathf.PI * 3371f * tSinceHit) * 0.6f + Mathf.Sin(2f * Mathf.PI * 5417f * tSinceHit) * 0.4f;
            return Dull(raw, spec.HatLowpassHz, 8000f) * envelope;
        }

        private static float LeadSample(WorldSpec spec, int[] leadSequence, float t, float leadSlotSeconds)
        {
            int slot = Mathf.FloorToInt(t / leadSlotSeconds);
            int degree = leadSequence[Mod(slot, leadSequence.Length)];
            if (degree < 0) return 0f;   // a rest slot

            float tInSlot = t - slot * leadSlotSeconds;
            const float duration = 0.9f;
            float noteLen = Mathf.Min(duration, leadSlotSeconds * 0.9f);
            if (tInSlot >= noteLen) return 0f;

            float hz = spec.BassRootHz * spec.LeadOctaveMultiplier * SemitoneRatio(spec.Scale[degree]);
            const float attack = 0.015f;
            float envelope = tInSlot < attack ? tInSlot / attack : Mathf.Clamp01(1f - (tInSlot - attack) / (noteLen - attack));

            float raw = spec.LeadWaveform switch
            {
                SfxWaveform.Square => Frac(hz * tInSlot) < 0.5f ? 1f : -1f,
                SfxWaveform.Saw => 2f * Frac(hz * tInSlot) - 1f,
                _ => Mathf.Sin(2f * Mathf.PI * hz * tInSlot),
            };
            return raw * envelope;
        }

        /// <summary>Deterministic seeded random walk over scale degrees, one slot per
        /// <see cref="WorldSpec.LeadSlotBeats"/> across the whole 32-bar loop — "sparse lead
        /// (pentatonic, random-walk seeded)". Computed once per render (cheap: tens of slots, not
        /// samples) and reused for every sample the loop touches.</summary>
        private static int[] BuildLeadSequence(WorldSpec spec)
        {
            float beatSeconds = 60f / spec.Bpm;
            float barSeconds = BeatsPerBar * beatSeconds;
            float loopSeconds = barSeconds * BarsPerLoop;
            float leadSlotSeconds = spec.LeadSlotBeats * beatSeconds;
            int slotCount = Mathf.Max(1, Mathf.CeilToInt(loopSeconds / leadSlotSeconds));

            var rng = new System.Random(spec.Seed);
            var sequence = new int[slotCount];
            int degree = 0;
            for (int i = 0; i < slotCount; i++)
            {
                if (rng.NextDouble() > spec.LeadNoteChance) { sequence[i] = -1; continue; }
                degree = Mathf.Clamp(degree + rng.Next(-1, 2), 0, spec.Scale.Length - 1);
                sequence[i] = degree;
            }
            return sequence;
        }

        // --- shared DSP helpers ---

        private static float SawWave(float hz, float t) => 2f * Frac(hz * t) - 1f;

        private static float Frac(float x) => x - Mathf.Floor(x);

        private static int Mod(int a, int m) => ((a % m) + m) % m;

        private static float SemitoneRatio(int semitones) => Mathf.Pow(2f, semitones / 12f);

        /// <summary>Cheap brightness shaping, evaluated as a pure function of the raw sample rather than
        /// a true stateful RC filter — no running state to carry across a frame-spread render's chunk
        /// boundaries. <paramref name="fullBrightnessHz"/> is the cutoff at which this stops attenuating
        /// at all; below it, quieter in proportion to how far under the cutoff sits. Not a real filter,
        /// just "duller than the raw waveform", which is all the pad drone/hat noise need.</summary>
        private static float Dull(float raw, float cutoffHz, float fullBrightnessHz)
        {
            if (cutoffHz <= 0f) return raw;
            float k = Mathf.Clamp01(cutoffHz / fullBrightnessHz);
            return raw * (0.3f + 0.7f * k);
        }

        /// <summary>Fades the first and last <see cref="DeclickSeconds"/> of the buffer to/from silence
        /// — the loop-join guarantee. The very first and very last samples land at exactly 0 regardless
        /// of what any layer's raw waveform was doing there (a naive <see cref="SawWave"/> in
        /// particular has its own hard, once-per-cycle phase-wrap discontinuity that has no reason to
        /// avoid the loop boundary), which is what makes the AC's "join without a discontinuity greater
        /// than 0.05" hold unconditionally rather than depending on where in each oscillator's cycle the
        /// boundary happens to fall.</summary>
        private static void ApplyDeclick(float[] buf)
        {
            int fadeSamples = Mathf.Min(buf.Length / 2, Mathf.RoundToInt(DeclickSeconds * SampleRate));
            for (int i = 0; i < fadeSamples; i++)
            {
                buf[i] *= (float)i / fadeSamples;
                int tailIndex = buf.Length - 1 - i;
                buf[tailIndex] *= (float)i / fadeSamples;
            }
        }

        private static void NormalizePeak(float[] buf)
        {
            float peak = 0f;
            for (int i = 0; i < buf.Length; i++) peak = Mathf.Max(peak, Mathf.Abs(buf[i]));
            if (peak < 1e-6f) return;
            float target = Mathf.Clamp(peak, MinTargetPeak, MaxTargetPeak);
            float scale = target / peak;
            if (Mathf.Approximately(scale, 1f)) return;
            for (int i = 0; i < buf.Length; i++) buf[i] *= scale;
        }

        /// <summary>Duplicates a mono buffer into an interleaved stereo <see cref="AudioClip"/> — the
        /// same simplification <see cref="RenderLoopClip"/>/<see cref="RenderIntensityClip"/> use, and
        /// what <c>MusicDirector</c> calls on a buffer that came back from <see cref="RenderIncremental"/>
        /// (which only hands back the raw samples, not a clip, since building an <see cref="AudioClip"/>
        /// is itself a main-thread-only call the incremental path can't do off in a background chunk).</summary>
        public static AudioClip BuildStereoClip(string name, float[] mono)
        {
            var interleaved = new float[mono.Length * 2];
            for (int i = 0; i < mono.Length; i++)
            {
                interleaved[i * 2] = mono[i];
                interleaved[i * 2 + 1] = mono[i];
            }
            var clip = AudioClip.Create(name, mono.Length, 2, SampleRate, false);
            clip.SetData(interleaved, 0);
            return clip;
        }
    }
}
