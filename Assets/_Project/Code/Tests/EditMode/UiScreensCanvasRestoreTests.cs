using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using MaxWorlds.Dev;
using Object = UnityEngine.Object;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-444 AC3: <c>UiScreensDirector</c>'s capture technique flips a screen's canvas to
    /// <c>ScreenSpaceCamera</c> for the render and must put it back exactly as found afterwards — render
    /// mode, world camera, plane distance and sorting order — or a capture run leaves the real screen
    /// mis-parented, the MV-440 failure shape. <c>ShowCanvasOnCamera</c>/<c>RestoreCanvas</c> are private
    /// instance methods (no public capture API — this is dev-only tooling), so this suite drives them via
    /// reflection, the same idiom used elsewhere in this codebase for director-internal behaviour.
    /// </summary>
    public sealed class UiScreensCanvasRestoreTests
    {
        private GameObject _canvasGo;
        private GameObject _camGo;
        private GameObject _directorGo;
        private Canvas _canvas;
        private CanvasScaler _scaler;
        private Camera _cam;
        private UiScreensDirector _director;
        private MethodInfo _show;
        private MethodInfo _restore;

        [SetUp]
        public void Build()
        {
            _canvasGo = new GameObject("TestScreenCanvas");
            _canvas = _canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 250;
            _canvas.planeDistance = 5f;
            _canvas.worldCamera = null;

            _scaler = _canvasGo.AddComponent<CanvasScaler>();
            _scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            _scaler.referenceResolution = new Vector2(1920, 1080);
            _scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            _scaler.matchWidthOrHeight = 1f;
            _scaler.enabled = true;

            _camGo = new GameObject("TestCaptureCamera");
            _cam = _camGo.AddComponent<Camera>();

            _directorGo = new GameObject("TestUiScreensDirector");
            _director = _directorGo.AddComponent<UiScreensDirector>();

            var t = typeof(UiScreensDirector);
            _show = t.GetMethod("ShowCanvasOnCamera", BindingFlags.NonPublic | BindingFlags.Instance);
            _restore = t.GetMethod("RestoreCanvas", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That(_show, Is.Not.Null, "ShowCanvasOnCamera not found — signature changed?");
            Assert.That(_restore, Is.Not.Null, "RestoreCanvas not found — signature changed?");
        }

        [TearDown]
        public void Teardown()
        {
            Object.DestroyImmediate(_canvasGo);
            Object.DestroyImmediate(_camGo);
            Object.DestroyImmediate(_directorGo);
        }

        [Test]
        public void ShowActuallyFlipsTheCanvasToCameraSpace()
        {
            _show.Invoke(_director, new object[] { _canvas, _cam, 1920, 1080 });

            Assert.That(_canvas.renderMode, Is.EqualTo(RenderMode.ScreenSpaceCamera));
            Assert.That(_canvas.worldCamera, Is.EqualTo(_cam));
            Assert.That(_scaler.enabled, Is.False, "CanvasScaler must be disabled while scaleFactor is driven manually");
        }

        [Test]
        public void RestoreReturnsRenderModeWorldCameraPlaneDistanceAndSortingOrder()
        {
            _show.Invoke(_director, new object[] { _canvas, _cam, 1920, 1080 });
            _restore.Invoke(_director, null);

            Assert.That(_canvas.renderMode, Is.EqualTo(RenderMode.ScreenSpaceOverlay));
            Assert.That(_canvas.worldCamera, Is.Null);
            Assert.That(_canvas.planeDistance, Is.EqualTo(5f));
            Assert.That(_canvas.sortingOrder, Is.EqualTo(250));
        }

        [Test]
        public void ShowMovesTheCanvasOntoTheUiLayerAndRestoreReturnsItsOriginalLayer()
        {
            // MV-444: the capture camera sits at the scene's default transform, inside the 3D scene, and
            // must render ONLY the canvas — WeaponsScreen/HudController never put their canvas on a
            // dedicated layer, so Show moves it onto "UI" (otherwise unused in this project) for the
            // capture and Restore must put it back, or every subsequent frame renders the canvas on the
            // wrong layer for every OTHER camera in the scene too.
            int originalLayer = _canvasGo.layer;
            int uiLayer = LayerMask.NameToLayer("UI");
            Assert.That(uiLayer, Is.GreaterThanOrEqualTo(0), "test assumes this project's TagManager still defines a 'UI' layer");
            Assert.That(originalLayer, Is.Not.EqualTo(uiLayer), "test needs to start off the UI layer to prove Show actually moves it");

            _show.Invoke(_director, new object[] { _canvas, _cam, 1920, 1080 });
            Assert.That(_canvasGo.layer, Is.EqualTo(uiLayer));

            _restore.Invoke(_director, null);
            Assert.That(_canvasGo.layer, Is.EqualTo(originalLayer));
        }

        [Test]
        public void RestoreReEnablesTheCanvasScaler()
        {
            _show.Invoke(_director, new object[] { _canvas, _cam, 1920, 1080 });
            _restore.Invoke(_director, null);

            Assert.That(_scaler.enabled, Is.True);
        }

        [Test]
        public void RestoreIsSafeToCallWithNothingShowing()
        {
            Assert.DoesNotThrow(() => _restore.Invoke(_director, null));
        }

        [Test]
        public void RestoreIsIdempotentAfterAlreadyRestoring()
        {
            _show.Invoke(_director, new object[] { _canvas, _cam, 1920, 1080 });
            _restore.Invoke(_director, null);

            // A second restore (e.g. a defensive call in a finally block after the first already ran)
            // must be a no-op, not stomp the now-live screen state with stale snapshotted values.
            _canvas.sortingOrder = 999;
            _restore.Invoke(_director, null);

            Assert.That(_canvas.sortingOrder, Is.EqualTo(999));
        }
    }
}
