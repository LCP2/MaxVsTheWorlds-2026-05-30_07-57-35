using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using MaxWorlds.Core;
using MaxWorlds.UI;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// The floating HP readout over each unit (YT-111).
    ///
    /// Measured in world metres rather than authored numbers, for the reason FactoryBarPlayTests
    /// records: a bar authored at 180 px on a body that carries a scale renders metres wide, and
    /// only a measurement in the units the player sees would catch it. Robots make that sharper —
    /// a rusher is 0.8x0.7x0.8 and a bruiser 1.15 all round, and the scale is stamped on AFTER the
    /// component exists.
    ///
    /// MV-464: migrated from WorldHealthBarPlayTests. <see cref="WorldHealthBar.Attach"/> builds
    /// synchronously (no Awake/OnEnable dependency), so every assertion that only needs the state
    /// right after Attach() moved with zero behaviour change. Anything that needs a SECOND draw — a
    /// changed Hp, a moved camera — normally comes from LateUpdate, which never fires outside Play
    /// mode, so those tests invoke the private Refresh() directly instead, same pattern
    /// WorldHealthBarNameplateTests already established for MV-312. The two tests that build a real
    /// <c>RobotEnemy</c> (whose Awake() is what attaches the bar) stay in PlayMode — Awake never runs
    /// as a side effect of AddComponent outside Play mode, and extracting it is bigger surgery than
    /// this pass, see WorldHealthBarPlayTests.
    /// </summary>
    public sealed class WorldHealthBarTests
    {
        private GameObject _go;
        private GameObject _go2;
        private GameObject _cam;

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
            if (_go2 != null) Object.DestroyImmediate(_go2);
            if (_cam != null) Object.DestroyImmediate(_cam);
        }

        /// <summary>A stand-in unit, so these test the bar rather than the robot AI.</summary>
        private sealed class FakeUnit : MonoBehaviour, IHealthReadout
        {
            public float Hp = 100f;
            public float MaxHp = 100f;
            public bool Alive = true;
            public float HealthNormalized => MaxHp > 0f ? Mathf.Clamp01(Hp / MaxHp) : 0f;
            public float HealthCurrent => Hp;
            public string ReadoutName => "TEST UNIT";
            public bool IsAlive => Alive;
        }

        /// <summary>Refresh() is private and normally only called from LateUpdate — invoke it directly
        /// to simulate the next frame's redraw without a real player loop.</summary>
        private static void Refresh(WorldHealthBar bar)
        {
            var m = typeof(WorldHealthBar).GetMethod("Refresh", BindingFlags.NonPublic | BindingFlags.Instance);
            m.Invoke(bar, null);
        }

        private FakeUnit NewUnit(Vector3 bodyScale, bool alwaysShow = false)
        {
            _go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            _go.transform.localScale = bodyScale;
            var unit = _go.AddComponent<FakeUnit>();
            WorldHealthBar.Attach(_go, unit, heightAboveCentre: 1.15f, worldWidth: 1.1f, alwaysShow);
            return unit;
        }

        private WorldHealthBar Bar => _go.GetComponent<WorldHealthBar>();
        private RectTransform Canvas => (RectTransform)_go.GetComponentInChildren<Canvas>(true).transform;

        /// <summary>A Max-like unit: a life bar with a water gauge stacked above (YT-121).</summary>
        private FakeUnit NewUnitWithWater(System.Func<float> water)
        {
            _go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            var unit = _go.AddComponent<FakeUnit>();
            WorldHealthBar.Attach(_go, unit, heightAboveCentre: 1.55f, worldWidth: 1.5f,
                                  alwaysShow: true, secondary: water,
                                  secondaryColor: new Color(0.2f, 0.62f, 0.92f));
            return unit;
        }

        private static float WorldWidth(RectTransform rt) => rt.sizeDelta.x * rt.lossyScale.x;

        // ---------------------------------------------------------------- never covers Max (YT-149)

        /// <summary>
        /// The bug this pins: at the fixed ~72° camera, Max's head slid UNDER his own floating bar
        /// whenever he ran up-screen (away from camera). It is a projection trap, so a declared-size
        /// check can't see it — it only shows up once you put the real camera at the real angle and
        /// ask where the bar and his head land ON SCREEN.
        ///
        /// Two things conspire: a world-up bar offset projects at only cos(72°) ≈ 0.31 of screen-up,
        /// so the ~0.8 m of real headroom is a sliver on screen; and the 3 m camera look-ahead
        /// (CameraTargetRig) re-aims the camera to a steeper ~79° over the top of him the instant he
        /// runs up-screen, lifting his silhouette into that sliver. The test recreates both — a camera
        /// dead-on him, and a camera led 3 m past him — and asserts his hair-tips never rise into the
        /// bar in either.
        /// </summary>
        [Test]
        public void TheBarNeverCoversMaxsHeadEvenWhenHeRunsUpScreen()
        {
            Camera cam = SetupMainCamera();

            _go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            _go.transform.position = new Vector3(0f, 1.0f, 0f);   // capsule centre 1 m up, like Max
            var unit = _go.AddComponent<FakeUnit>();
            // Max's real bar: the water gauge stacked above the life bar, at his authored height/width.
            var bar = WorldHealthBar.Attach(_go, unit, heightAboveCentre: 1.65f, worldWidth: 2.1f,
                                  alwaysShow: true, secondary: () => 1f,
                                  secondaryColor: new Color(0.2f, 0.62f, 0.92f));

            Vector3 ground = Vector3.zero;
            Vector3 hairTip = new Vector3(0f, 1.83f, 0f);   // "stands 1.83 m to the tips of his hair" (MaxRig)

            // Standing still: the camera sits dead on him at the fixed pitch (60° post-MV-468).
            AimFollowCamera(cam, ground, leadNorth: 0f);
            Refresh(bar);
            AssertBarClearsHead(cam, hairTip, "standing still");

            // Running up-screen: the look-ahead leads the camera 3 m past him, steepening the view over
            // the top of his head — the frame the bug actually happened in.
            AimFollowCamera(cam, ground, leadNorth: 3f);
            Refresh(bar);
            AssertBarClearsHead(cam, hairTip, "running up-screen (away from camera)");
        }

        /// <summary>A MainCamera so <see cref="Camera.main"/> resolves inside the bar.</summary>
        private Camera SetupMainCamera()
        {
            _cam = new GameObject("MainCamera", typeof(Camera));
            _cam.tag = "MainCamera";
            return _cam.GetComponent<Camera>();
        }

        /// <summary>Place the camera exactly as FixedAngleCameraRig + CameraTargetRig would: the fixed
        /// 60° pitch (MV-468), the phone pull-back, and a look-ahead <paramref name="leadNorth"/>
        /// metres past the subject toward where he is running.</summary>
        private static void AimFollowCamera(Camera cam, Vector3 ground, float leadNorth)
        {
            const float pitch = 60f, distance = 23f;
            float rad = pitch * Mathf.Deg2Rad;
            Vector3 offset = new Vector3(0f, distance * Mathf.Sin(rad), -distance * Mathf.Cos(rad));
            Vector3 target = ground + new Vector3(0f, 0f, leadNorth);
            cam.transform.position = target + offset;
            cam.transform.rotation = Quaternion.Euler(pitch, 0f, 0f);
        }

        /// <summary>Project the bar's lowest corner and Max's hair-tip to the screen and fail if the bar
        /// reaches down over his head. Screen Y grows upward, so "clear" means the bar's bottom is
        /// strictly higher on screen than the top of him.</summary>
        private void AssertBarClearsHead(Camera cam, Vector3 hairTipWorld, string when)
        {
            var corners = new Vector3[4];
            FindImage("Outline").rectTransform.GetWorldCorners(corners);

            float barBottomY = float.MaxValue;
            foreach (Vector3 c in corners)
                barBottomY = Mathf.Min(barBottomY, cam.WorldToScreenPoint(c).y);

            float headTopY = cam.WorldToScreenPoint(hairTipWorld).y;
            Assert.That(barBottomY, Is.GreaterThan(headTopY),
                $"{when}: the bar's bottom edge sits at {barBottomY:0} px, at/under the top of Max's " +
                $"head at {headTopY:0} px — the bar is covering him (YT-149)");
        }

        // ---------------------------------------------------------------- Max's water stack (YT-121)

        [Test]
        public void MaxsBarCarriesAWaterGaugeStackedAboveItsLifeBar()
        {
            float water = 0.5f;
            NewUnitWithWater(() => water);

            Assert.That(Bar.HasSecondary, Is.True, "Max's stack has no water gauge above the life bar");

            var waterFill = FindImage("Water Fill");
            var lifeFill = FindImage("Fill");
            Assert.That(waterFill.fillAmount, Is.EqualTo(0.5f).Within(0.02f), "the gauge did not read the tank");

            // Stacked ABOVE: the water gauge sits higher on screen than the life bar.
            var wc = new Vector3[4]; var lc = new Vector3[4];
            waterFill.rectTransform.GetWorldCorners(wc);
            lifeFill.rectTransform.GetWorldCorners(lc);
            Assert.That(wc[1].y, Is.GreaterThan(lc[1].y),
                "the water gauge must sit above the life bar, not below or on it");
        }

        [Test]
        public void TheWaterGaugeTracksTheTankLive()
        {
            float water = 1f;
            NewUnitWithWater(() => water);

            water = 0.2f;
            Refresh(Bar);
            Assert.That(FindImage("Water Fill").fillAmount, Is.EqualTo(0.2f).Within(0.02f),
                "draining the tank must drain the gauge without a rebuild");
        }

        [Test]
        public void ARobotHasNoWaterGauge()
        {
            NewUnit(Vector3.one).Hp = 50f;
            Assert.That(Bar.HasSecondary, Is.False, "only Max carries a water gauge; robots must not");
        }

        // ---------------------------------------------------------------- prominence (YT-125)

        [Test]
        public void TheBarHasASolidDarkOutlineThatPops()
        {
            NewUnit(Vector3.one).Hp = 50f;

            var outline = FindImage("Outline");
            Assert.That(outline, Is.Not.Null, "the beefed bar has no dark outline capsule");
            Assert.That(outline.color.a, Is.GreaterThan(0.8f), "the outline is too faint to read as a border");
            // Dark: the outline should be near-black, not another bright element.
            Assert.That(outline.color.r + outline.color.g + outline.color.b, Is.LessThan(0.3f),
                        "the outline is not dark enough to separate the bar from the grass");

            // Behind the fill, so the fill draws over it and only the border shows.
            Assert.That(outline.rectTransform.GetSiblingIndex(),
                        Is.LessThan(FindImage("Fill").rectTransform.GetSiblingIndex()),
                        "the outline must sit behind the fill");
        }

        [Test]
        public void TheBarIsProminentlyWideAndFlat()
        {
            NewUnit(Vector3.one).Hp = 50f;

            // Prominence comes from WIDTH now (YT-136), not height. YT-128 chased chunkiness through
            // height (a > 0.30 m threshold); that reared a tall bar up over Max and buried him. This
            // re-points at the two properties that actually matter: the bar is wide enough to read at
            // the 23 m phone zoom, and it is FLAT — much wider than tall — so all of Max stays visible
            // above and below it. Measured as rect x world-scale (not GetWorldCorners, which
            // foreshortens under the billboard and made the old check camera-dependent).
            var rt = FindImage("Outline").rectTransform;
            float worldWidth = rt.rect.width * rt.lossyScale.x;
            float worldHeight = rt.rect.height * rt.lossyScale.y;

            // This test unit is 1.1 m wide; the robots that ship are 1.5 and Max 2.1, so real bars are
            // wider still.
            Assert.That(worldWidth, Is.GreaterThan(0.9f),
                        $"the bar is only {worldWidth:0.00} m wide — too narrow to read at 23 m zoom");
            Assert.That(worldWidth / worldHeight, Is.GreaterThan(3f),
                        $"the bar is {worldWidth / worldHeight:0.0}:1 — not flat enough; a tall bar " +
                        "buries the character it floats over (YT-136)");
        }

        [Test]
        public void TheWaterGaugeGetsTheSameOutlineTreatment()
        {
            NewUnitWithWater(() => 0.5f);

            Assert.That(FindImage("Water Outline"), Is.Not.Null,
                        "the water gauge must be beefed up like the life bar, not left a hairline");
        }

        // ---------------------------------------------------------------- size and place

        [Test]
        public void TheBarIsAboutAsWideAsAskedForRegardlessOfTheBodysScale()
        {
            foreach (Vector3 scale in new[]
                     { Vector3.one, new Vector3(0.8f, 0.7f, 0.8f), new Vector3(1.15f, 1.15f, 1.15f) })
            {
                NewUnit(scale).Hp = 50f;

                float w = WorldWidth(Canvas);
                Assert.That(w, Is.EqualTo(1.1f).Within(0.05f),
                            $"a body scaled {scale} rendered a {w:0.00} m bar — the scale leaked in");

                Object.DestroyImmediate(_go);
                _go = null;
            }
        }

        [Test]
        public void TheBarSitsAboveTheUnitNotInsideItOrInOrbit()
        {
            NewUnit(new Vector3(0.8f, 0.7f, 0.8f)).Hp = 50f;

            float above = Canvas.position.y - _go.transform.position.y;
            Assert.That(above, Is.GreaterThan(0.5f), "the bar is buried in the body");
            Assert.That(above, Is.LessThan(2.5f), "the bar is floating in orbit above the unit");
        }

        // ---------------------------------------------------------------- shear-free on rotated bodies (MV-302)

        /// <summary>
        /// Pins MV-302: an area gate is a long, thin box (wide local X, thin local Z) that is yaw
        /// rotated 90 degrees for an E/W-wall doorway — SAME anisotropic local scale as a horizontal
        /// (N/S-wall) gate, just spun (MapRuntime.BuildAreaGate). Parenting a camera-facing bar
        /// straight to a body that is BOTH anisotropically scaled AND independently rotated renders it
        /// SHEARED — Transform.lossyScale can't even see it, since Unity's scale/rotation composition
        /// assumes no shear exists. This recreates that exact body shape in both orientations and
        /// checks the rendered bar is still a clean, undistorted rectangle of the same size either way.
        /// </summary>
        [Test]
        public void TheBarIsNotShearedOnAWideYawRotatedBody()
        {
            Camera cam = SetupMainCamera();

            // Same local scale MapRuntime.BuildAreaGate gives every area gate: wide along local X,
            // thin along local Z. Only the rotation differs between a horizontal and a vertical gate.
            Vector3 gateScale = new Vector3(6f, 3f, 0.6f);

            _go = new GameObject("HorizontalGate");
            _go.transform.localScale = gateScale;
            var hUnit = _go.AddComponent<FakeUnit>();
            var hBar = WorldHealthBar.Attach(_go, hUnit, heightAboveCentre: 2.2f, worldWidth: 1.8f, alwaysShow: true);

            _go2 = new GameObject("VerticalGate");
            _go2.transform.localScale = gateScale;
            _go2.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
            var vUnit = _go2.AddComponent<FakeUnit>();
            var vBar = WorldHealthBar.Attach(_go2, vUnit, heightAboveCentre: 2.2f, worldWidth: 1.8f, alwaysShow: true);

            // The real fixed camera angle (AimFollowCamera), aimed at the gates — the shear only
            // shows up once the bar's pivot has to rotate away from its parent to face a real camera.
            AimFollowCamera(cam, Vector3.zero, leadNorth: 0f);
            Refresh(hBar);
            Refresh(vBar);

            RectTransform hOutline = FindImageOn(_go, "Outline").rectTransform;
            RectTransform vOutline = FindImageOn(_go2, "Outline").rectTransform;

            AssertRectangular(hOutline, "horizontal gate");
            AssertRectangular(vOutline, "vertical gate");

            float hWidth = WorldWidth(hOutline);
            float vWidth = WorldWidth(vOutline);
            Assert.That(vWidth, Is.EqualTo(hWidth).Within(0.05f),
                $"the vertical gate's bar rendered {vWidth:0.00} m wide vs the horizontal gate's " +
                $"{hWidth:0.00} m — a vertical gate's bar must draw the same clean way (MV-302)");
        }

        /// <summary>Fails if <paramref name="rt"/>'s four world corners aren't a right-angled
        /// rectangle — the signature of shear (a parallelogram with non-perpendicular, unequal
        /// sides), as distinct from a simple rotation (which stays rectangular).</summary>
        private static void AssertRectangular(RectTransform rt, string label)
        {
            var c = new Vector3[4];
            rt.GetWorldCorners(c);

            Vector3 e01 = c[1] - c[0];
            Vector3 e12 = c[2] - c[1];
            Vector3 e32 = c[2] - c[3];

            float cosAngle = Vector3.Dot(e01.normalized, e12.normalized);
            Assert.That(Mathf.Abs(cosAngle), Is.LessThan(0.05f),
                $"{label}: the bar's adjacent edges aren't perpendicular (cos={cosAngle:0.000}) — it is sheared");

            Assert.That(e01.magnitude, Is.EqualTo(e32.magnitude).Within(0.05f),
                $"{label}: opposite edges of the bar have different lengths — it is sheared");
        }

        private static UnityEngine.UI.Image FindImageOn(GameObject go, string name)
        {
            foreach (UnityEngine.UI.Image i in go.GetComponentsInChildren<UnityEngine.UI.Image>(true))
                if (i.name == name) return i;
            Assert.Fail($"no '{name}' image on {go.name}'s bar");
            return null;
        }

        // ---------------------------------------------------------------- what it says

        [Test]
        public void TheBarTracksDamageAndPrintsTheNumber()
        {
            FakeUnit unit = NewUnit(Vector3.one);
            unit.Hp = 25f;
            Refresh(Bar);

            var fill = FindImage("Fill");
            Assert.That(fill.fillAmount, Is.EqualTo(0.25f).Within(0.01f));

            bool printed = false;
            foreach (UnityEngine.UI.Text t in _go.GetComponentsInChildren<UnityEngine.UI.Text>(true))
                if (t.text == "25") printed = true;

            Assert.That(printed, Is.True, "the bar shows no numeric HP — the ticket asks for the figure");
        }

        [Test]
        public void TheBarNamesTheUnit()
        {
            NewUnit(Vector3.one).Hp = 50f;

            bool named = false;
            foreach (UnityEngine.UI.Text t in _go.GetComponentsInChildren<UnityEngine.UI.Text>(true))
                if (t.text == "TEST UNIT") named = true;

            Assert.That(named, Is.True, "the bar does not say what it is sitting on");
        }

        [Test]
        public void TheBarChangesColourAsThingsGetSerious()
        {
            FakeUnit unit = NewUnit(Vector3.one);
            unit.Hp = 90f;
            Refresh(Bar);
            Color healthy = FindImage("Fill").color;

            unit.Hp = 10f;
            Refresh(Bar);
            Color critical = FindImage("Fill").color;

            Assert.That(critical, Is.Not.EqualTo(healthy),
                        "a bar at 10% looks the same as one at 90%");
            Assert.That(critical.r, Is.GreaterThan(critical.g),
                        "the critical colour should read as danger, not as health");
        }

        // ---------------------------------------------------------------- clutter rules

        [Test]
        public void AnUntouchedRobotDoesNotCarryABar()
        {
            NewUnit(Vector3.one).Hp = 100f;

            Assert.That(Bar.Showing, Is.False,
                        "a field of full-health robots each with a bar is the clutter the ticket warned about");
        }

        [Test]
        public void BeingHitBringsTheBarOut()
        {
            FakeUnit unit = NewUnit(Vector3.one);
            unit.Hp = 100f;
            Assert.That(Bar.Showing, Is.False);

            unit.Hp = 99f;
            Refresh(Bar);
            Assert.That(Bar.Showing, Is.True, "took damage and still shows nothing");
        }

        [Test]
        public void MaxAlwaysCarriesHisOwnBarEvenAtFullHealth()
        {
            NewUnit(Vector3.one, alwaysShow: true).Hp = 100f;

            Assert.That(Bar.Showing, Is.True,
                        "you should be able to find your own health without being hit first");
        }

        [Test]
        public void ADeadUnitTakesItsBarWithIt()
        {
            FakeUnit unit = NewUnit(Vector3.one, alwaysShow: true);
            unit.Hp = 0f;
            unit.Alive = false;
            Refresh(Bar);

            Assert.That(Bar.Showing, Is.False, "a corpse is still advertising its health");
        }

        private UnityEngine.UI.Image FindImage(string name)
        {
            foreach (UnityEngine.UI.Image i in _go.GetComponentsInChildren<UnityEngine.UI.Image>(true))
                if (i.name == name) return i;
            Assert.Fail($"no '{name}' image on the bar");
            return null;
        }
    }
}
