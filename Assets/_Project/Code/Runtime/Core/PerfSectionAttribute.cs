using System;

namespace MaxWorlds.Core
{
    /// <summary>
    /// MV-968: marks a <c>MonoBehaviour</c> that declares its own <c>Update</c>, <c>LateUpdate</c>,
    /// <c>FixedUpdate</c> or <c>OnGUI</c> with the named system it belongs to, so
    /// <see cref="PerfTelemetry"/> can attribute per-frame cost to a system a device reading actually
    /// groups by (robot, rig, sentinel, replicator, hud, ...) instead of every cost landing in one
    /// undifferentiated bucket. <c>Mv968PerfSectionCoverageTests</c> reflects over every Runtime
    /// MonoBehaviour and fails, listing offenders by name, if any of those four methods exists without
    /// this attribute on the declaring type — so a new system that forgets to tag itself is a build
    /// failure, not a silent gap in the readout.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = false)]
    public sealed class PerfSectionAttribute : Attribute
    {
        public string Name { get; }

        public PerfSectionAttribute(string name) => Name = name;
    }
}
