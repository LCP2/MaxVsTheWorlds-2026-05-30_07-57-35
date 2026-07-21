using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Rendering;

namespace MaxWorlds.Pickups
{
    /// <summary>
    /// A walk-over collectible dropped by a robot (YT-131) — a power cell or a part. Greybox stand-in:
    /// a small hovering, spinning shape (a sphere for a cell, a cube for a part) so it reads on the
    /// lawn at the ~72° camera. It carries no collection logic itself; the <see cref="PickupDirector"/>
    /// pools it and does the walk-over check, so there is one Max lookup and one pool, not one per drop.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Pickup : MonoBehaviour
    {
        private static readonly Color CellColor = new Color(0.31f, 0.86f, 0.98f); // cyan power cell
        private static readonly Color PartColor = new Color(0.98f, 0.72f, 0.22f); // gold part

        /// <summary>How high the collectible hovers over the ground.</summary>
        private const float FloatHeight = 0.6f;

        public PickupKind Kind { get; private set; }

        private Transform _spin;
        private float _baseY = FloatHeight;

        /// <summary>Build a pooled pickup of the given kind (its visual never changes, so the director
        /// pools per kind and reuses it as-is).</summary>
        public static Pickup Create(PickupKind kind)
        {
            var go = new GameObject($"Pickup ({kind})");
            var p = go.AddComponent<Pickup>();
            p.Kind = kind;
            p.BuildVisual();
            return p;
        }

        private void BuildVisual()
        {
            // KeepsOwnMaterial on the root so the surface sweep leaves the tinted pickup alone (it is
            // not scenery). The child renderer gets a real URP material via MaterialLibrary — a raw
            // runtime primitive would draw magenta in a player build.
            gameObject.AddComponent<KeepsOwnMaterial>();

            bool cell = Kind == PickupKind.PowerCell;
            var prim = GameObject.CreatePrimitive(cell ? PrimitiveType.Sphere : PrimitiveType.Cube);
            prim.name = "Visual";
            Destroy(prim.GetComponent<Collider>());   // walk-over is a distance check, not physics
            prim.transform.SetParent(transform, worldPositionStays: false);
            prim.transform.localScale = Vector3.one * (cell ? 0.32f : 0.5f);
            prim.transform.localPosition = Vector3.zero;

            var mr = prim.GetComponent<MeshRenderer>();
            mr.sharedMaterial = MaterialLibrary.Tinted(SurfaceKind.Metal, cell ? CellColor : PartColor);
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _spin = prim.transform;
        }

        /// <summary>Drop the pickup at a ground position and switch it on (the director calls this).</summary>
        public void Place(Vector3 groundPos)
        {
            transform.position = new Vector3(groundPos.x, _baseY, groundPos.z);
            gameObject.SetActive(true);
        }

        private void Update()
        {
            // A slow spin + gentle bob is the whole reason a small greybox reads as "pick me up" from
            // above rather than as a bit of dropped debris.
            if (_spin != null) _spin.Rotate(0f, 140f * Time.deltaTime, 0f, Space.Self);
            Vector3 pos = transform.position;
            pos.y = _baseY + Mathf.Sin(Time.unscaledTime * 3f) * 0.12f;
            transform.position = pos;
        }
    }
}
