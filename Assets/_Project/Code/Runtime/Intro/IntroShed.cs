using UnityEngine;

namespace MaxWorlds.Intro
{
    /// <summary>
    /// The SHED act of the opening cinematic (YT-156), storyboard beat 5: the camera lands inside Max's
    /// shed where he is working away, completely oblivious to what has just come down outside. Then he
    /// straightens, grabs his hose, and the door swings open to daylight — and the game begins.
    ///
    /// Max here is a compact stand-in built from the same palette as the full <see cref="MaxWorlds.VFX.MaxRig"/>
    /// — red hoodie, brown hair, the junk-built water gun — so the kid the intro hands over to is visibly
    /// the kid the player then controls. The director drives the beat by time; this builds the room and
    /// exposes the door swing + the grab so a test (and, later, YT-155's Timeline) can scrub it.
    /// </summary>
    public sealed class IntroShed
    {
        public Transform Root { get; }
        /// <summary>Max's body root — where the camera looks, and what turns from bench to door.</summary>
        public Transform Max { get; private set; }
        /// <summary>The door hinge. Shut at 0, swung wide as the beat ends.</summary>
        public Transform Door { get; private set; }
        /// <summary>A good viewpoint inside the shed, for the director's camera.</summary>
        public Transform CameraAnchor { get; private set; }

        /// <summary>How far the door is open, 0..1 — what a test reads to prove the door actually opens.</summary>
        public float DoorOpen01 { get; private set; }
        /// <summary>How far Max has turned from his bench toward the door, 0..1.</summary>
        public float Turn01 { get; private set; }

        private const float RoomW = 8f, RoomH = 3.6f, RoomD = 9f;
        private const float DoorW = 2.2f, DoorH = 3f;
        private const float BenchYaw = 200f;   // Max starts facing his bench (back-left), not the door

        private Transform _torso;
        private Transform _head;
        private Transform _gun;
        private MeshRenderer _daylight;

        public IntroShed(Transform parent, Vector3 localOrigin)
        {
            Root = IntroBuild.Pivot(parent, "IntroShed", localOrigin);

            BuildRoom();
            BuildBenchAndTools();
            BuildMax();
            BuildDoorAndDaylight();

            CameraAnchor = IntroBuild.Pivot(Root, "ShedCam", new Vector3(4.4f, 1.85f, 1.6f));

            SetPhase(0f);
            SetActive(false);
        }

        public void SetActive(bool on) => Root.gameObject.SetActive(on);

        // ------------------------------------------------------------------ the beat

        /// <summary>
        /// Drive the whole shed beat, t in 0..1:
        ///   • 0.00–0.45  Max works, oblivious — a gentle tinker at the bench.
        ///   • 0.40–0.70  he straightens and turns to the door, and the gun comes up into his hands.
        ///   • 0.55–1.00  the door swings wide and daylight floods in.
        /// </summary>
        public void SetPhase(float t)
        {
            t = Mathf.Clamp01(t);

            // The oblivious tinker: a small bob and a busy set of hands, fading out as he notices.
            float working = 1f - IntroBuild.Ramp(0.35f, 0.5f, t);
            float tinker = Mathf.Sin(t * 46f) * 0.06f * working;
            if (_torso != null) _torso.localPosition = new Vector3(0f, 0.02f * working, tinker);
            if (_head != null) _head.localRotation = Quaternion.Euler(18f * working + tinker * 60f, 0f, 0f);

            // He turns from the bench to the door.
            Turn01 = IntroBuild.Ramp(0.40f, 0.70f, t);
            if (Max != null) Max.localRotation = Quaternion.Euler(0f, Mathf.Lerp(BenchYaw, 360f, Turn01), 0f);

            // The gun comes up out of the tinker and into a carry as he turns.
            if (_gun != null)
            {
                _gun.localPosition = Vector3.Lerp(new Vector3(0.35f, 0.9f, 0.5f),
                                                  new Vector3(0.1f, 1.05f, 0.55f), Turn01);
                _gun.localRotation = Quaternion.Euler(Mathf.Lerp(70f, 8f, Turn01), 0f, 0f);
            }

            // The door swings wide, and the daylight past it floods from a slit to the whole doorway.
            DoorOpen01 = IntroBuild.Ramp(0.55f, 1f, t);
            if (Door != null) Door.localRotation = Quaternion.Euler(0f, -104f * DoorOpen01, 0f);
            if (_daylight != null)
                IntroBuild.SetGlow(_daylight, IntroPalette.Daylight * Mathf.Lerp(0.15f, 1.4f, DoorOpen01));
        }

