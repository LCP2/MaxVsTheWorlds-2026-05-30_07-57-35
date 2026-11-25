using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Enemies;

namespace MaxWorlds.VFX
{
    /// <summary>
    /// MV-1451 hit frazzle: 4-6 small electric arcs crawling over a robot's body for 0.25 s after a
    /// RICOCHET disc (POWER 4+) or a chain arc lands on it. Pure presentation - never touches health or
    /// state. Lives on the robot, bolts in the robot's local space so they follow it.
    /// </summary>
    [MaxWorlds.Core.PerfSection("vfx")]
    public sealed class RobotFrazzleVfx : MonoBehaviour
    {
        public const float Seconds = 0.25f;
        public const int MaxArcs = 6;

        private readonly List<JaggedBolt> _bolts = new List<JaggedBolt>(MaxArcs);
        private Transform _root;
        private float _remaining;
        private int _arcCount;
        private int _countdown;

        /// <summary>True while the frazzle is playing (resolved from the remaining time, not a flag).</summary>
        public bool IsActive => _remaining > 0f && _arcCount > 0;

        public int ActiveArcCount
        {
            get
            {
                if (_remaining <= 0f) return 0;
                int n = 0;
                for (int i = 0; i < _bolts.Count; i++) if (_bolts[i].Active) n++;
                return n;
            }
        }

        public static RobotFrazzleVfx Trigger(RobotEnemy robot)
        {
            if (robot == null) return null;
            var f = robot.GetComponent<RobotFrazzleVfx>();
            if (f == null) f = robot.gameObject.AddComponent<RobotFrazzleVfx>();
            f.Begin();
            return f;
        }

        private void Begin()
        {
            if (_root == null)
            {
                var go = new GameObject("Frazzle");
                go.transform.SetParent(transform, false);
                _root = go.transform;
            }
            while (_bolts.Count < MaxArcs)
                _bolts.Add(new JaggedBolt(_root, "FrazzleArc" + _bolts.Count, false, 0.07f, 0.02f));

            _remaining = Seconds;
            _arcCount = Random.Range(4, MaxArcs + 1);
            for (int i = 0; i < _bolts.Count; i++) _bolts[i].SetActive(i < _arcCount);
            Rerandomize();
        }

        private void Rerandomize()
        {
            _countdown = Random.Range(2, 4);
            for (int i = 0; i < _arcCount; i++)
            {
                Vector3 dir = Random.onUnitSphere;
                dir.y = Mathf.Abs(dir.y) * 0.6f;
                Vector3 start = Vector3.up * 0.6f + dir.normalized * 0.45f;
                Vector3 end = start + Random.onUnitSphere * Random.Range(0.2f, 0.4f);
                _bolts[i].Randomize(start, end, 0.06f);
            }
        }

        private void Update()
        {
            if (_remaining <= 0f) return;
            _remaining -= Time.deltaTime;
            if (_remaining <= 0f)
            {
                for (int i = 0; i < _bolts.Count; i++) _bolts[i].SetActive(false);
                return;
            }
            if (--_countdown <= 0) Rerandomize();
        }
    }
}
