// MV-854: ported literally from the L layout in buildNew() and the hair() function in
// C:\Dev\MaxVsTheWorlds-Images\max-lookdev.html ("New · title reveal", Lee's approved prototype).
// Change the prototype and re-port by hand; there is no auto-emit step for this.
using System.Collections.Generic;
using UnityEngine;

namespace MaxWorlds.VFX
{
    /// <summary>MV-854: one lock's authored layout — where it roots on the scalp, which way it hugs
    /// the head at rest, and which way it streams once the spring settles. Ported literally from the
    /// <c>L</c> array <c>buildNew()</c> assembles before calling <c>hair()</c> — see <see cref="MaxHair"/>
    /// for the rest of the port.</summary>
    public readonly struct HairLockSpec
    {
        public readonly Vector3 RootLocal;
        public readonly Vector3 Hug;
        public readonly Vector3 Flow;
        public readonly float Length;
        public readonly float RootWidth;
        public readonly bool Fringe;
        public readonly bool Back;
        public readonly float Seed;

        public HairLockSpec(Vector3 rootLocal, Vector3 hug, Vector3 flow, float length, float rootWidth,
                            bool fringe, bool back, float seed)
        {
            RootLocal = rootLocal; Hug = hug; Flow = flow; Length = length; RootWidth = rootWidth;
            Fringe = fringe; Back = back; Seed = seed;
        }
    }

    /// <summary>MV-854: one lock's live spring state — a direction and a velocity per segment, carried
    /// frame to frame. <see cref="MaxHair.SegCount"/> segments bridge the lock's
    /// <see cref="MaxHair.NodeCount"/> nodes.</summary>
    public sealed class HairLockState
    {
        public readonly Vector3[] SegDir = new Vector3[MaxHair.SegCount];
        public readonly Vector3[] SegVel = new Vector3[MaxHair.SegCount];

        /// <summary>Starts each segment at the same hug→flow blend its own target will pull it toward
        /// — the prototype's own <c>segs.push({dir: hug.lerp(flow, w)...})</c> — so a lock never
        /// spawns pointing somewhere its own spring immediately fights.</summary>
        public HairLockState(in HairLockSpec spec)
        {
            for (int k = 0; k < MaxHair.SegCount; k++)
            {
                float w = (float)(k + 1) / MaxHair.SegCount;
                SegDir[k] = Vector3.Lerp(spec.Hug, spec.Flow, w).normalized;
                SegVel[k] = Vector3.zero;
            }
        }
    }

    /// <summary>
    /// MV-854: Max's hair — 31 flowing locks, ported literally from <c>buildNew()</c>'s lock layout
    /// and <c>hair()</c>'s motion in the approved prototype (<c>max-lookdev.html</c>, Lee's title-
    /// reveal reference). Everything here is pure — no <see cref="Transform"/>, no
    /// <see cref="GameObject"/> — so an EditMode test can drive the spring maths directly, the same
    /// "expose the resolved value" contract <see cref="MaxRig.AimPose"/> and <see cref="MaxRig.Stride"/>
    /// already make for this rig. The GameObject/Mesh side of this — one merged dynamic mesh, one draw
    /// call for all 31 locks — is <see cref="MaxHairRig"/>.
    ///
    /// Everything below is in HEAD-LOCAL space, the same convention <see cref="MaxBody"/>'s own hair
    /// cap already draws in (a bare local Vector3 like (0, 1.72, -0.06) IS a point in the Head pivot's
    /// own space) — so a lock never has to know the head is yawing, bobbing or leaning.
    /// </summary>
    public static class MaxHair
    {
        /// <summary>Nodes per lock (<c>buildNew()</c>'s own K). 2 ribbon vertices per node.</summary>
        public const int NodeCount = 7;

        /// <summary>Springs per lock — one bridging each pair of adjacent nodes.</summary>
        public const int SegCount = NodeCount - 1;

        /// <summary>26 scalp locks survive the layout loop below (three rows, face left clear) plus 5
        /// fringe locks — the ticket's own "31 locks".</summary>
        public const int LockCount = 31;

        /// <summary>Nodes are never allowed closer to the head than this, in head-local metres — keeps
        /// a lock from ever passing through the skull.</summary>
        public const float HeadClearance = 0.235f;

        /// <summary>Where a lock's root sits, out from the head centre, before the spring ever moves it
        /// — <c>buildNew()</c>'s own 0.215.</summary>
        private const float RootDistance = 0.215f;

