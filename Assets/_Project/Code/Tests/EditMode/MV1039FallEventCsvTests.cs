using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;

namespace MaxWorlds.Tests.EditMode
{
    /// <summary>
    /// MV-1039 (Lee, TestFlight v0.11.2): a fall in World 1 a22 with no shed nearby (weakening MV-1022's
    /// shed hypothesis) went entirely unrecorded on disk -- <see cref="FallEventLog.Record"/> only ever
    /// wrote a ring buffer and a <see cref="Debug.LogWarning"/>, both read only by someone watching live.
    /// This proves <c>Record</c> now also writes ONE row to the session events CSV
    /// (<see cref="Bootstrap.ActiveSessionRecorder"/>), carrying a RESOLVED floor-probe verdict (Tier 2 --
    /// the raycast's own actual hit, never an authored constant) for both positions the fall names.
    ///
    /// Must FAIL on the pre-fix commit: <c>FallEventLog.Record</c> never touches
    /// <c>Bootstrap.ActiveSessionRecorder</c> at all, so the events CSV never gains a FALL row -- it stays
    /// at just its header line.
    /// // Guards MV-1039
    /// </summary>
    public sealed class MV1039FallEventCsvTests
    {
        private GameObject _floorGo;
        private string _tempDir;

        [SetUp]
        public void SetUp() => FallEventLog.Reset();

        [TearDown]
        public void TearDown()
        {
            Bootstrap.SetActiveSessionRecorderForTest(null);
            FallEventLog.Reset();
            if (_floorGo != null) UnityEngine.Object.DestroyImmediate(_floorGo);
            if (_tempDir != null && Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
        }

        [Test]
        public void Record_WritesOneFallRow_NamingTheFloorColliderForProbe1_AndNoneForProbe2()
        {
            // Same remote-coordinate hygiene as MV955FallEventLogTests -- keeps this test's own floor
            // geometry structurally isolated from whatever another EditMode test in the same shared
            // batch-mode scene left behind near the origin.
            const float ox = -84213f, oz = 51087f;
            Vector3 lastGrounded = new Vector3(ox, 0f, oz);
            // Far enough from the floor collider below that the second probe's raycast hits nothing.
            Vector3 firstOutOfPlay = new Vector3(ox + 500f, 0f, oz);

            _floorGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _floorGo.name = "MV1039-ProbeFloor";
            _floorGo.transform.position = new Vector3(ox, -0.05f, oz);
            _floorGo.transform.localScale = new Vector3(4f, 0.1f, 4f); // top surface sits at y=0
            Physics.SyncTransforms();

            _tempDir = Path.Combine(Path.GetTempPath(), "mv1039-" + Guid.NewGuid().ToString("N"));
            var recorder = new PerfSessionRecorder(_tempDir, "testbuild", DateTime.UtcNow);
            Bootstrap.SetActiveSessionRecorderForTest(recorder);

            var map = new MapData
            {
                zones = new[] { new MapZone { id = "area1", x = ox, z = oz, width = 50f, depth = 50f, level = 0 } },
                entities = Array.Empty<MapEntity>(),
            };

            FallEventLog.Record("max", map, firstOutOfPlay, lastGrounded, 1f / 60f);
            recorder.Flush();

            string[] eventLines = File.ReadAllLines(recorder.EventsCsvPath);
            Assert.That(eventLines.Length, Is.EqualTo(2),
                "MV-1039: header + exactly one FALL row -- FallEventLog.Record must write one row per fall");
            Assert.That(eventLines[1], Does.Contain("FALL"),
                "MV-1039: the written row must be tagged FALL");
            Assert.That(eventLines[1], Does.Contain("MV1039-ProbeFloor"),
                "MV-1039: probe 1 (raycast from LastGroundedPosition) must name the real floor collider it hit");
            Assert.That(eventLines[1], Does.Contain("floorProbe2=none"),
                "MV-1039: probe 2 (raycast from FirstOutOfPlayPosition, with nothing underneath) must read none");
        }
    }
}
