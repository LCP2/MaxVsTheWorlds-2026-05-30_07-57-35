using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Enemies;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1434 - the ARC STRIKER hovers (no legs) and its lightning visibly comes from its own orb. ONE test
    /// (MV-465 Rule 1), Tier 2 resolved values (renderer bounds, line-renderer points). On the base commit
    /// <c>ArcStrikeFx.Spawn</c> takes no orb position and the body has two legs, so this fails there.
    /// </summary>
    public sealed class MV1434ArcStrikerLookTests
    {
        [Test]
        public void ArcStriker_HoversWithNoLegs_AndItsArcAndBoltStartAtTheOrb()
        {
            GameObject root = null, targetGo = null;
            ArcStrikerBattery battery = null;
            ArcStrikeFx fx = null;
            try
            {
                root = new GameObject("MV1434 Striker");
                var m = new Material(Shader.Find("Hidden/InternalErrorShader") ?? Shader.Find("Sprites/Default"));
                RobotBodies.Body body = RobotBodies.Build(EnemyKind.ArcStriker, root.transform, new RobotPalette(m, m, m, m));

                // 1. No leg transforms anywhere under the body.
                Assert.AreEqual(0, body.Legs.Length, "the body lists no legs");
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                    Assert.IsFalse(t.name.Contains("Leg") || t.name.Contains("Foot"), "no leg transform under the body: " + t.name);

                // 2. At rest (hover pivot un-bobbed) the box's lowest renderer bound clears the floor by >= 0.3 m.
                Transform hover = body.OrbitalHover;
                Assert.IsNotNull(hover, "the striker has a hover pivot");
                hover.localPosition = Vector3.zero;
                hover.localRotation = Quaternion.identity;
                float lowest = float.MaxValue;
                foreach (Renderer r in hover.GetComponentsInChildren<Renderer>(true))
                    if (r.name.StartsWith("ArcBox")) lowest = Mathf.Min(lowest, r.bounds.min.y);
                Assert.That(lowest, Is.GreaterThanOrEqualTo(root.transform.position.y + 0.3f), "box underside clears the floor");

                // 3. During a mark an arc runs from the orb to the circle centre.
                Vector3 orb = body.Eyes[body.Eyes.Length - 1].transform.position;
                targetGo = new GameObject("MV1434 Max");
                targetGo.transform.position = root.transform.position + new Vector3(8f, 1f, 0f);
                battery = new ArcStrikerBattery { OrbSource = () => orb };
                battery.Tick(0.02f, root.transform.position, targetGo.transform, true);
                Assert.IsTrue(battery.IsMarking, "a mark started");
                LineRenderer arc = null;
                foreach (LineRenderer lr in battery.Arcs) if (lr.enabled) { arc = lr; break; }
                Assert.IsNotNull(arc, "an arc is active during the mark");
                var pts = new Vector3[arc.positionCount];
                arc.GetPositions(pts);
                Assert.That(Vector3.Distance(pts[0], orb), Is.LessThan(0.2f), "arc starts at the orb");
                Assert.That(Vector3.Distance(pts[pts.Length - 1], battery.MarkCenter), Is.LessThan(0.3f), "arc ends at the circle centre");

                // 4. The strike's bolt leaves the orb, not the sky above the circle.
                fx = ArcStrikeFx.Spawn(battery.MarkCenter, orb);
                var bp = new Vector3[fx.Bolt.positionCount];
                fx.Bolt.GetPositions(bp);
                Assert.That(Vector3.Distance(bp[0], orb), Is.LessThan(0.2f), "bolt starts at the orb");
            }
            finally
            {
                battery?.Dispose();
                if (fx != null) Object.DestroyImmediate(fx.gameObject);
                if (targetGo != null) Object.DestroyImmediate(targetGo);
                if (root != null) Object.DestroyImmediate(root);
            }
        }
    }
}
