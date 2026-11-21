using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MaxWorlds.Arena;
using MaxWorlds.Factories;
using MaxWorlds.VFX;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1409: City pylon cables (a2 among them) were never drawn in a rendered frame. The cable's
    /// <see cref="LineRenderer"/> was resolved <c>enabled == false</c> in the live capture, i.e. switched
    /// off after it was built. <see cref="FactoryDoorway"/> sweeps every scene renderer overlapping a
    /// replicator's entry ramp and disables "decorative dressing" - and a cable lying across that ramp's
    /// footprint is swept with it. Tier 2 (a resolved renderer state): after the real City build and the
    /// real door install, every fed pylon's cable line and dots are still enabled.
    /// Guards MV-1409 (sole guard on a known defect - exempt from culling).
    /// </summary>
    public sealed class MV1409PylonCableStaysDrawnTests
    {
        private GameObject _root;

        [TearDown]
        public void TearDown()
        {
            foreach (FactoryDoorway d in new List<FactoryDoorway>(FactoryDoorway.AllDoors))
                if (d != null) Object.DestroyImmediate(d.gameObject);
            if (_root != null) Object.DestroyImmediate(_root);
        }

        [Test]
        public void EveryFedCityPylon_CableLineAndDots_StayEnabledAfterDoorsInstall()
        {
            LogAssert.ignoreFailingMessages = true;

            WorldConfig cfg = WorldLibrary.Load(WorldLibrary.City);
            Assert.IsNotNull(cfg, "setup failure: City did not load");
            Assert.IsTrue(WorldMapLoader.TryLoad(cfg, out MapData map, out string why), $"setup failure: {why}");

            _root = new GameObject("MV1409 root");
            MapBuild built = MapRuntime.Build(map, _root.transform);
            // Awake never fires from AddComponent in EditMode; a live boot has built every pylon (and its
            // cable) by the time FactoryDoorway.Install runs at AfterSceneLoad, so do the same here.
            foreach (Pylon p in built.Pylons) p.Build();
            foreach (Replicator r in built.Replicators) r.Build();
            FactoryDoorway.Install();

            int cables = 0;
            var offenders = new List<string>();
            foreach (LineRenderer lr in _root.GetComponentsInChildren<LineRenderer>(true))
            {
                if (!lr.gameObject.name.EndsWith("_Cable")) continue;
                cables++;
                if (!lr.enabled) offenders.Add(lr.gameObject.name + " (line)");
                foreach (MeshRenderer dot in lr.GetComponentsInChildren<MeshRenderer>(true))
                    if (!dot.enabled) offenders.Add(lr.gameObject.name + " (" + dot.name + ")");
            }

            Assert.Greater(cables, 0, "setup failure: the City built no pylon cables at all");
            Assert.IsEmpty(offenders, "cable renderers switched off after build:\n" + string.Join("\n", offenders));
        }
    }
}
