// MV-1226: the Mothership gives two existing behaviours (Rusher, Turret) their own ship bodies and
// names -- Drone and Sentry -- the same "world skin picks its own silhouette" mechanism MV-746/MV-800
// already established for the Reef/Stormdrain, dispatched from RobotBodies.Build via
// MothershipSkinTag. MV-1243 adds two more -- Gunner/Brute as Lensman/Hauler. Every other kind in this
// world falls through untouched, and TryBuildMothership returns false for them so Build() keeps using
// the base roster. Colours are deliberately NOT overridden here -- unlike TryBuildReef (which repaints
// every kind in WorldMaterials' Reef colours), RobotRig.BuildMaterials only special-cases
// ReefSkinTag/StormdrainSkinTag; MothershipSkinTag falls through to CharacterSkin.ResolveBodyColor's
// plain BaseColorFor(role) path -- exactly the kind's own hue, the ticket's own "Lee likes the
// roster's kind colours; keep them".
using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Enemies;

namespace MaxWorlds.VFX
{
    public static partial class RobotBodies
    {
        /// <summary>Dispatches the Mothership's two kinds to their own silhouette. Returns false (and
        /// builds nothing) for any other kind, so <see cref="Build"/> falls through to the base
        /// roster's body instead of silently reskinning something this world doesn't rename.</summary>
        private static bool TryBuildMothership(EnemyKind kind, Transform root, in RobotPalette p,
                                         List<MeshRenderer> eyes, List<Transform> wheels, List<Transform> legs)
        {
            switch (kind)
            {
                case EnemyKind.Rusher: BuildMothershipDrone(root, p, eyes, wheels, legs); return true;
                case EnemyKind.Turret: BuildMothershipSentry(root, p, eyes, wheels, legs); return true;
                case EnemyKind.Gunner: BuildMothershipLensman(root, p, eyes, wheels, legs); return true;
                case EnemyKind.Brute:  BuildMothershipHauler(root, p, eyes, wheels, legs); return true;
                default: return false;
            }
        }

        // ------------------------------------------------------------------ Drone (Rusher)

        /// <summary>Drone -- a hovering sphere 0.78 m across, 0.3 m clear of the deck. One big
        /// off-centre lens eye, two short stabiliser fins swept off the rear flanks, and a faint
        /// thruster glow disc tucked underneath. No wheels, no legs: it never touches the ground.</summary>
        private static void BuildMothershipDrone(Transform root, in RobotPalette p,
                                    List<MeshRenderer> eyes, List<Transform> wheels, List<Transform> legs)
        {
            const float hoverGap = 0.3f;
            const float diameter = 0.78f;
            float centreY = hoverGap + diameter * 0.5f;

            Add(root, CharacterMeshes.Sphere(24), p.Warm, new Vector3(0f, centreY, 0f),
                Quaternion.identity, new Vector3(diameter, diameter, diameter), "Hull");

            // The faint thruster glow -- a flattened disc tucked just under the hull.
            Add(root, CharacterMeshes.Lathe(new[] { new Vector2(0f, 0f), new Vector2(0.2f, 0.015f), new Vector2(0f, 0.03f) }, 20), p.Cool,
                new Vector3(0f, hoverGap - 0.02f, 0f), Quaternion.identity, Vector3.one, "ThrusterGlow");

            // Two short stabiliser fins, swept back off the rear flanks.
            Add(root, CharacterMeshes.Prism(3, 0.02f, 0.008f, 0.14f, 0.2f, 0f), p.Dark,
                new Vector3(-0.25f, centreY, -0.2f), Quaternion.Euler(0f, 0f, -64f), Vector3.one, "Fin");
            Add(root, CharacterMeshes.Prism(3, 0.02f, 0.008f, 0.14f, 0.2f, 0f), p.Dark,
                new Vector3(0.25f, centreY, -0.2f), Quaternion.Euler(0f, 0f, 64f), Vector3.one, "Fin");

            // The one big off-centre lens eye, tilted up toward the play camera's own downward pitch --
            // the same -40 deg convention every other forward lens in this file uses (BuildRusher et
            // al.); left at Quaternion.identity the disc's thin axis points dead level and reads as
            // nothing at all from the fixed ~72 deg top-down camera (confirmed via cc-robot-roster.bat:
            // mothership_pair.png showed no eye on either body until this fix).
            eyes.Add(Lens(root, CharacterMeshes.Sphere(22), new Vector3(0.16f, centreY + 0.03f, 0.33f),
                Quaternion.Euler(-40f, 0f, 0f), new Vector3(0.22f, 0.22f, 0.1f)));
        }

