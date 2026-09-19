using UnityEngine;
using UnityEngine.Rendering;

namespace MaxWorlds.VFX
{
    /// <summary>
    /// MV-854: owns the one dynamic mesh all 31 locks render through — a single draw call, rebuilt
    /// every <c>LateUpdate</c> from <see cref="MaxHair"/>'s pure spring maths (kept pure precisely so
    /// an EditMode test can drive it without this class, a <see cref="GameObject"/> or a scene — see
    /// that class's doc). Lives under Max's Head pivot, the same space the static hair cap
    /// (<see cref="MaxBody.Build"/>) already draws in, so it never has to know the head is yawing,
    /// bobbing or leaning independently of the rest of him.
    /// </summary>
    public sealed class MaxHairRig
    {
        /// <summary>Front + back copies of each ribbon vertex — see <see cref="BuildIndices"/> for why
        /// a shared vertex per triangle pair is not "double-sided", just wrong.</summary>
        private const int VertsPerLock = MaxHair.NodeCount * 2 * 2;

        private readonly Transform _head;
        private readonly HairLockSpec[] _specs;
        private readonly HairLockState[] _states;
        private readonly Vector3[] _nodes = new Vector3[MaxHair.NodeCount];
        private readonly Vector3[] _vertices;
        private readonly Mesh _mesh;

        public MaxHairRig(Transform head, Material material)
        {
            _head = head;
            _specs = MaxHair.BuildLayout();
            _states = new HairLockState[_specs.Length];
            for (int i = 0; i < _specs.Length; i++) _states[i] = new HairLockState(_specs[i]);

            _vertices = new Vector3[_specs.Length * VertsPerLock];

            var go = new GameObject("HairLocks");
            go.transform.SetParent(head, worldPositionStays: false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            // YT-186: shadows off. The fixed camera reads a character by shape and eye colour, and
            // thirty-one shadow-casting ribbons is pure cost for nothing anyone can see.
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            _mesh = new Mesh { name = "MaxHairLocks" };
            _mesh.MarkDynamic();
            _mesh.SetVertices(_vertices);
            _mesh.SetIndices(BuildIndices(_specs.Length), MeshTopology.Triangles, 0);
            go.AddComponent<MeshFilter>().sharedMesh = _mesh;

            // First pose, so he never shows a frame of un-simulated hair collapsed at the roots.
            Tick(0f, Vector3.forward, 0f, 0f, 0f, MaxHair.WindStrengthDefault, MaxHair.WindDirWorld);
        }

        /// <summary>
        /// Two full vertex copies per lock (front + back) rather than one set of vertices wound both
        /// ways: <see cref="Mesh.RecalculateNormals"/> averages the face normals touching each SHARED
        /// vertex, and a vertex touched by a forward-wound triangle and a reverse-wound one sharing the
        /// same position averages to a near-zero normal — a black ribbon, not a double-sided one.
        /// Separate vertices at the same position each get their own, correctly opposite, normal.
        /// This is Max's own outline material (<c>StylizedCharacter.shader</c>'s main pass is hard
        /// <c>Cull Back</c>, not a togglable property), and a ribbon this thin is routinely seen edge-on.
        /// </summary>
        private static int[] BuildIndices(int lockCount)
        {
            const int quads = MaxHair.NodeCount - 1;
            var idx = new int[lockCount * quads * 12];
            int o = 0;
            for (int l = 0; l < lockCount; l++)
            {
                int frontBase = l * VertsPerLock;
                int backBase = frontBase + MaxHair.NodeCount * 2;
                for (int q = 0; q < quads; q++)
                {
                    int pf = frontBase + q * 2;
                    idx[o++] = pf; idx[o++] = pf + 1; idx[o++] = pf + 2;
                    idx[o++] = pf + 1; idx[o++] = pf + 3; idx[o++] = pf + 2;

                    int pb = backBase + q * 2;
                    idx[o++] = pb; idx[o++] = pb + 2; idx[o++] = pb + 1;
                    idx[o++] = pb + 1; idx[o++] = pb + 2; idx[o++] = pb + 3;
                }
            }
            return idx;
        }

        /// <summary>Advances every lock and re-uploads the merged mesh. The caller skips this entirely
        /// while paused (dt = 0), like the rest of the rig — see <see cref="MaxRig.LateUpdate"/>.</summary>
        public void Tick(float dt, Vector3 facingFlatWorld, float speed01, float stridePhase, float time,
                         float windStrength, Vector3 windDirWorld)
        {
            Vector3 pushWorld = MaxHair.ComputePushWorld(facingFlatWorld, windDirWorld, windStrength, time,
                                                          stridePhase, speed01);
            Vector3 pl = _head.InverseTransformDirection(pushWorld);

            for (int i = 0; i < _specs.Length; i++)
            {
                MaxHair.Tick(_specs[i], _states[i], dt, speed01, pl, time, windStrength, _nodes);
                WriteRibbon(i);
            }

            _mesh.SetVertices(_vertices);
            _mesh.RecalculateNormals();
            _mesh.RecalculateBounds();
        }

        private void WriteRibbon(int lockIndex)
        {
            var spec = _specs[lockIndex];
            var dirs = _states[lockIndex].SegDir;
            int frontBase = lockIndex * VertsPerLock;
            int backBase = frontBase + MaxHair.NodeCount * 2;
            const int n = MaxHair.SegCount;

            for (int k = 0; k < MaxHair.NodeCount; k++)
            {
                Vector3 p = _nodes[k];
                Vector3 dir = dirs[Mathf.Min(k, n - 1)];
                Vector3 radial = (p - MaxHair.HeadCenterLocal).normalized;
                Vector3 side = Vector3.Cross(dir, radial).normalized;

                float t = (float)k / n;
                float hw = spec.RootWidth * (1f - 0.65f * Mathf.Pow(t, 1.8f)) * 0.5f;

                Vector3 low = p - side * hw, high = p + side * hw;
                int v = frontBase + k * 2;
                _vertices[v] = low; _vertices[v + 1] = high;
                _vertices[backBase + k * 2] = low; _vertices[backBase + k * 2 + 1] = high;
            }
        }

        /// <summary>Runtime-only — never touch the mesh again once this has run.</summary>
        public void DestroyResources()
        {
            if (_mesh != null) Object.Destroy(_mesh);
        }
    }
}
