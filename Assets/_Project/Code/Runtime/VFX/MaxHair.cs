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
    /// MV-854/MV-1133: Max's hair — 25 flowing locks, ported literally from buildV3()'s lock layout
    /// and <c>hair()</c>'s motion in the approved prototype (<c>max-lookdev.html</c>, Lee's title-
    /// reveal reference). Everything here is pure — no <see cref="Transform"/>, no
    /// <see cref="GameObject"/> — so an EditMode test can drive the spring maths directly, the same
    /// "expose the resolved value" contract <see cref="MaxRig.AimPose"/> and <see cref="MaxRig.Stride"/>
    /// already make for this rig. The GameObject/Mesh side of this — one merged dynamic mesh, one draw
    /// call for all 25 locks — is <see cref="MaxHairRig"/>.
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

        /// <summary>MV-1133: 20 scalp locks survive the layout loop below (three rings swept to one
        /// side, face left clear) plus 5 fringe locks — buildV3()'s own "25 thick pointed locks (three
        /// rings round the head and a fringe of five)", replacing MV-854's 31-lock long-hair layout.</summary>
        public const int LockCount = 25;

        /// <summary>Nodes are never allowed closer to the head than this, in head-local metres — keeps
        /// a lock from ever passing through the skull.</summary>
        public const float HeadClearance = 0.235f;

        /// <summary>Where a SCALP lock's root sits, out from the head centre, before the spring ever
        /// moves it — buildV3()'s own 0.2.</summary>
        private const float ScalpRootDistance = 0.2f;

        /// <summary>Where a FRINGE lock's root sits — buildV3()'s own 0.215, unchanged from MV-854.</summary>
        private const float FringeRootDistance = 0.215f;

        /// <summary>The head centre every lock roots and clears against — <c>buildNew()</c>'s own
        /// <c>hairC</c>, in the Head pivot's local space. Unchanged by MV-1133.</summary>
        public static readonly Vector3 HeadCenterLocal = new Vector3(0f, 1.72f, -0.04f);

        /// <summary>MV-1133: how a SCALP lock hugs the head at rest, before being pulled toward its own
        /// flow — buildV3()'s own (0, -1, -0.3), replacing MV-854's (0, -1, -0.35).</summary>
        private static readonly Vector3 ScalpHug0 = new Vector3(0f, -1f, -0.3f);

        /// <summary>MV-1133: how a FRINGE lock hugs the head at rest — buildV3()'s own (0, -1, 0.25),
        /// now distinct from the scalp rings' own hug (it used to share <c>Hug0</c> with the scalp).</summary>
        private static readonly Vector3 FringeHug0 = new Vector3(0f, -1f, 0.25f);

        /// <summary>Where a fringe lock sweeps instead — across the brow, to one side. MV-1133:
        /// buildV3()'s own (0.75, -0.6, 0.3), replacing MV-854's (1, -0.45, 0.3).</summary>
        private static readonly Vector3 FringeFlow = new Vector3(0.75f, -0.6f, 0.3f).normalized;

        /// <summary>MV-1133: the three scalp rings — (elevation, azimuth step, base length, flow lift),
        /// ported literally from buildV3()'s own ring table. <c>Lift</c> feeds the per-lock flow
        /// direction below; a positive lift sweeps the ring's hair up and back, a negative one sweeps it
        /// down and back — this is what gives the "swept to one side" read instead of a flat cap.</summary>
        private static readonly (float El, float Step, float Len0, float Lift)[] ScalpRings =
        {
            (64f, 60f, 0.21f, 0.25f),
            (34f, 36f, 0.29f, -0.15f),
            (6f, 34f, 0.30f, -0.55f),
        };

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
        /// The 25-lock layout — deterministic, so the same head grows the same hair every time. Ported
        /// literally from buildV3()'s own layout loop: three scalp rings (<see cref="ScalpRings"/>),
        /// each lim-excluded near the front (az = 0, where his face is) so the face stays clear, plus 5
        /// fringe locks swept across the brow at a fixed azimuth/length table.
        /// </summary>
        public static HairLockSpec[] BuildLayout()
        {
            var specs = new List<HairLockSpec>(LockCount);
            int i = 0;

            foreach (var ring in ScalpRings)
            {
                // buildV3()'s own limit table: wide open at the crown (nothing to clear at the very
                // top), progressively wider exclusion zones lower down the scalp where the face is.
                float lim = ring.El > 60f ? 0f : (ring.El > 20f ? 58f : 72f);

                for (double az = -180.0; az < 180.0; az += ring.Step)
                {
                    i++;
                    if (Mathf.Abs((float)az) < lim) continue;

                    float a = ((float)az + Rnd(i) * 10f) * Mathf.Deg2Rad;
                    float e = ring.El * Mathf.Deg2Rad;
                    var outDir = new Vector3(Mathf.Sin(a) * Mathf.Cos(e), Mathf.Sin(e), Mathf.Cos(a) * Mathf.Cos(e));
                    Vector3 root = HeadCenterLocal + outDir * ScalpRootDistance;

                    // Hug: down the scalp, with the component along "out" (straight away from the head)
                    // removed — a lock has to start flat against the skull, not sticking out of it.
                    Vector3 hug = (ScalpHug0 - outDir * Vector3.Dot(ScalpHug0, outDir)).normalized;

                    // Flow: buildV3()'s own per-ring sweep, blended toward "out" then toward "hug" —
                    // Lift is what makes the three rings sweep differently instead of all matching one
                    // shared direction (MV-854's SharedFlow).
                    Vector3 flow = new Vector3(0.8f, ring.Lift, -0.45f).normalized;
                    flow = Vector3.Lerp(flow, outDir, 0.3f);
                    flow = Vector3.Lerp(flow, hug, 0.25f).normalized;

                    float len = ring.Len0 + Rnd(i + 7) * 0.07f;
                    float width = 0.2f + Rnd(i + 3) * 0.05f;
                    bool back = Mathf.Cos((float)az * Mathf.Deg2Rad) < 0f;
                    float seed = Rnd((float)az + ring.El) * 10f;

                    specs.Add(new HairLockSpec(root, hug, flow, len, width, fringe: false, back, seed));
                }
            }

            // Fringe: [azimuth, length] pairs, buildV3()'s own literal table — no jitter, unlike the
            // scalp rings above. Fixed elevation (70°) and a fixed flow (FringeFlow), not derived per
            // lock the way the scalp rings are.
            (float Az, float Len)[] fringe =
            {
                (-44f, 0.15f), (-22f, 0.19f), (0f, 0.21f), (22f, 0.2f), (44f, 0.16f),
            };
            foreach (var f in fringe)
            {
                float a = f.Az * Mathf.Deg2Rad, e = 70f * Mathf.Deg2Rad;
                var outDir = new Vector3(Mathf.Sin(a) * Mathf.Cos(e), Mathf.Sin(e), Mathf.Cos(a) * Mathf.Cos(e));
                Vector3 root = HeadCenterLocal + outDir * FringeRootDistance;
                Vector3 hug = (FringeHug0 - outDir * Vector3.Dot(FringeHug0, outDir)).normalized;
                float seed = Rnd(f.Az) * 10f;

                specs.Add(new HairLockSpec(root, hug, FringeFlow, f.Len, 0.15f, fringe: true, back: false, seed));
            }

            return specs.ToArray();
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