        // ------------------------------------------------------------------ Sentry (Turret)

        /// <summary>Sentry -- a deck-mounted gun post: a 0.9 m square base bolted to the floor, a
        /// 1.1 m column, and a turret head carrying one forward barrel and the eye. No wheels, no legs:
        /// it is bolted down, the same static-emplacement footing the base-roster Turret already
        /// stands on. The pre-shot flash remains driven by <see cref="MaxWorlds.Enemies.RobotEnemy"/>'s
        /// existing eye-colour telegraph (unchanged by this ticket, per its own "do not re-raise");
        /// the gold tip on the barrel is purely a visual balance, the same roster idiom
        /// <see cref="BuildBolter"/>/<see cref="BuildTurret"/> already use.</summary>
        private static void BuildMothershipSentry(Transform root, in RobotPalette p,
                                    List<MeshRenderer> eyes, List<Transform> wheels, List<Transform> legs)
        {
            const float baseHeight = 0.1f;
            const float columnHeight = 1.1f;
            const float headHeight = 0.22f;
            float columnCentreY = baseHeight + columnHeight * 0.5f;
            float headCentreY = baseHeight + columnHeight + headHeight * 0.5f;

            // The 0.9m square base, bolted to the floor.
            Add(root, CharacterMeshes.Prism(4, 0.64f, 0.6f, baseHeight, 0.3f, 0f), p.Dark,
                new Vector3(0f, baseHeight * 0.5f, 0f), Quaternion.identity, Vector3.one, "Base");

            // The column.
            Add(root, CharacterMeshes.Prism(4, 0.11f, 0.095f, columnHeight, 0.1f, 0f), p.Cool,
                new Vector3(0f, columnCentreY, 0f), Quaternion.identity, Vector3.one, "Column");

            // The turret head.
            Add(root, CharacterMeshes.Prism(4, 0.19f, 0.17f, headHeight, 0.2f, 0f), p.Warm,
                new Vector3(0f, headCentreY, 0f), Quaternion.identity, Vector3.one, "Head");

            // The one forward barrel, along the object's own +Z.
            Add(root, CharacterMeshes.Beam(0.5f, 0.04f, 0.032f, 8), p.Dark,
                new Vector3(0f, headCentreY, 0.26f), Quaternion.Euler(90f, 0f, 0f), Vector3.one, "Barrel");
            Add(root, CharacterMeshes.Sphere(10), p.Gold,
                new Vector3(0f, headCentreY, 0.5f), Quaternion.identity, new Vector3(0.05f, 0.05f, 0.05f));

            // The one eye lens, per the roster's one-eye rule -- same -40 deg camera-facing tilt as the
            // Drone's above, for the same reason.
            eyes.Add(Lens(root, CharacterMeshes.Sphere(20), new Vector3(0f, headCentreY + 0.05f, 0.19f),
                Quaternion.Euler(-40f, 0f, 0f), new Vector3(0.1f, 0.1f, 0.06f)));
        }

        // ------------------------------------------------------------------ Lensman (Gunner)

        private const float LensmanHipHeight = 0.42f;
        private const float LensmanLegBeamHalfLength = 0.21f;