        // ------------------------------------------------------------------ build

        private void BuildRoom()
        {
            var plank = IntroBuild.Lit("shed_plank", IntroPalette.ShedPlank);
            var plankDark = IntroBuild.Lit("shed_plank_dark", IntroPalette.ShedPlankDark);
            var floor = IntroBuild.Lit("shed_floor", IntroPalette.ShedFloor);

            IntroBuild.Part(Root, "Floor", PrimitiveType.Cube, new Vector3(0f, -0.1f, 0f),
                            new Vector3(RoomW, 0.2f, RoomD), floor, castShadows: false);
            IntroBuild.Part(Root, "Ceiling", PrimitiveType.Cube, new Vector3(0f, RoomH, 0f),
                            new Vector3(RoomW, 0.2f, RoomD), plankDark, castShadows: false);

            // Back and side walls solid; the front wall (+Z) is two pillars and a lintel around the door.
            IntroBuild.Part(Root, "WallBack", PrimitiveType.Cube, new Vector3(0f, RoomH * 0.5f, -RoomD * 0.5f),
                            new Vector3(RoomW, RoomH, 0.2f), plank, castShadows: false);
            IntroBuild.Part(Root, "WallLeft", PrimitiveType.Cube, new Vector3(-RoomW * 0.5f, RoomH * 0.5f, 0f),
                            new Vector3(0.2f, RoomH, RoomD), plank, castShadows: false);
            IntroBuild.Part(Root, "WallRight", PrimitiveType.Cube, new Vector3(RoomW * 0.5f, RoomH * 0.5f, 0f),
                            new Vector3(0.2f, RoomH, RoomD), plank, castShadows: false);

            float pillarW = (RoomW - DoorW) * 0.5f;
            float front = RoomD * 0.5f;
            IntroBuild.Part(Root, "FrontL", PrimitiveType.Cube,
                            new Vector3(-(DoorW * 0.5f + pillarW * 0.5f), RoomH * 0.5f, front),
                            new Vector3(pillarW, RoomH, 0.2f), plank, castShadows: false);
            IntroBuild.Part(Root, "FrontR", PrimitiveType.Cube,
                            new Vector3(DoorW * 0.5f + pillarW * 0.5f, RoomH * 0.5f, front),
                            new Vector3(pillarW, RoomH, 0.2f), plank, castShadows: false);
            IntroBuild.Part(Root, "Lintel", PrimitiveType.Cube,
                            new Vector3(0f, DoorH + (RoomH - DoorH) * 0.5f, front),
                            new Vector3(DoorW, RoomH - DoorH, 0.2f), plank, castShadows: false);

            // The bare bulb hanging over the bench — the one warm light in the shed.
            IntroBuild.Part(Root, "Flex", PrimitiveType.Cylinder, new Vector3(-1.2f, RoomH - 0.35f, -1.6f),
                            new Vector3(0.03f, 0.35f, 0.03f), plankDark, castShadows: false);
            IntroBuild.Glow(Root, "Bulb", new Vector3(-1.2f, RoomH - 0.75f, -1.6f), 0.32f, IntroPalette.Bulb);
        }

