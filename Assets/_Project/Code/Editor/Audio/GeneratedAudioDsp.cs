using UnityEngine;

namespace MaxWorlds.Editor.Audio
{
    /// <summary>
    /// MV-1134: pure sample-buffer transforms applied to a generated sound effect after it comes back
    /// from ElevenLabs, before it's written to disk as a WAV. Kept as static functions with no Unity
    /// API dependency beyond <see cref="Mathf"/> so they're cheap to call from
    /// <see cref="ElevenLabsAudioGenerator"/> and easy to reason about in isolation.
    /// </summary>
    public static class GeneratedAudioDsp
    {
        /// <summary>Averages an interleaved multi-channel buffer down to mono. A buffer already at
        /// <paramref name="channelCount"/> 1 is returned unchanged.</summary>
        public static float[] ToMono(float[] interleaved, int channelCount)
        {
            if (channelCount <= 1) return interleaved;

            int frameCount = interleaved.Length / channelCount;
            var mono = new float[frameCount];
            for (int i = 0; i < frameCount; i++)
            {
                float sum = 0f;
                for (int c = 0; c < channelCount; c++) sum += interleaved[i * channelCount + c];
                mono[i] = sum / channelCount;
            }
            return mono;
        }

        /// <summary>Drops samples before the first one whose magnitude exceeds <paramref name="threshold"/>,
        /// keeping <paramref name="keepSeconds"/> of buffer immediately before it. A buffer that never
        /// crosses the threshold (effectively silent) is returned unchanged rather than emptied.</summary>
        public static float[] TrimLeadingSilence(float[] samples, int sampleRate, float threshold = 0.02f, float keepSeconds = 0.002f)
        {
            int first = -1;
            for (int i = 0; i < samples.Length; i++)
            {
                if (Mathf.Abs(samples[i]) > threshold) { first = i; break; }
            }
            if (first < 0) return samples;

            int keepSamples = Mathf.RoundToInt(keepSeconds * sampleRate);
            int start = Mathf.Max(0, first - keepSamples);
            if (start == 0) return samples;

            var trimmed = new float[samples.Length - start];
            System.Array.Copy(samples, start, trimmed, 0, trimmed.Length);
            return trimmed;
        }

        /// <summary>Drops samples after the last one whose magnitude exceeds <paramref name="threshold"/>.
        /// A buffer that never crosses the threshold is returned unchanged.</summary>
        public static float[] TrimTrailingSilence(float[] samples, float threshold = 0.005f)
        {
            int last = -1;
            for (int i = samples.Length - 1; i >= 0; i--)
            {
                if (Mathf.Abs(samples[i]) > threshold) { last = i; break; }
            }
            if (last < 0 || last == samples.Length - 1) return samples;

            var trimmed = new float[last + 1];
            System.Array.Copy(samples, 0, trimmed, 0, trimmed.Length);
            return trimmed;
        }

        /// <summary>Applies a linear fade in and out, in place, each <paramref name="fadeSeconds"/> long
        /// (clamped to half the buffer so the two fades never overlap on a very short clip).</summary>
        public static void FadeInOut(float[] samples, int sampleRate, float fadeSeconds = 0.003f)
        {
            int fadeSamples = Mathf.Min(samples.Length / 2, Mathf.RoundToInt(fadeSeconds * sampleRate));
            for (int i = 0; i < fadeSamples; i++)
            {
                float t = (float)i / fadeSamples;
                samples[i] *= t;
                samples[samples.Length - 1 - i] *= t;
            }
        }

        /// <summary>Rescales the whole buffer, in place, so its peak sample lands exactly at
        /// <paramref name="targetPeak"/>. A buffer that is effectively silent is left unchanged rather
        /// than amplifying noise floor into something audible.</summary>
        public static void ScaleToPeak(float[] samples, float targetPeak)
        {
            float peak = 0f;
            for (int i = 0; i < samples.Length; i++) peak = Mathf.Max(peak, Mathf.Abs(samples[i]));
            if (peak < 1e-6f) return;

            float scale = targetPeak / peak;
            for (int i = 0; i < samples.Length; i++) samples[i] *= scale;
        }
    }
}