        /// <summary>The head centre every lock roots and clears against — <c>buildNew()</c>'s own
        /// <c>hairC</c>, in the Head pivot's local space.</summary>
        public static readonly Vector3 HeadCenterLocal = new Vector3(0f, 1.72f, -0.04f);

        /// <summary>The shared flow direction a scalp lock bends toward — <c>buildNew()</c>'s own FLOW.</summary>
        private static readonly Vector3 SharedFlow = new Vector3(0.55f, -0.85f, -0.35f).normalized;

        /// <summary>Where a fringe lock sweeps instead — across the brow, to one side.</summary>
        private static readonly Vector3 FringeFlow = new Vector3(1f, -0.45f, 0.3f).normalized;

        /// <summary>World-space wind direction, per the ticket's own constant. Grepping "Wind" turns up
        /// only per-material shader sway amplitudes with no direction of their own — <see cref="MaxWorlds.Rendering.BiomePalette.GroundWindLean"/>
        /// leans the lawn's texture sampling, and the foliage shader's own wind strength
        /// (<c>SurfaceProfile.Wind</c>, see <c>WindTests</c>/<c>WindVisibilityTests</c>) is a bend
        /// amplitude in metres, not a world vector — so there is no scene-wide wind value to read
        /// instead, and the prototype's own constant is what ships.</summary>
        public static readonly Vector3 WindDirWorld = new Vector3(1f, 0f, 0.35f).normalized;

        /// <summary>Wind strength, per the ticket's own constant (see <see cref="WindDirWorld"/>'s doc
        /// for why there is no scene value to read instead).</summary>
        public const float WindStrengthDefault = 0.55f;

        /// <summary>The prototype's own pseudo-random hash: <c>((sin(i*12.9898)*43758.5453)%1+1)%1</c>,
        /// always in [0, 1) — ported literally so the layout below is bit-for-bit the same shape, not
        /// just a similar one.</summary>
        private static float Rnd(float i)
        {
            float x = Mathf.Sin(i * 12.9898f) * 43758.5453f;
            float m = x % 1f;
            return (m + 1f) % 1f;
        }

        /// <summary>
        /// The 31-lock layout — deterministic, so the same head grows the same hair every time. Ported
        /// literally from <c>buildNew()</c>'s own <c>L</c> array: three scalp rows at elevation
        /// 68°/42°/18° (45° azimuth steps at 68°, 32° at the other two, skipping |az| &lt; 50° below
        /// 60° elevation to leave the face clear) plus 5 fringe locks swept across the brow.
        /// </summary>
        public static HairLockSpec[] BuildLayout()
        {
            var specs = new List<HairLockSpec>(LockCount);
            int i = 0;

            foreach (float el in new[] { 68f, 42f, 18f })
            {
                float step = el > 60f ? 45f : 32f;
                for (double az = -180.0; az < 180.0; az += step)
                {
                    i++;
                    if (Mathf.Abs((float)az) < 50f && el < 60f) continue;

                    bool back = Mathf.Cos((float)az * Mathf.Deg2Rad) < 0f;
                    float len = (back ? 0.42f : 0.32f) + Rnd(i + 7) * 0.12f;
                    float width = 0.15f + Rnd(i + 3) * 0.04f;
                    float azFinal = (float)az + Rnd(i) * 14f;
                    specs.Add(BuildLock(azFinal, el, len, width, fringe: false, back));
                }
            }

            // Fringe: [azimuth, length] pairs, buildNew()'s own literal table — no jitter, unlike the
            // scalp rows above.
            (float az, float len)[] fringe =
            {
                (-36f, 0.2f), (-18f, 0.24f), (2f, 0.26f), (20f, 0.22f), (38f, 0.18f),
            };
            foreach (var (az, len) in fringe)
                specs.Add(BuildLock(az, 74f, len, 0.11f, fringe: true, back: false));

            return specs.ToArray();
        }

