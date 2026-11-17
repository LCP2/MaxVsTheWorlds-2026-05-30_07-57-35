using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Enemies;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>MV-1389 (Tier 2, resolved values): the Shield Projector beam is a thin flowing tether, not a flat bar.</summary>
    public sealed class MV1389ProjectorBeamTetherTests
    {
        [Test]
        public void BeamIsThinCoreWithManyPointsAndScrollingTexture_MV1389()
        {
            var dome = ProjectorDome.Spawn(Vector3.zero, 4f, new Vector3(6f, 2.3f, 0f));
            try
            {
                var core = dome.transform.Find("Beam").GetComponent<LineRenderer>();
                float w = core.widthMultiplier * core.widthCurve.Evaluate(0.5f);
                Assert.LessOrEqual(w, 0.08f, "core resolved width");
                Assert.GreaterOrEqual(core.positionCount, 8, "beam positions");

                dome.Tick(0.016f);
                float o1 = core.sharedMaterial.mainTextureOffset.x;
                dome.Tick(0.1f);
                float o2 = core.sharedMaterial.mainTextureOffset.x;
                Assert.AreNotEqual(o1, o2, "texture offset must scroll between ticks");
            }
            finally { Object.DestroyImmediate(dome.gameObject); }
        }
    }
}