        /// <summary>Lensman -- a tall frame (MV-1243, 1.7 m) standing on two thin articulated legs
        /// (half the base-roster Gunner's three-leg tripod -- it is a static emplacement here too, so
        /// the legs are purely a stance, never a gait), tapering up to a thin neck mast that carries
        /// one big lens as its whole head. No separate eye socket: the lens IS the head, per the
        /// ticket's own "one big lens as its head" -- <see cref="MaxWorlds.VFX.RobotRig.UpdateBeamVfx"/>
        /// already fires the beam from <c>_eyes[0]</c>'s position for any Gunner, mothership-skinned or
        /// not, so "its beam leaves from the lens" falls out of the existing beam-origin code for free.</summary>
        private static void BuildMothershipLensman(Transform root, in RobotPalette p,
                                    List<MeshRenderer> eyes, List<Transform> wheels, List<Transform> legs)
        {
            Transform leg1Beam = Add(root, CharacterMeshes.Beam(0.42f, 0.032f, 0.02f, 6), p.Dark,
                new Vector3(-0.1f, 0.21f, 0.02f), Quaternion.Euler(6f, 0f, 4f), Vector3.one);
            Transform leg1Foot = Add(root, CharacterMeshes.Lathe(new[] { new Vector2(0.045f, 0f), new Vector2(0.03f, 0.025f), new Vector2(0.012f, 0.05f) }, 8), p.Dark,
                new Vector3(-0.11f, 0.02f, 0.05f), Quaternion.identity, Vector3.one);
            legs.Add(AddLeg(root, leg1Beam, leg1Foot, LensmanLegBeamHalfLength));

            Transform leg2Beam = Add(root, CharacterMeshes.Beam(0.42f, 0.032f, 0.02f, 6), p.Dark,
                new Vector3(0.1f, 0.21f, 0.02f), Quaternion.Euler(6f, 0f, -4f), Vector3.one);
            Transform leg2Foot = Add(root, CharacterMeshes.Lathe(new[] { new Vector2(0.045f, 0f), new Vector2(0.03f, 0.025f), new Vector2(0.012f, 0.05f) }, 8), p.Dark,
                new Vector3(0.11f, 0.02f, 0.05f), Quaternion.identity, Vector3.one);
            legs.Add(AddLeg(root, leg2Beam, leg2Foot, LensmanLegBeamHalfLength));

            // The frame -- cool lower half, warm upper half, the same two-lathe-half torso idiom every
            // other kind uses, tapering to a thin neck mast.
            Add(root, CharacterMeshes.Lathe(new[] { new Vector2(0.08f, 0f), new Vector2(0.1f, 0.22f), new Vector2(0.085f, 0.4f) }, 20), p.Cool,
                new Vector3(0f, LensmanHipHeight, 0f), Quaternion.identity, Vector3.one, "Frame");
            Add(root, CharacterMeshes.Lathe(new[] { new Vector2(0.085f, 0.4f), new Vector2(0.065f, 0.56f), new Vector2(0.03f, 0.68f) }, 20), p.Warm,
                new Vector3(0f, LensmanHipHeight, 0f), Quaternion.identity, Vector3.one, "Frame");

            const float neckBeamCentreY = LensmanHipHeight + 0.68f + 0.2f;   // ~1.30
            Add(root, CharacterMeshes.Beam(0.4f, 0.03f, 0.022f, 6), p.Dark,
                new Vector3(0f, neckBeamCentreY, 0f), Quaternion.identity, Vector3.one, "NeckMast");

            // One big lens as the head (ticket's own phrase) -- the kind's own hue (p.Warm) fills
            // almost the whole head, reading as one big coloured lens rather than a small eye set into
            // a separate head shape; a small dark socket and the gameplay eye tell (the telegraph/fire
            // flash) sit on its forward face, the same socket-then-lens idiom every other kind uses.
            // A solid sphere, not a ring: a ring here left the "head" hollow and unreadable from the
            // fixed top-down camera (caught via cc-robot-roster.bat: mothership_pair_lensman_hauler.png
            // showed an empty loop with no head at all).
            const float headDiameter = 0.34f;
            const float headCentreY = 1.7f - headDiameter * 0.5f;
            Add(root, CharacterMeshes.Sphere(24), p.Warm, new Vector3(0f, headCentreY, 0f), Quaternion.identity,
                new Vector3(headDiameter, headDiameter, headDiameter), "LensHead");
            Add(root, CharacterMeshes.Lathe(new[] { new Vector2(0.07f, 0f), new Vector2(0.1f, 0.012f), new Vector2(0.085f, 0.028f) }, 18), p.Dark,
                new Vector3(0f, headCentreY, headDiameter * 0.42f), Quaternion.Euler(50f, 0f, 0f), Vector3.one, "LensSocket");
            eyes.Add(Lens(root, CharacterMeshes.Sphere(22), new Vector3(0f, headCentreY + 0.01f, headDiameter * 0.46f),
                Quaternion.Euler(-40f, 0f, 0f), new Vector3(0.16f, 0.16f, 0.06f)));
        }

        // ------------------------------------------------------------------ Hauler (Brute)

