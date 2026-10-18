// MV-1226: the Mothership gives two existing behaviours (Rusher, Turret) their own ship bodies and
// names -- Drone and Sentry -- the same "world skin picks its own silhouette" mechanism MV-746/MV-800
// already established for the Reef/Stormdrain, dispatched from RobotBodies.Build via
// MothershipSkinTag. Only these two kinds carry a Mothership body; every other kind in this world
// (Lensman/Hauler, MV-1243) falls through untouched, and TryBuildMothership returns false for them so
// Build() keeps using the base roster. Colours are deliberately NOT overridden here -- unlike
// TryBuildReef (which repaints every kind in WorldMaterials' Reef colours), RobotRig.BuildMaterials
// only special-cases ReefSkinTag/StormdrainSkinTag; MothershipSkinTag falls through to
// CharacterSkin.ResolveBodyColor's plain BaseColorFor(role) path -- exactly the kind's own hue, the
// ticket's own "Lee likes the roster's kind colours; keep them".
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
    }
}
