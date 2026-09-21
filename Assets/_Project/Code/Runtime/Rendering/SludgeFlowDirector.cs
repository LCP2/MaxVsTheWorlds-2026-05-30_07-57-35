using System.Collections.Generic;
using UnityEngine;

namespace MaxWorlds.Rendering
{
    /// <summary>
    /// MV-873: World 2 ran every sludge tile's <see cref="SludgeFlowRig"/> off its own <c>Update()</c>,
    /// unconditionally, wherever Max stood — 69 tiles x 43 moving pieces every frame from the moment
    /// the level loaded, including every tile behind the player or never reached. This is the one
    /// director that owns every registered rig instead, the same central-director shape
    /// <c>MaxWorlds.VFX.GroundAnchorVfx</c> already uses and for the same reason: replace N per-object
    /// <c>Update()</c> calls with one place that decides which of them actually need the work done
    /// this frame.
    ///
    /// A rig only pays for <see cref="SludgeFlowRig.Apply"/> while it is within <see cref="GateRadius"/>
    /// (flat XZ distance) of the player, re-evaluated every frame. Past that it does nothing at all —
    /// but its timers are not frozen: the dt it would have spent ticking is banked in
    /// <see cref="_pendingDt"/> and handed to <see cref="SludgeFlowRig.Tick"/> in one lump the moment
    /// the rig comes back into range, so it resumes exactly where continuous ticking would have left
    /// it (the scroll is a pure function of elapsed time) instead of jumping or freezing.
    /// </summary>
    public static class SludgeFlowDirector
    {
        /// <summary>Past the fixed 72° camera's reach (ticket-fixed; a tuning change is a comment on
        /// MV-873, not a re-derivation here).</summary>
        public const float GateRadius = 45f;

        private static readonly List<SludgeFlowRig> _rigs = new List<SludgeFlowRig>(96);
        private static readonly List<float> _pendingDt = new List<float>(96);

        /// <summary>Called from <see cref="SludgeFlowRig.OnEnable"/> — every dressed tile registers
        /// itself, so no build site has to remember to wire it in.</summary>
        public static void Register(SludgeFlowRig rig)
        {
            _rigs.Add(rig);
            _pendingDt.Add(0f);
        }

        /// <summary>Called from <see cref="SludgeFlowRig.OnDisable"/>.</summary>
        public static void Unregister(SludgeFlowRig rig)
        {
            int index = _rigs.IndexOf(rig);
            if (index < 0) return;
            _rigs.RemoveAt(index);
            _pendingDt.RemoveAt(index);
        }

        /// <summary>Advance every registered rig by <paramref name="dt"/>: tick whichever are within
        /// <see cref="GateRadius"/> of <paramref name="playerPosition"/> (flat XZ), and bank the dt for
        /// whichever aren't, so a rig that regains range advances by the full time it actually spent
        /// gated, not just the frame that brought it back in.</summary>
        public static void Tick(float dt, Vector3 playerPosition)
        {
            float gateSqr = GateRadius * GateRadius;
            for (int i = 0; i < _rigs.Count; i++)
            {
                SludgeFlowRig rig = _rigs[i];
                if (rig == null) continue;

                Vector3 p = rig.transform.position;
                float dx = p.x - playerPosition.x;
                float dz = p.z - playerPosition.z;

                if (dx * dx + dz * dz <= gateSqr)
                {
                    rig.Tick(_pendingDt[i] + dt);
                    _pendingDt[i] = 0f;
                }
                else
                {
                    _pendingDt[i] += dt;
                }
            }
        }
    }
}
