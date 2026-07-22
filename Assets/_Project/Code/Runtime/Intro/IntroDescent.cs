using UnityEngine;

namespace MaxWorlds.Intro
{
    /// <summary>
    /// The DESCENT act of the opening cinematic (YT-156), storyboard beat 4: the viewpoint plunges from
    /// high orbit toward Max — a continent forms, then a town, then a single building, and finally the
    /// camera rushes at the roof of Max's shed and drops through it.
    ///
    /// Sold not by real orbit-to-ground scale (which no top-down set can hold) but by a fast dive down a
    /// stacked set: a wide land plane reads as a CONTINENT from high up, resolves into a TOWN of boxes as
    /// the camera falls, and the town parts around the one BUILDING the dive is aimed at — the shed. The
    /// director flies the camera; this only builds the set and hands back the dive target (the roof apex).
    /// </summary>
    public sealed class IntroDescent
    {
        public Transform Root { get; }
        /// <summary>The roof apex of Max's shed — where the dive ends and the camera drops through.</summary>
        public Transform DiveTarget { get; }

        private const float ShedWallH = 10f;
        private const float RoofRise = 6f;

        public IntroDescent(Transform parent, Vector3 localOrigin)
        {
            Root = IntroBuild.Pivot(parent, "IntroDescent", localOrigin);

            BuildLand();
            BuildTown();
            DiveTarget = BuildShed();

            SetActive(false);
        }

        public void SetActive(bool on) => Root.gameObject.SetActive(on);

        private void BuildLand()
        {
            // A huge land plane — from the top of the dive this is the "continent". A dry belt runs
            // across it so it is not a single flat green, and two roads cross where the town sits.
            var grass = IntroBuild.Lit("land_grass", IntroPalette.Grass);
            var dry = IntroBuild.Lit("land_dry", IntroPalette.LandDry);
            var road = IntroBuild.Lit("land_road", IntroPalette.Road);

            IntroBuild.Part(Root, "Land", PrimitiveType.Cube, new Vector3(0f, -1f, 0f),
                            new Vector3(1400f, 2f, 1400f), grass, castShadows: false);
            IntroBuild.Part(Root, "DryBelt", PrimitiveType.Cube, new Vector3(180f, -0.5f, -120f),
                            new Vector3(520f, 2f, 300f), dry, Quaternion.Euler(0f, 18f, 0f),
                            castShadows: false);

            IntroBuild.Part(Root, "RoadNS", PrimitiveType.Cube, new Vector3(0f, 0.2f, 0f),
                            new Vector3(14f, 2f, 620f), road, castShadows: false);
            IntroBuild.Part(Root, "RoadEW", PrimitiveType.Cube, new Vector3(0f, 0.2f, 40f),
                            new Vector3(620f, 2f, 14f), road, castShadows: false);
        }

        private void BuildTown()
        {
            var wallA = IntroBuild.Lit("town_wallA", IntroPalette.HouseWall);
            var wallB = IntroBuild.Lit("town_wallB", IntroPalette.HouseWallB);
            var roof = IntroBuild.Lit("town_roof", IntroPalette.RoofTile);

            // A loose grid of boxes with the centre kept clear for the shed the dive lands on. Heights
            // vary so the roofline is a town and not a wall. Deterministic — same town every replay.
            int idx = 0;
            for (int gx = -3; gx <= 3; gx++)
            for (int gz = -3; gz <= 3; gz++)
            {
                if (Mathf.Abs(gx) <= 1 && Mathf.Abs(gz) <= 1) continue;   // the shed's block

                idx++;
                float x = gx * 78f + ((gz & 1) == 0 ? 8f : -8f);
                float z = gz * 78f + ((gx & 1) == 0 ? -6f : 6f);
                float w = 26f + (idx % 4) * 6f;
                float d = 24f + (idx % 3) * 7f;
                float h = 18f + (idx * 13 % 40);

                var wall = (idx % 2 == 0) ? wallA : wallB;
                IntroBuild.Part(Root, "Building", PrimitiveType.Cube, new Vector3(x, h * 0.5f, z),
                                new Vector3(w, h, d), wall, castShadows: false);
                // A flat roof cap, a touch darker, so each box reads as a roofed building from above.
                IntroBuild.Part(Root, "BuildingRoof", PrimitiveType.Cube, new Vector3(x, h + 1f, z),
                                new Vector3(w + 2f, 2f, d + 2f), roof, castShadows: false);
            }
        }

        /// <summary>Max's shed, seen from outside: plank walls and a pitched roof, standing alone in the
        /// cleared block the dive is aimed at. Returns the roof apex — the point the camera rushes.</summary>
        private Transform BuildShed()
        {
            var shed = IntroBuild.Pivot(Root, "Shed", Vector3.zero);

            var plank = IntroBuild.Lit("shed_ext_plank", IntroPalette.ShedPlank);
            var plankDark = IntroBuild.Lit("shed_ext_dark", IntroPalette.ShedPlankDark);
            var roof = IntroBuild.Lit("shed_ext_roof", IntroPalette.RoofTile);

            const float w = 16f, d = 20f;
            IntroBuild.Part(shed, "Walls", PrimitiveType.Cube, new Vector3(0f, ShedWallH * 0.5f, 0f),
                            new Vector3(w, ShedWallH, d), plank, castShadows: false);
            // A door on the front, dark against the planks — where Max will come out.
            IntroBuild.Part(shed, "Door", PrimitiveType.Cube, new Vector3(0f, 3.6f, d * 0.5f + 0.2f),
                            new Vector3(5f, 7.2f, 0.6f), plankDark, castShadows: false);

            // Two tipped roof slabs meeting at a ridge — the same box-and-wedge as the neighbourhood.
            float slope = w * 0.62f;
            foreach (float s in new[] { -1f, 1f })
            {
                IntroBuild.Part(shed, "RoofSlab", PrimitiveType.Cube,
                                new Vector3(s * w * 0.25f, ShedWallH + RoofRise * 0.5f, 0f),
                                new Vector3(slope, 0.8f, d + 2f), roof,
                                Quaternion.Euler(0f, 0f, s * 32f), castShadows: false);
            }

            return IntroBuild.Pivot(shed, "RoofApex", new Vector3(0f, ShedWallH + RoofRise, 0f));
        }
    }
}
