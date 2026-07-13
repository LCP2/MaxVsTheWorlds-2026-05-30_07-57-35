using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Factories;

namespace MaxWorlds.Tests.PlayMode
{
    /// <summary>
    /// The factory's floating health bar must be a small readout on the factory, not a banner over
    /// the arena (YT-71). It's built on a body scaled (3, 2, 3), and a world-space Canvas inherits
    /// that — which is how a 220 px bar came to render 13.2 metres wide. This measures the REAL
    /// bar in world metres, because that's the only thing that would have caught it.
    /// </summary>
    public sealed class FactoryBarPlayTests
    {
        private GameObject _go;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_go != null) Object.Destroy(_go);
            yield return null;
        }

        /// <summary>A Mower Hutch exactly as the scene builds it — including the scaled body.</summary>
        private IEnumerator NewHutch(Vector3 bodyScale)
        {
            _go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _go.name = "Mower Hutch";
            _go.transform.position = new Vector3(0f, 1f, 15f);
            _go.transform.localScale = bodyScale;
            _go.AddComponent<MowerHutch>();
            yield return null;   // Awake builds the bar
        }

        private RectTransform BarCanvas()
        {
            var canvas = _go.GetComponentInChildren<Canvas>(true);
            Assert.IsNotNull(canvas, "the factory has no health bar at all");
            return (RectTransform)canvas.transform;
        }

        /// <summary>The bar's actual width on the ground, in metres.</summary>
        private static float WorldWidth(RectTransform rt) => rt.sizeDelta.x * rt.lossyScale.x;
        private static float WorldHeight(RectTransform rt) => rt.sizeDelta.y * rt.lossyScale.y;

        [UnityTest]
        public IEnumerator TheBarIsSmallerThanTheFactoryItSitsOn()
        {
            yield return NewHutch(new Vector3(3f, 2f, 3f));   // the shipped body
            RectTransform bar = BarCanvas();

            float width = WorldWidth(bar);
            Assert.Less(width, 3f, $"the bar ({width:0.0} m) is wider than the 3 m factory");
            Assert.Greater(width, 0.5f, "shrunk into uselessness — you couldn't read your damage");
        }

        [UnityTest]
        public IEnumerator TheBarIsNowhereNearTheBannerItUsedToBe()
        {
            yield return NewHutch(new Vector3(3f, 2f, 3f));
            // It rendered 13.2 m wide — over half the width of the whole 24 m lawn.
            Assert.Less(WorldWidth(BarCanvas()), 4f);
        }

        [UnityTest]
        public IEnumerator TheBarIsNotStretchedByTheBody()
        {
            // The body is scaled 3 across and 2 up, which skewed the bar as well as inflating it.
            yield return NewHutch(new Vector3(3f, 2f, 3f));
            Vector3 s = BarCanvas().lossyScale;
            Assert.AreEqual(s.x, s.y, 1e-4, "the bar is stretched — it inherited a non-uniform body");
        }

        [UnityTest]
        public IEnumerator TheBarsSizeDoesNotDependOnTheBodysScale()
        {
            // The actual invariant. Re-scale the factory and the bar must not change size — that's
            // what "the bar is a readout, not part of the model" means, and it's what broke.
            yield return NewHutch(new Vector3(3f, 2f, 3f));
            float onTheShippedBody = WorldWidth(BarCanvas());
            Object.Destroy(_go);
            yield return null;

            yield return NewHutch(new Vector3(6f, 5f, 6f));   // a much bigger factory
            float onABigBody = WorldWidth(BarCanvas());

            Assert.AreEqual(onTheShippedBody, onABigBody, 0.05f,
                "the bar grew with the factory — the scale is still being inherited");
        }

        [UnityTest]
        public IEnumerator TheBarSitsAboveTheFactory_NotInsideItOrInOrbit()
        {
            yield return NewHutch(new Vector3(3f, 2f, 3f));
            // Body centre y=1, scaled 2 → the cube's top is at y=2.
            float y = BarCanvas().position.y;
            Assert.Greater(y, 2f, "the bar is buried in the factory");
            Assert.Less(y, 4f, "the bar is floating in the sky");
        }

        [UnityTest]
        public IEnumerator TheBarStaysProportionate_NotAWaferOrASlab()
        {
            yield return NewHutch(new Vector3(3f, 2f, 3f));
            RectTransform bar = BarCanvas();
            float ratio = WorldWidth(bar) / WorldHeight(bar);
            Assert.Greater(ratio, 3f, "too tall to read as a bar");
            Assert.Less(ratio, 14f, "a hairline");
        }
    }
}
