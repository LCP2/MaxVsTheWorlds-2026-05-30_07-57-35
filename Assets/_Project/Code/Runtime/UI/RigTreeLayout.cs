using System.Collections.Generic;
using UnityEngine;

namespace MaxWorlds.UI
{
    /// <summary>MV-1410: the pure layout behind THE RIG's one-tree-at-a-time panel. Takes a category's
    /// nodes and their <c>parent</c> links (never the board JSON's x/y) and returns, for each node, the
    /// centre of its square tap rect inside an area of <c>areaW x areaH</c> (origin top-left, y down):
    /// roots in the top row, one row per depth below, parents centred over their children, rows spaced
    /// evenly. No Unity scene, no UI - one function, so an EditMode test can drive it on the real
    /// <c>rig_board*.json</c> data.</summary>
    public static class RigTreeLayout
    {
        public readonly struct Placed
        {
            public readonly string Id;
            public readonly int Depth;
            public readonly bool IsRoot, HasChildren;
            /// <summary>Centre of the node's square rect, relative to the area's top-left, y down.</summary>
            public readonly float X, Y;
            /// <summary>Side of the node's square tap rect.</summary>
            public readonly float Size;

            public Placed(string id, int depth, bool isRoot, bool hasChildren, float x, float y, float size)
            { Id = id; Depth = depth; IsRoot = isRoot; HasChildren = hasChildren; X = x; Y = y; Size = size; }
        }

        /// <param name="nodes">(id, parent) pairs in board order. A node whose parent is null/empty or not in
        /// the list is a root (a FORGE fusion has no node parent, so every fusion is a root).</param>
        /// <param name="rootSize">Side of a root's rect.</param>
        /// <param name="childSize">Side of a non-root's rect.</param>
        /// <param name="hang">Room kept free below the last row for the label that hangs under a leaf.</param>
        /// <param name="maxGapY">Most clear space allowed between two rows (spacing shrinks first, never size).</param>
        /// <param name="maxPitchX">Widest horizontal slot a leaf may be given.</param>
        public static Placed[] Compute(IReadOnlyList<(string id, string parent)> nodes, float areaW, float areaH,
            float rootSize, float childSize, float hang, float maxGapY, float maxPitchX)
        {
            int n = nodes.Count;
            var result = new Placed[n];
            if (n == 0) return result;

            var index = new Dictionary<string, int>();
            for (int i = 0; i < n; i++) index[nodes[i].id] = i;

            var parentOf = new int[n];
            var children = new List<int>[n];
            for (int i = 0; i < n; i++) children[i] = new List<int>();
            var roots = new List<int>();
            for (int i = 0; i < n; i++)
            {
                string p = nodes[i].parent;
                if (!string.IsNullOrEmpty(p) && index.TryGetValue(p, out int pi) && pi != i)
                {
                    parentOf[i] = pi;
                    children[pi].Add(i);
                }
                else { parentOf[i] = -1; roots.Add(i); }
            }

            // depth + leaf-slot x (a parent sits over the middle of its children).
            var depth = new int[n];
            var slotX = new float[n];
            int nextSlot = 0, maxDepth = 0;
            void Walk(int i, int d)
            {
                depth[i] = d;
                if (d > maxDepth) maxDepth = d;
                if (children[i].Count == 0) { slotX[i] = nextSlot++; return; }
                foreach (int c in children[i]) Walk(c, d + 1);
                slotX[i] = (slotX[children[i][0]] + slotX[children[i][children[i].Count - 1]]) * 0.5f;
            }
            foreach (int r in roots) Walk(r, 0);

            float pitch = Mathf.Min(maxPitchX, areaW / Mathf.Max(1, nextSlot));
            float left = (areaW - pitch * nextSlot) * 0.5f;

            // rows: each as tall as its biggest member; spacing is whatever is left, capped.
            int rows = maxDepth + 1;
            var rowH = new float[rows];
            for (int i = 0; i < n; i++)
            {
                float size = parentOf[i] < 0 ? rootSize : childSize;
                if (size > rowH[depth[i]]) rowH[depth[i]] = size;
            }
            float sum = 0f;
            for (int d = 0; d < rows; d++) sum += rowH[d];
            float gap = rows > 1 ? Mathf.Clamp((areaH - hang - sum) / (rows - 1), 0f, maxGapY) : 0f;
            float used = sum + gap * (rows - 1);
            float top = Mathf.Max(0f, Mathf.Min((areaH - hang - used) * 0.25f, 24f));   // a little air above the first row

            var rowCentre = new float[rows];
            float y = top;
            for (int d = 0; d < rows; d++) { rowCentre[d] = y + rowH[d] * 0.5f; y += rowH[d] + gap; }

            for (int i = 0; i < n; i++)
            {
                bool isRoot = parentOf[i] < 0;
                result[i] = new Placed(nodes[i].id, depth[i], isRoot, children[i].Count > 0,
                    left + (slotX[i] + 0.5f) * pitch, rowCentre[depth[i]], isRoot ? rootSize : childSize);
            }
            return result;
        }
    }
}
