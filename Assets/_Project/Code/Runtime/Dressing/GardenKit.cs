using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Rendering;
using MaxWorlds.VFX;

namespace MaxWorlds.Dressing
{
    /// <summary>
    /// Turns a <see cref="DressProp"/> into an object in the yard (YT-75).
    ///
    /// Models come from a free CC0 low-poly garden kit, loaded by key out of Resources — the same
    /// load-by-key rule the generated-model pipeline uses (docs/CODE_DRIVEN_SCENES.md §4), so a
    /// fresh clone and the WebGL build assemble the identical yard with nothing hand-placed.
    ///
    /// Two things are worth knowing:
    ///
    ///  * WE DO NOT TRUST THE KIT'S SCALE. A kit is authored in whatever units its author liked, and
    ///    the FBX importer has opinions of its own on top. So a prop is authored as the WORLD SIZE
    ///    it should end up (a 3.4 m fence, a 6 m tree) and fitted to it here, measured off the mesh
    ///    bounds. Nothing in the plan is a magic multiplier that breaks the day the kit is swapped.
    ///
    ///  * DRESSING NEVER COLLIDES. Every collider is stripped on the way in. That is what makes
    ///    "props don't obstruct movement" a property of the code rather than a thing to re-check by
    ///    hand each time a prop moves — and it's why this stream can fill the arena without touching
    ///    the gameplay stream's movement, spawns or pathing.
    /// </summary>
    public static class GardenKit
    {
        /// <summary>Resources sub-folder the kit's models live in.</summary>
        public const string ResourceRoot = "GardenKit";

        private static readonly HashSet<string> s_missing = new HashSet<string>();

        public static GameObject Prefab(string model)
        {
            if (string.IsNullOrEmpty(model)) return null;
            return Resources.Load<GameObject>($"{ResourceRoot}/{model}");
        }

        /// <summary>Build a prop and put it in the world. Null if its model isn't in the kit — a
        /// missing prop is a thinner yard, never a broken scene.</summary>
        public static GameObject Spawn(in DressProp p, Transform parent)
        {
            GameObject go = p.Model == null ? Timber(p) : FromKit(p);
            if (go == null) return null;

            go.transform.SetParent(parent, worldPositionStays: false);
            Fit(go, p);
            Dress(go, p);
            return go;
        }

        /// <summary>The shed's structural timber: a plain box wearing a kit material. The kit has no
        /// shed in it, and a pitched roof is two boxes — not a reason to go modelling.</summary>
        private static GameObject Timber(in DressProp p)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = $"Timber ({p.Paint})";

            var col = go.GetComponent<Collider>();
            if (col != null) Object.Destroy(col);

            var r = go.GetComponent<MeshRenderer>();
            var mat = KitMaterials.For(p.Paint);
            if (mat != null) r.sharedMaterial = mat;

            return go;
        }

        private static GameObject FromKit(in DressProp p)
        {
            var prefab = Prefab(p.Model);
            if (prefab == null)
            {
                if (s_missing.Add(p.Model))
                    Debug.LogWarning($"[GardenKit] no model '{p.Model}' in Resources/{ResourceRoot} — skipped.");
                return null;
            }

            var go = Object.Instantiate(prefab);
            go.name = p.Model;
            return go;
        }

        /// <summary>
        /// Scale and place the prop so it ends up the size the plan asked for, standing on the
        /// ground at the point the plan asked for — whatever pivot, orientation or unit scale the
        /// kit's author happened to use.
        ///
        /// An axis left at 0 in the plan keeps the model's own proportions; the axes that are given
        /// are matched exactly. That's what lets a fence run be tiled to fill a wall to the
        /// millimetre while a tree stays a tree.
        /// </summary>
        public static void Fit(GameObject go, in DressProp p)
        {
            Transform t = go.transform;
            t.localPosition = Vector3.zero;
            t.localRotation = Quaternion.identity;
            t.localScale = Vector3.one;

            Bounds b = LocalBounds(go);
            Vector3 size = b.size;

            var s = new Vector3(
                size.x > 1e-4f && p.Size.x > 0f ? p.Size.x / size.x : 0f,
                size.y > 1e-4f && p.Size.y > 0f ? p.Size.y / size.y : 0f,
                size.z > 1e-4f && p.Size.z > 0f ? p.Size.z / size.z : 0f);

            float given = Mathf.Max(s.x, Mathf.Max(s.y, s.z));
            if (given <= 0f) given = 1f;
            if (s.x <= 0f) s.x = given;
            if (s.y <= 0f) s.y = given;
            if (s.z <= 0f) s.z = given;

            t.localRotation = Quaternion.Euler(p.Euler);
            t.localScale = s;

            // The anchor: the plan's Y is where the prop TOUCHES THE GROUND, not where its pivot is.
            // (The pitched roof asks for its centre instead — it doesn't touch anything.)
            var anchor = new Vector3(
                b.center.x,
                p.AnchorCentre ? b.center.y : b.min.y,
                b.center.z);

            t.localPosition = p.Position - (t.localRotation * Vector3.Scale(s, anchor));
        }

        /// <summary>The prop's bounds in its own local space, from the meshes rather than from the
        /// renderers — Renderer.bounds is a world AABB and would fold in the rotation we're about to
        /// apply. Works on non-readable meshes: mesh.bounds is serialised, not computed.</summary>
        public static Bounds LocalBounds(GameObject go)
        {
            Matrix4x4 toLocal = go.transform.worldToLocalMatrix;
            bool any = false;
            var b = new Bounds();

            foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
            {
                var mesh = mf.sharedMesh;
                if (mesh == null) continue;

                Matrix4x4 m = toLocal * mf.transform.localToWorldMatrix;
                Bounds mb = mesh.bounds;

                for (int corner = 0; corner < 8; corner++)
                {
                    var p = new Vector3(
                        (corner & 1) == 0 ? mb.min.x : mb.max.x,
                        (corner & 2) == 0 ? mb.min.y : mb.max.y,
                        (corner & 4) == 0 ? mb.min.z : mb.max.z);
                    Vector3 world = m.MultiplyPoint3x4(p);

                    if (!any) { b = new Bounds(world, Vector3.zero); any = true; }
                    else b.Encapsulate(world);
                }
            }

            return any ? b : new Bounds(Vector3.zero, Vector3.one);
        }

        /// <summary>Strip the colliders, repaint the kit's materials in our palette, and tell the
        /// material directors this one is already handled.</summary>
        private static void Dress(GameObject go, in DressProp p)
        {
            foreach (var col in go.GetComponentsInChildren<Collider>())
                Object.Destroy(col);

            foreach (var r in go.GetComponentsInChildren<MeshRenderer>())
            {
                if (p.Model != null) Repaint(r);

                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                if (r.GetComponent<SurfaceSkinned>() == null)
                    r.gameObject.AddComponent<SurfaceSkinned>();
            }

            var skin = go.AddComponent<DressingSkin>();
            skin.Sways = p.Sways;
        }

        /// <summary>The kit's material NAMES survive the import; its colours do not. See
        /// <see cref="KitMaterials"/> for why.</summary>
        private static void Repaint(MeshRenderer r)
        {
            var source = r.sharedMaterials;
            if (source == null || source.Length == 0) return;

            var painted = new Material[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                string name = source[i] != null ? source[i].name : null;
                painted[i] = KitMaterials.For(name) ?? source[i];
            }
            r.sharedMaterials = painted;
        }
    }
}