        /// <summary>Hauler -- a cargo loader (MV-1243, 1.25 m wide/1.5 m tall) on four wheels with two
        /// raised forks. The hull -- not the wheel track -- sets the width: its peak lathe radius
        /// (0.625 m) is exactly half the ticket's 1.25 m, and every wheel/fork mount sits well inside
        /// that radius so nothing else in the silhouette reaches further out.</summary>
        private static void BuildMothershipHauler(Transform root, in RobotPalette p,
                                    List<MeshRenderer> eyes, List<Transform> wheels, List<Transform> legs)
        {
            float[] wheelX = { -0.5f, 0.5f };
            float[] wheelZ = { -0.36f, 0.36f };
            foreach (float x in wheelX)
                foreach (float z in wheelZ)
                {
                    wheels.Add(Add(root, CharacterMeshes.Lathe(new[] { new Vector2(0.09f, -0.06f), new Vector2(0.17f, -0.06f), new Vector2(0.18f, -0.03f), new Vector2(0.18f, 0.03f), new Vector2(0.17f, 0.06f), new Vector2(0.09f, 0.06f) }, 18), p.Dark,
                        new Vector3(x, 0.18f, z), Quaternion.Euler(0f, 0f, 90f), Vector3.one));
                    wheels.Add(Add(root, CharacterMeshes.Lathe(new[] { new Vector2(0.1f, -0.022f), new Vector2(0.115f, 0f), new Vector2(0.1f, 0.022f) }, 14), p.Warm,
                        new Vector3(x, 0.18f, z), Quaternion.Euler(0f, 0f, 90f), Vector3.one));
                }

            // The hull -- a wide cargo-loader deck, lathed rather than hard-edged (the same "no
            // cardboard boxes" idiom every roster body uses), cool lower / warm upper, FLAT-topped
            // (the last profile point keeps a real radius, so Lathe caps it with a flat deck disc)
            // rather than tapering to a point -- a tapered dome read as a plain ball from the fixed
            // top-down camera with nothing else visible (caught via cc-robot-roster.bat:
            // mothership_pair_lensman_hauler.png), which a flat deck breaks up.
            Add(root, CharacterMeshes.Lathe(new[] { new Vector2(0.3f, 0.3f), new Vector2(0.5f, 0.4f), new Vector2(0.625f, 0.55f), new Vector2(0.625f, 0.78f) }, 24), p.Cool,
                Vector3.zero, Quaternion.identity, Vector3.one, "Hull");
            Add(root, CharacterMeshes.Lathe(new[] { new Vector2(0.625f, 0.78f), new Vector2(0.58f, 0.86f), new Vector2(0.58f, 1.1f), new Vector2(0.5f, 1.16f) }, 24), p.Warm,
                Vector3.zero, Quaternion.identity, Vector3.one, "Hull");

            // The one Tier-1 gold tell, on the flat deck.
            Add(root, CharacterMeshes.Sphere(12), p.Gold, new Vector3(0f, 1.28f, -0.2f), Quaternion.identity, new Vector3(0.12f, 0.12f, 0.12f));

            // The one eye lens, set into the cab's forward face, per the roster's one-eye rule.
            Add(root, CharacterMeshes.Lathe(new[] { new Vector2(0.08f, 0f), new Vector2(0.11f, 0.012f), new Vector2(0.095f, 0.03f) }, 18), p.Dark,
                new Vector3(0f, 0.92f, 0.55f), Quaternion.Euler(60f, 0f, 0f), Vector3.one);
            eyes.Add(Lens(root, CharacterMeshes.Sphere(20), new Vector3(0f, 0.94f, 0.59f),
                Quaternion.Euler(-40f, 0f, 0f), new Vector3(0.16f, 0.16f, 0.06f)));

            // Two raised forks, splayed outward in a shallow V as they extend forward past the hull's
            // own 0.625m reach -- a pure +Z beam (tried first) foreshortens to almost nothing under the
            // fixed top-down camera, the same read every other forward-pointing tell in this roster
            // (the Bolter's barrel, the Sentry's) already gets from this angle; splaying outward keeps
            // both forks legible as two diverging lines rather than two near-invisible stubs.
            Add(root, CharacterMeshes.Beam(0.75f, 0.05f, 0.04f, 6), p.Dark,
                new Vector3(-0.3f, 0.18f, 0.45f), Quaternion.Euler(90f, -18f, 0f), Vector3.one, "Fork");
            Add(root, CharacterMeshes.Beam(0.75f, 0.05f, 0.04f, 6), p.Dark,
                new Vector3(0.3f, 0.18f, 0.45f), Quaternion.Euler(90f, 18f, 0f), Vector3.one, "Fork");
        }
    }
}