        private void BuildBenchAndTools()
        {
            var bench = IntroBuild.Lit("shed_bench", IntroPalette.Workbench);
            var steel = IntroBuild.Lit("shed_steel", IntroPalette.Steel);
            var dark = IntroBuild.Lit("shed_plank_dark", IntroPalette.ShedPlankDark);

            // The bench against the back wall — where Max is bent over his work.
            IntroBuild.Part(Root, "Bench", PrimitiveType.Cube, new Vector3(-1.2f, 0.95f, -3.4f),
                            new Vector3(4.5f, 0.18f, 1.4f), bench, castShadows: false);
            for (int i = 0; i < 2; i++)
            {
                float x = -1.2f + (i == 0 ? -1.9f : 1.9f);
                IntroBuild.Part(Root, "BenchLeg", PrimitiveType.Cube, new Vector3(x, 0.47f, -3.4f),
                                new Vector3(0.16f, 0.94f, 0.16f), dark, castShadows: false);
            }
            // Junk on the bench — the tinkerer's clutter, a few boxes and a bit of steel.
            IntroBuild.Part(Root, "Junk0", PrimitiveType.Cube, new Vector3(-2.3f, 1.14f, -3.4f),
                            new Vector3(0.5f, 0.32f, 0.5f), dark, castShadows: false);
            IntroBuild.Part(Root, "Junk1", PrimitiveType.Cylinder, new Vector3(-0.2f, 1.16f, -3.5f),
                            new Vector3(0.3f, 0.18f, 0.3f), steel, Quaternion.Euler(90f, 0f, 0f),
                            castShadows: false);
            // A pegboard of shapes on the back wall, for a bit of workshop life.
            for (int i = 0; i < 4; i++)
                IntroBuild.Part(Root, "Tool", PrimitiveType.Cube,
                                new Vector3(0.6f + i * 0.5f, 2.1f, -4.35f),
                                new Vector3(0.08f, 0.5f + (i % 2) * 0.3f, 0.08f), steel,
                                Quaternion.Euler(0f, 0f, i * 12f), castShadows: false);
        }

        private void BuildMax()
        {
            Max = IntroBuild.Pivot(Root, "IntroMax", new Vector3(-1.2f, 0f, -2.2f));
            Max.localRotation = Quaternion.Euler(0f, BenchYaw, 0f);

            var hoodie = IntroBuild.Lit("max_hoodie", IntroPalette.Hoodie);
            var hoodieShade = IntroBuild.Lit("max_hoodie_shade", IntroPalette.HoodieShade);
            var skin = IntroBuild.Lit("max_skin", IntroPalette.Skin);
            var hair = IntroBuild.Lit("max_hair", IntroPalette.Hair);
            var trousers = IntroBuild.Lit("max_trousers", IntroPalette.Trousers);

            // Legs.
            for (int i = 0; i < 2; i++)
            {
                float s = i == 0 ? -1f : 1f;
                IntroBuild.Part(Max, "Leg", PrimitiveType.Cube, new Vector3(s * 0.13f, 0.42f, 0f),
                                new Vector3(0.22f, 0.84f, 0.26f), trousers, castShadows: false);
            }

            _torso = IntroBuild.Pivot(Max, "Torso", new Vector3(0f, 0.84f, 0f));
            IntroBuild.Part(_torso, "Chest", PrimitiveType.Cube, new Vector3(0f, 0.34f, 0f),
                            new Vector3(0.6f, 0.5f, 0.4f), hoodie, castShadows: false);
            // The hood, wrapping the shoulders — the load-bearing "kid in a hoodie" shape.
            IntroBuild.Part(_torso, "Hood", PrimitiveType.Cube, new Vector3(0f, 0.62f, -0.16f),
                            new Vector3(0.46f, 0.26f, 0.24f), hoodieShade, Quaternion.Euler(20f, 0f, 0f),
                            castShadows: false);
            for (int i = 0; i < 2; i++)
            {
                float s = i == 0 ? -1f : 1f;
                IntroBuild.Part(_torso, "Shoulder", PrimitiveType.Sphere, new Vector3(s * 0.28f, 0.5f, 0f),
                                new Vector3(0.24f, 0.23f, 0.3f), hoodie, castShadows: false);
                // A sleeve angled to the front where his hands hold the gun.
                IntroBuild.Part(_torso, "Arm", PrimitiveType.Cube, new Vector3(s * 0.22f, 0.28f, 0.24f),
                                new Vector3(0.15f, 0.42f, 0.15f), hoodieShade,
                                Quaternion.Euler(58f, 0f, s * 8f), castShadows: false);
            }

            _head = IntroBuild.Pivot(_torso, "Head", new Vector3(0f, 0.66f, 0.02f));
            IntroBuild.Part(_head, "Skull", PrimitiveType.Sphere, new Vector3(0f, 0.13f, 0.02f),
                            new Vector3(0.3f, 0.3f, 0.29f), skin, castShadows: false);
            IntroBuild.Part(_head, "Hair", PrimitiveType.Sphere, new Vector3(0f, 0.22f, -0.05f),
                            new Vector3(0.33f, 0.21f, 0.32f), hair, castShadows: false);
            // The goggles, pushed up on his brow — the one lit thing on him, amber like MaxRig's lenses.
            for (int i = 0; i < 2; i++)
                IntroBuild.Glow(_head, "Lens", new Vector3((i == 0 ? -1 : 1) * 0.08f, 0.2f, 0.15f), 0.06f,
                                IntroPalette.EyeAmber);

            BuildGun();
        }