        private static HairLockSpec BuildLock(float az, float el, float len, float width, bool fringe, bool back)
        {
            float a = az * Mathf.Deg2Rad, e = el * Mathf.Deg2Rad;
            var outDir = new Vector3(Mathf.Sin(a) * Mathf.Cos(e), Mathf.Sin(e), Mathf.Cos(a) * Mathf.Cos(e));
            Vector3 root = HeadCenterLocal + outDir * RootDistance;

            // Hug: down the scalp, biased backward, with the component along "out" (straight away from
            // the head) removed — a lock has to start flat against the skull, not sticking out of it.
            var hug0 = new Vector3(0f, -1f, -0.35f);
            Vector3 hug = (hug0 - outDir * Vector3.Dot(hug0, outDir)).normalized;

            Vector3 flow = fringe ? FringeFlow : Vector3.Lerp(SharedFlow, hug, 0.25f).normalized;

            float seed = Rnd(az + el) * 10f;
            return new HairLockSpec(root, hug, flow, len, width, fringe, back, seed);
        }

        /// <summary>
        /// The world-space push on the hair, before it is turned into a head-local delta by the caller
        /// (<see cref="MaxHairRig.Tick"/>) — drag from walking plus a gusting wind plus a little walk
        /// bounce. Ported literally from <c>buildNew()</c>'s own <c>hair()</c>:
        /// <c>push = -facing*0.5*speed01 + windDir*gust*0.6</c>, lifted by its own length plus a touch
        /// more on each footfall.
        /// </summary>
        public static Vector3 ComputePushWorld(Vector3 facingFlatWorld, Vector3 windDirWorld, float windStrength,
                                                float time, float stridePhase, float speed01)
        {
            float gust = windStrength * (0.55f + 0.3f * Mathf.Sin(time * 0.9f)
                                                 + 0.2f * Mathf.Sin(time * 2.3f + 1.3f)
                                                 + 0.12f * Mathf.Sin(time * 5.1f));
            Vector3 push = facingFlatWorld * (-0.5f * speed01) + windDirWorld * (gust * 0.6f);
            push.y += push.magnitude * 0.12f + Mathf.Abs(Mathf.Cos(stridePhase)) * 0.1f * speed01;
            return push;
        }

        /// <summary>
        /// Advances one lock's spring chain by <paramref name="dt"/> and writes its
        /// <see cref="NodeCount"/> resolved node positions (head-local) into <paramref name="outNodes"/>
        /// — <c>outNodes[0]</c> is the root, <c>outNodes[NodeCount - 1]</c> is the tip. Ported literally
        /// from <c>buildNew()</c>'s own per-strand loop in <c>hair()</c>: each segment direction is a
        /// damped spring (stiffness <c>60 * 0.66^k + 6</c>, damping ratio 0.5) toward
        /// <c>lerp(hug, flow, w^0.8) + push * (baseline + 0.45w) + flutter</c>.
        /// </summary>
        public static void Tick(in HairLockSpec spec, HairLockState state, float dt, float speed01,
                                Vector3 headLocalPush, float time, float windStrength, Vector3[] outNodes)
        {
            const int n = SegCount;
            float segLen = spec.Length / n;
            float pushScaleBase = spec.Fringe ? 0.1f : 0.12f;

            Vector3 p = spec.RootLocal;
            outNodes[0] = p;

            for (int k = 1; k <= n; k++)
            {
                int seg = k - 1;
                float w = (float)k / n;

                // Per-lock flutter, on top of the shared push — buildNew()'s own 6 rad/s.
                float flutter = (0.16f * windStrength + 0.06f * speed01) *
                                Mathf.Sin(time * 6f + spec.Seed + k * 0.9f) * w;

                Vector3 target = Vector3.Lerp(spec.Hug, spec.Flow, Mathf.Pow(w, 0.8f)).normalized;
                target += headLocalPush * (pushScaleBase + 0.45f * w);
                target += new Vector3(flutter, flutter * 0.3f, -flutter * 0.6f);
                target = target.normalized;

                float stiffness = 60f * Mathf.Pow(0.66f, seg) + 6f;
                Vector3 acc = (target - state.SegDir[seg]) * stiffness - state.SegVel[seg] * Mathf.Sqrt(stiffness);
                state.SegVel[seg] += acc * dt;
                state.SegDir[seg] = (state.SegDir[seg] + state.SegVel[seg] * dt).normalized;

                p += state.SegDir[seg] * segLen;

                // Keep the ribbon off the skull.
                Vector3 rel = p - HeadCenterLocal;
                float dist = rel.magnitude;
                if (dist < HeadClearance) p = HeadCenterLocal + rel.normalized * HeadClearance;

                outNodes[k] = p;
            }
        }
    }
}