        /// <summary>The junk-built water gun: a steel receiver, the blue tank, the green hose. Parented to
        /// a pivot the beat raises from the bench into his hands.</summary>
        private void BuildGun()
        {
            _gun = IntroBuild.Pivot(_torso, "Gun", new Vector3(0.35f, 0.9f, 0.5f));

            var steel = IntroBuild.Lit("max_steel", IntroPalette.Steel);
            var water = IntroBuild.Lit("max_water", IntroPalette.TankWater);
            var hose = IntroBuild.Lit("max_hose", IntroPalette.HoseGreen);

            IntroBuild.Part(_gun, "Receiver", PrimitiveType.Cube, new Vector3(0f, 0f, 0.05f),
                            new Vector3(0.12f, 0.14f, 0.36f), steel, castShadows: false);
            IntroBuild.Part(_gun, "Barrel", PrimitiveType.Cylinder, new Vector3(0f, 0.01f, 0.32f),
                            new Vector3(0.08f, 0.16f, 0.08f), steel, Quaternion.Euler(90f, 0f, 0f),
                            castShadows: false);
            IntroBuild.Part(_gun, "Tank", PrimitiveType.Cylinder, new Vector3(0f, 0.12f, -0.04f),
                            new Vector3(0.13f, 0.14f, 0.13f), water, Quaternion.Euler(90f, 0f, 0f),
                            castShadows: false);
            // The hose feeding the back of the sprayer and dropping to the coil at his hip.
            IntroBuild.Part(_gun, "Hose", PrimitiveType.Cylinder, new Vector3(0f, -0.08f, -0.16f),
                            new Vector3(0.06f, 0.18f, 0.06f), hose, Quaternion.Euler(56f, 0f, 0f),
                            castShadows: false);
        }

        private void BuildDoorAndDaylight()
        {
            var plankDark = IntroBuild.Lit("shed_plank_dark", IntroPalette.ShedPlankDark);

            // The hinge sits on the LEFT edge of the doorway; the panel hangs off it to the right, so
            // swinging the hinge open takes the whole door with it.
            Door = IntroBuild.Pivot(Root, "DoorHinge", new Vector3(-DoorW * 0.5f, 0f, RoomD * 0.5f));
            IntroBuild.Part(Door, "DoorPanel", PrimitiveType.Cube, new Vector3(DoorW * 0.5f, DoorH * 0.5f, 0f),
                            new Vector3(DoorW, DoorH, 0.12f), plankDark, castShadows: false);

            // The daylight past the door — a bright glow filling the doorway from just outside, brightened
            // as the door opens. This is the world Max is about to step into.
            _daylight = IntroBuild.Glow(Root, "Daylight", new Vector3(0f, DoorH * 0.5f, RoomD * 0.5f + 0.35f),
                                        1f, IntroPalette.Daylight * 0.15f);
            _daylight.transform.localScale = new Vector3(DoorW * 1.05f, DoorH * 1.05f, 0.1f);
        }
    }
}
