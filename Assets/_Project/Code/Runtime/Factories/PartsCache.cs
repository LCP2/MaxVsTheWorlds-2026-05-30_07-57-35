using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Arena;
using MaxWorlds.Core;
using MaxWorlds.Enemies;
using MaxWorlds.Pickups;
using MaxWorlds.Rendering;
using MaxWorlds.UI;
using MaxWorlds.VFX;

namespace MaxWorlds.Factories
{
    /// <summary>
    /// A Parts Cache (MV-1478; Lee, 27 Nov: "after World 2, Parts should have locations ... almost like a chest or some sort of
    /// electronic, stationary, blinking artifact ... something you shouldn't be able to access while you're fighting. It should be
    /// a reward for completing a fight."). One per area in a world with <see cref="WorldDefinition.UsesPartsCaches"/>; robots in
    /// that world drop no Parts, the area's Parts sit in its cache.
    ///
    /// STATES: SEALED while any robot belonging to the area is alive (its garrison, queued robots, anything a shed/relay/cradle
    /// spawned there, elites - all carry <see cref="RobotEnemy.AreaIndex"/>; a robot wandering in from another area does not
    /// count) or a producer in the area is alive. Shots are absorbed. When the last owned robot is gone and no producer is alive
    /// it UNSEALS (the shield shatters over <see cref="ShatterSeconds"/>); it never re-seals. Any player weapon then wears down its
    /// HP; a gold lock pops off at 2/3, 1/3 and 0 HP, and at 0 HP it BURSTS: the area's Parts
    /// (<see cref="WorldEconomyModel.CachePartsForArea"/>) spill out and fly to Max. The opened shell stays as an inert prop.
    ///
    /// Static and solid (the built cube is the collider), robots never target it and enemy fire cannot hurt it
    /// (<see cref="Team.Enemy"/> + <see cref="DamageRules.Applies"/>, same as a <see cref="Pylon"/>).
    /// </summary>
    [MaxWorlds.Core.PerfSection("factories")]
    public sealed class PartsCache : MonoBehaviour, IDamageable
    {
        public enum CacheState { Sealed, Unsealed, Opened }

        public const float ShatterSeconds = 0.4f;     // ticket: "the shield shatters over 0.4 s"
        public const int LockCount = 3;               // ticket: "three gold lock clamps"
        public const int MaxPips = 8;                 // ticket: "up to 8 pips"
        public const float ColumnHeight = 8f;         // ticket: "a cyan light column rises ~8 m"
        public const float CensusIntervalSeconds = 0.2f;

        private const float Radius = WorldMapLoader.PartsCacheFootprint * 0.5f;
        private const float Height = WorldMapLoader.PartsCacheHeight;
        // MV-1487: label cap height ~ one lock clamp, never wider than 2.2x the dome; the dome is a hemisphere on the ring.
        private const float LabelWorldHeight = 0.28f;
        private const float LabelGap = 0.2f;
        private const float LabelMaxWidthOverDome = 2.2f;
        private const float RingRadiusScale = 1.75f;
        private const float DomeHeightOverCache = 1.3f;
        private const float LabelCharacterSize = 0.06f;
        private const int LabelFontSize = 100;
        private const float LabelPitchDegrees = 72f;
        private const float LockPopSeconds = 0.35f;
        private const float BurstFlashSeconds = 0.35f;
        private const float LidFlySeconds = 0.5f;
        private const int HealthRingSegments = 24;

        private static readonly Color GunmetalColor = new Color(0.20f, 0.22f, 0.27f);
        private static readonly Color LidColor = new Color(0.27f, 0.29f, 0.35f);
        private static readonly Color DeadColor = new Color(0.10f, 0.11f, 0.13f);
        private static readonly Color RedColor = new Color(1.00f, 0.22f, 0.26f);
        private static readonly Color CyanColor = new Color(0.20f, 0.90f, 1.00f);
        private static readonly Color GoldColor = new Color(1.00f, 0.78f, 0.22f);
        private static readonly Color PipDeadColor = new Color(0.25f, 0.27f, 0.32f);

        public string Id { get; private set; } = string.Empty;

        /// <summary>The 1-based area this cache belongs to; 0 until <see cref="Configure"/>.</summary>
        public int AreaIndex { get; private set; }

        /// <summary>The 0-based world this cache is in (reads its HP and its Parts amount).</summary>
        public int WorldIndex { get; private set; }

        public CacheState State { get; private set; } = CacheState.Sealed;

        /// <summary>The Parts this cache bursts - the economy model's figure for its area.</summary>
        public int PartsAmount { get; private set; }

        /// <summary>Resolved readouts, never authored constants (Tier 2).</summary>
        public int RobotsLeft { get; private set; }
        public bool ProducerAlive { get; private set; }
        public int LocksLeft { get; private set; } = LockCount;
        public float Health => _hp;
        public float MaxHealth => _maxHp;
        public string LabelText => _label != null ? _label.text : string.Empty;
        /// <summary>Resolved world-space size (metres) of the label's rendered glyphs (MV-1487; Tier 2).</summary>
        public Vector2 LabelWorldSize
        {
            get
            {
                if (_label == null) return Vector2.zero;
                _label.ForceMeshUpdate();
                Vector3 ls = _label.transform.lossyScale;
                return new Vector2(_label.renderedWidth * ls.x, _label.renderedHeight * ls.y);
            }
        }
        public Renderer ShieldRenderer => _shield != null ? _shield.GetComponent<Renderer>() : null;
        public Transform DashedRing => _dashedRing;
        private string _fittedText;
        public int BurstPickupCount { get; private set; }

        // MV-1484: a SEALED cache is a live, solid shot-stopper too (it absorbs with a spark; TakeDamage does the sealed/
        // unsealed split) - when this was Unsealed-only every weapon's `!IsAlive -> skip` filter let shots fly straight through.
        public bool IsAlive => State != CacheState.Opened;
        public Team Team => Team.Enemy; // player weapons hurt it; robots and their fire cannot

        private float _hp;
        private float _maxHp = 1f;
        private float _censusTimer;
        private bool _populated;
        private float _shatterT = -1f;
        private float _flashT = -1f;
        private float _lidT = -1f;
        private readonly float[] _lockPopT = { -1f, -1f, -1f };

        private Collider _collider;
        private Transform _visual;
        private Transform _lid;
        private Transform _shield;
        private Transform _column;
        private Transform _flash;
        private Transform _dashedRing;
        private Transform _solidRing;
        private Transform _healthRing;
        private TMPro.TextMeshPro _label;
        private readonly List<Renderer> _slitRenderers = new List<Renderer>();
        private readonly List<Renderer> _bodyRenderers = new List<Renderer>();
        private Renderer _coreRenderer;
        private readonly List<Transform> _locks = new List<Transform>();
        private readonly List<Vector3> _lockHome = new List<Vector3>();
        private readonly List<Renderer> _pips = new List<Renderer>();
        private readonly List<Renderer> _ringSegments = new List<Renderer>();
        private Vector3 _lidHome;
        private AreaAccumulationDirector _director;

        // --- Static registry, same shape as Vault/Pylon ---
        private static readonly List<PartsCache> Registered = new List<PartsCache>(8);

        public static void ResetRegistry() => Registered.Clear();

        /// <summary>Every registered cache (read-only snapshot, for the map marker and tests).</summary>
        public static IReadOnlyList<PartsCache> All => Registered;

        /// <summary>Stable ids of every cache already OPENED this run (checkpoint capture).</summary>
        public static string[] OpenedIds() => IdsInState(CacheState.Opened);

        /// <summary>Stable ids of every cache already UNSEALED but not yet opened (a cache never re-seals, so a resume must
        /// not seal it again just because the area's robots were rebuilt).</summary>
        public static string[] UnsealedIds() => IdsInState(CacheState.Unsealed);

        private static string[] IdsInState(CacheState s)
        {
            var ids = new List<string>();
            foreach (PartsCache c in Registered)
                if (c != null && c.State == s && !string.IsNullOrEmpty(c.Id)) ids.Add(c.Id);
            return ids.ToArray();
        }

        /// <summary>Re-applies a checkpoint's recorded states onto the rebuilt caches (RESUME). An OPENED cache comes back as an
        /// inert shell and pays nothing; an UNSEALED one comes back unsealed with full HP.</summary>
        public static void ApplyCheckpointStates(IReadOnlyList<string> openedIds, IReadOnlyList<string> unsealedIds)
        {
            foreach (PartsCache c in Registered)
            {
                if (c == null || string.IsNullOrEmpty(c.Id)) continue;
                if (Contains(openedIds, c.Id)) c.ApplyCheckpointOpened();
                else if (Contains(unsealedIds, c.Id)) c.ApplyCheckpointUnsealed();
            }
        }

        private static bool Contains(IReadOnlyList<string> ids, string id)
        {
            if (ids == null) return false;
            for (int i = 0; i < ids.Count; i++) if (ids[i] == id) return true;
            return false;
        }

        /// <summary>Post-AddComponent configure, same convention as <see cref="Vault.Configure"/>.</summary>
        public void Configure(string id, int worldIndex, int areaIndex)
        {
            Id = id ?? string.Empty;
            WorldIndex = worldIndex;
            AreaIndex = areaIndex;
            PartsAmount = WorldEconomyModel.CachePartsForArea(worldIndex, areaIndex);
            _maxHp = Mathf.Max(1f, WorldCatalog.Get(worldIndex).PartsCacheHealth);
            _hp = _maxHp;
        }

        private void Awake() => Build();

        public void Build()
        {
            if (_visual != null) return;
            if (_hp <= 0f) { _maxHp = Mathf.Max(1f, _maxHp); _hp = _maxHp; }

            _collider = GetComponent<Collider>();
            // MV-1484: solid to shots like cover (MV-1478 change 3). The Cover layer is what every weapon's sight/occlusion test
            // and the robots' own fire stop on, so nothing passes through to what stands behind it. Side effect, stated: a robot's
            // line of sight is now blocked by the cache like any other cover piece.
            CoverLayer.Assign(gameObject);
            var rootRend = GetComponent<Renderer>();
            if (rootRend != null)
            {
                rootRend.enabled = false;   // the built cube is only the collider; the pod is dressed below
                MapStaticBatchRoot.Active?.MarkPermanentlyHidden(rootRend);
            }

            _visual = ParentScale.MakeMetreSpace(new GameObject("Body").transform, transform);
            BuildPod();
            BuildLabelAndPips();
            ApplyStateVisuals();
            if (!Registered.Contains(this)) Registered.Add(this);
        }

        private void OnDestroy() => Registered.Remove(this);

        // ---------------------------------------------------------------- build

        private void BuildPod()
        {
            float baseY = -Height * 0.5f;
            float bodyH = Height - 0.18f;

            // Hexagonal armoured body (flat-topped hex prism, ~1.6 m across).
            MakeHex("Pod", _visual, new Vector3(0f, baseY + bodyH * 0.5f, 0f), Radius, bodyH, GunmetalColor, _bodyRenderers);

            // Three vertical glowing core slits, on alternate faces.
            for (int i = 0; i < 3; i++)
            {
                float a = (i * 120f + 30f) * Mathf.Deg2Rad;
                Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                float faceDist = Radius * Mathf.Cos(30f * Mathf.Deg2Rad) + 0.015f;
                var slit = Prim(PrimitiveType.Cube, "Slit" + i, _visual, dir * faceDist + new Vector3(0f, baseY + bodyH * 0.5f, 0f),
                    new Vector3(0.11f, bodyH * 0.62f, 0.03f), Quaternion.LookRotation(dir, Vector3.up), RedColor, additive: false);
                _slitRenderers.Add(slit);
            }

            // Hex lid with a hex core.
            _lid = new GameObject("Lid").transform;
            _lid.SetParent(_visual, false);
            _lidHome = new Vector3(0f, baseY + bodyH + 0.045f, 0f);
            _lid.localPosition = _lidHome;
            MakeHex("LidPlate", _lid, Vector3.zero, Radius * 0.98f, 0.09f, LidColor, _bodyRenderers);
            MakeHex("CoreHex", _lid, new Vector3(0f, 0.06f, 0f), Radius * 0.36f, 0.05f, RedColor, null, additive: false, rendererOut: out _coreRenderer);

            // Three gold lock clamps over the lid rim at 90, 210 and 330 degrees.
            float[] lockAngles = { 90f, 210f, 330f };
            for (int i = 0; i < LockCount; i++)
            {
                float a = lockAngles[i] * Mathf.Deg2Rad;
                Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Vector3 home = dir * (Radius * 0.78f) + new Vector3(0f, 0.11f, 0f);
                var lk = Prim(PrimitiveType.Cube, "Lock" + i, _lid, home, new Vector3(0.30f, 0.20f, 0.34f),
                    Quaternion.LookRotation(dir, Vector3.up), GoldColor, additive: false);
                _locks.Add(lk.transform);
                _lockHome.Add(home);
            }

            // Shield dome (MV-1487): a HEMISPHERE whose base circle sits on the floor at exactly the dashed ring's radius and
            // centre, so the dashed ring is the dome's rim. Mesh is unit radius, y >= 0.
            var dome = Prim(PrimitiveType.Cube, "Shield", _visual, new Vector3(0f, baseY, 0f),
                DomeScale, Quaternion.identity, RedColor, additive: false);
            dome.GetComponent<MeshFilter>().sharedMesh = HemisphereMesh();
            dome.sharedMaterial = ShieldMaterial();
            dome.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            dome.receiveShadows = false;
            _shield = dome.transform;

            // Dashed red floor ring (sealed) and solid cyan ring (unsealed).
            _dashedRing = BuildRing("DashedRing", baseY + 0.03f, Radius * RingRadiusScale, 14, 0.5f, RedColor, additive: false);
            _solidRing = BuildRing("SolidRing", baseY + 0.03f, Radius * RingRadiusScale, 40, 1.05f, CyanColor);
            _healthRing = BuildRing("HealthRing", baseY + 0.05f, Radius * 1.45f, HealthRingSegments, 0.9f, CyanColor, _ringSegments);

            // Light column.
            var col = Prim(PrimitiveType.Cylinder, "Column", _visual, new Vector3(0f, baseY + ColumnHeight * 0.5f, 0f),
                new Vector3(0.5f, ColumnHeight * 0.5f, 0.5f), Quaternion.identity, new Color(0.08f, 0.45f, 0.55f), additive: true);
            _column = col.transform;

            // Burst flash.
            var fl = Prim(PrimitiveType.Sphere, "Flash", _visual, new Vector3(0f, baseY + Height, 0f), Vector3.one * 0.3f,
                Quaternion.identity, new Color(1f, 0.85f, 0.45f), additive: true);
            _flash = fl.transform;
        }

        private static Vector3 DomeScale => new Vector3(Radius * RingRadiusScale, Height * DomeHeightOverCache, Radius * RingRadiusScale);

        private static Mesh _hemisphere;

        /// <summary>Unit-radius upper hemisphere (y from 0 to 1), smooth normals.</summary>
        private static Mesh HemisphereMesh()
        {
            if (_hemisphere != null) return _hemisphere;
            const int lon = 32, lat = 12;
            var verts = new List<Vector3>();
            var tris = new List<int>();
            for (int j = 0; j <= lat; j++)
            {
                float phi = j * Mathf.PI * 0.5f / lat;   // 0 = pole, 90 deg = rim on the floor
                for (int i = 0; i <= lon; i++)
                {
                    float th = i * 2f * Mathf.PI / lon;
                    verts.Add(new Vector3(Mathf.Sin(phi) * Mathf.Cos(th), Mathf.Cos(phi), Mathf.Sin(phi) * Mathf.Sin(th)));
                }
            }
            for (int j = 0; j < lat; j++)
                for (int i = 0; i < lon; i++)
                {
                    int a = j * (lon + 1) + i, b = a + lon + 1;
                    tris.AddRange(new[] { a, a + 1, b, a + 1, b + 1, b });
                }
            _hemisphere = new Mesh { name = "PartsCacheHemisphere", hideFlags = HideFlags.HideAndDontSave };
            _hemisphere.SetVertices(verts);
            _hemisphere.SetNormals(verts);
            _hemisphere.SetTriangles(tris, 0);
            _hemisphere.RecalculateBounds();
            return _hemisphere;
        }

        /// <summary>The same Fresnel-rim force-field shader <see cref="RelayDome"/> uses (a translucent red shell with a bright rim),
        /// with the same flat translucent fallback where it is unsupported.</summary>
        private static Material ShieldMaterial()
        {
            var shader = Shader.Find("MaxWorlds/ForceFieldShield");
            if (shader == null || !shader.isSupported) return VfxMaterials.AlphaBlendTinted(VfxMaterials.Solid(), new Color(1f, 0.15f, 0.15f, 0.22f));
            var m = new Material(shader) { name = "PartsCacheShield", hideFlags = HideFlags.HideAndDontSave };
            m.SetColor("_BaseColor", new Color(0.95f, 0.12f, 0.08f, 0.22f));
            m.SetColor("_RimColor", new Color(1f, 0.35f, 0.22f, 1f));
            return m;
        }

        private Transform BuildRing(string ringName, float y, float radius, int segments, float fill, Color color,
            List<Renderer> collect = null, bool additive = true)
        {
            var root = new GameObject(ringName).transform;
            root.SetParent(_visual, false);
            float seg = 2f * Mathf.PI * radius / segments;
            for (int i = 0; i < segments; i++)
            {
                float a = i * (2f * Mathf.PI / segments);
                Vector3 pos = new Vector3(Mathf.Cos(a) * radius, y, Mathf.Sin(a) * radius);
                Vector3 tangent = new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a));
                var r = Prim(PrimitiveType.Cube, ringName + i, root, pos, new Vector3(seg * fill, 0.03f, 0.11f),
                    Quaternion.LookRotation(tangent, Vector3.up) * Quaternion.Euler(0f, 90f, 0f), color, additive);
                collect?.Add(r);
            }
            return root;
        }

        private Renderer Prim(PrimitiveType type, string partName, Transform parent, Vector3 localPos, Vector3 localScale,
            Quaternion localRot, Color color, bool additive)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = partName;
            var c = go.GetComponent<Collider>();
            if (c != null) Object.DestroyImmediate(c);   // cosmetic only: the root cube is the real collider
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot;
            go.transform.localScale = localScale;
            go.AddComponent<SelfDrivenTint>();   // the skin director re-skins every renderer under a damageable body (it turned these teal)
            var rend = go.GetComponent<MeshRenderer>();
            Paint(rend, color, additive);
            return rend;
        }

        private static void Paint(Renderer r, Color color, bool additive)
        {
            if (r == null) return;
            Material m = additive ? VfxMaterials.AdditiveTinted(color) : FlatColorMaterial.Get(color);
            if (m != null) r.sharedMaterial = m;
        }

        private void MakeHex(string partName, Transform parent, Vector3 localPos, float radius, float height, Color color,
            List<Renderer> collect, bool additive = false)
            => MakeHex(partName, parent, localPos, radius, height, color, collect, additive, out _);

        private void MakeHex(string partName, Transform parent, Vector3 localPos, float radius, float height, Color color,
            List<Renderer> collect, bool additive, out Renderer rendererOut)
        {
            var go = new GameObject(partName);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.AddComponent<MeshFilter>().sharedMesh = HexPrismMesh(radius, height);
            go.AddComponent<SelfDrivenTint>();   // see Prim
            var rend = go.AddComponent<MeshRenderer>();
            Paint(rend, color, additive);
            collect?.Add(rend);
            rendererOut = rend;
        }

        /// <summary>A flat-shaded hexagonal prism, centred on its own origin (flat top, vertex pointing along +X).</summary>
        public static Mesh HexPrismMesh(float radius, float height)
        {
            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var tris = new List<int>();
            float h = height * 0.5f;
            for (int i = 0; i < 6; i++)
            {
                float a0 = i * Mathf.PI / 3f, a1 = (i + 1) * Mathf.PI / 3f;
                Vector3 p0 = new Vector3(Mathf.Cos(a0) * radius, 0f, Mathf.Sin(a0) * radius);
                Vector3 p1 = new Vector3(Mathf.Cos(a1) * radius, 0f, Mathf.Sin(a1) * radius);
                Vector3 n = new Vector3(Mathf.Cos((a0 + a1) * 0.5f), 0f, Mathf.Sin((a0 + a1) * 0.5f));
                int b = verts.Count;
                verts.Add(p0 + Vector3.down * h); verts.Add(p1 + Vector3.down * h);
                verts.Add(p1 + Vector3.up * h); verts.Add(p0 + Vector3.up * h);
                for (int k = 0; k < 4; k++) norms.Add(n);
                tris.AddRange(new[] { b, b + 3, b + 2, b, b + 2, b + 1 });
            }
            int top = verts.Count;
            verts.Add(Vector3.up * h); norms.Add(Vector3.up);
            for (int i = 0; i < 6; i++)
            {
                float a = i * Mathf.PI / 3f;
                verts.Add(new Vector3(Mathf.Cos(a) * radius, h, Mathf.Sin(a) * radius)); norms.Add(Vector3.up);
            }
            for (int i = 0; i < 6; i++) tris.AddRange(new[] { top, top + 1 + (i + 1) % 6, top + 1 + i });
            var mesh = new Mesh { name = "PartsCacheHex" };
            mesh.SetVertices(verts); mesh.SetNormals(norms); mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        private void BuildLabelAndPips()
        {
            var go = new GameObject("Label");
            go.transform.SetParent(_visual, false);   // metre space: the root is the 1.6 x 1.4 x 1.6 collider cube, which would stretch the text
            go.transform.localPosition = LabelLocalPos(LabelWorldHeight);
            go.transform.localRotation = Quaternion.Euler(LabelPitchDegrees, 0f, 0f);
            _label = CrispUi.WorldText(go, string.Empty, LabelFontSize, LabelCharacterSize, Color.white);

            for (int i = 0; i < MaxPips; i++)
            {
                float x = (i - (MaxPips - 1) * 0.5f) * 0.28f;
                _pips.Add(Prim(PrimitiveType.Sphere, "Pip" + i, _visual, new Vector3(x, LabelLocalPos(LabelWorldHeight).y - 0.3f, 0f),
                    Vector3.one * 0.2f, Quaternion.identity, RedColor, additive: false));
            }
        }

        private static Vector3 LabelLocalPos(float labelHeight)
            => new Vector3(0f, -Height * 0.5f + Height * DomeHeightOverCache + LabelGap + labelHeight * 0.5f, 0f);

        /// <summary>Shrinks the label to cap height ~<see cref="LabelWorldHeight"/>, then further if it would exceed 2.2x the dome's
        /// diameter; runs only when the text changes.</summary>
        private void FitLabel()
        {
            if (_label == null || _label.text == _fittedText) return;
            _fittedText = _label.text;
            _label.transform.localScale = Vector3.one;
            _label.ForceMeshUpdate();
            float h = Mathf.Max(0.0001f, _label.renderedHeight), w = Mathf.Max(0.0001f, _label.renderedWidth);
            float k = LabelWorldHeight / h;
            float maxW = LabelMaxWidthOverDome * 2f * Radius * RingRadiusScale;
            k = Mathf.Min(k, maxW / w);
            _label.transform.localScale = Vector3.one * k;
        }

        // ---------------------------------------------------------------- state

        /// <summary>Force the next <see cref="Tick"/> to re-count the area's robots (tests; the live game recounts every
        /// <see cref="CensusIntervalSeconds"/>).</summary>
        public void InvalidateCensus() => _censusTimer = 0f;

        private void Update() => Tick(Time.deltaTime);

        public void Tick(float dt)
        {
            if (_visual == null) return;

            if (State == CacheState.Sealed)
            {
                _censusTimer -= dt;
                if (_censusTimer <= 0f)
                {
                    _censusTimer = CensusIntervalSeconds;
                    // An area nobody has entered holds no robots yet: that is "not populated", not "cleared" - and it is not
                    // worth three scene-wide searches five times a second for each of the world's 30 caches.
                    _populated = AreaPopulated();
                    if (!_populated) { RobotsLeft = 0; ProducerAlive = false; UpdateLabel(); }
                    else
                    {
                        Census();
                        if (RobotsLeft == 0 && !ProducerAlive) Unseal(silent: false);
                        else UpdateLabel();
                    }
                }
            }

            TickAnimations(dt);
        }

        private bool AreaPopulated()
        {
            if (_director == null) _director = FindFirstObjectByType<AreaAccumulationDirector>();
            return _director == null || _director.IsAreaFilled(AreaIndex);   // no director (a test) = nothing to wait for
        }

        /// <summary>Counts what keeps this cache sealed: live robots stamped with this area, robots still queued for it, and live
        /// producers (Replicator / Assembly Cradle) in it. A robot from another area never counts.</summary>
        private void Census()
        {
            if (_director == null) _director = FindFirstObjectByType<AreaAccumulationDirector>();
            int robots = 0;
            foreach (RobotEnemy r in Object.FindObjectsByType<RobotEnemy>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (r != null && r.IsAlive && r.AreaIndex == AreaIndex) robots++;
            if (_director != null) robots += _director.QueuedForArea(AreaIndex);
            RobotsLeft = robots;

            bool producer = false;
            foreach (Replicator rep in Object.FindObjectsByType<Replicator>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (rep != null && rep.IsAlive && rep.AreaIndex == AreaIndex) { producer = true; break; }
            if (!producer)
                foreach (AssemblyCradle cr in Object.FindObjectsByType<AssemblyCradle>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    if (cr != null && cr.IsAlive && cr.AreaIndex == AreaIndex) { producer = true; break; }
            ProducerAlive = producer;
        }

        private void Unseal(bool silent)
        {
            State = CacheState.Unsealed;
            _hp = _maxHp;
            LocksLeft = LockCount;
            RobotsLeft = 0;
            if (!silent)
            {
                _shatterT = 0f;
                HudSignals.EmitPartsCacheUnsealed(transform.position);
            }
            ApplyStateVisuals();
        }

        private void ApplyCheckpointUnsealed()
        {
            if (State != CacheState.Sealed) return;
            Unseal(silent: true);
            _shatterT = -1f;
            if (_shield != null) _shield.gameObject.SetActive(false);
        }

        private void ApplyCheckpointOpened()
        {
            if (State == CacheState.Opened) return;
            State = CacheState.Opened;
            LocksLeft = 0;
            _hp = 0f;
            _lidT = LidFlySeconds;   // already at rest
            ApplyStateVisuals();
            if (_lid != null) _lid.localPosition = _lidHome + new Vector3(0.9f, -0.05f, 0.35f);
        }

        public void TakeDamage(in DamageInfo info)
        {
            if (State == CacheState.Opened) return;
            if (!DamageRules.Applies(info.Attacker, Team)) return;   // robots / their fire do nothing to it

            if (State == CacheState.Sealed)
            {
                // Absorbed: a spark burst and a clang, no damage, no number.
                HudSignals.EmitShieldDeflected(info.Point != Vector3.zero ? info.Point : transform.position, info.Direction);
                return;
            }

            if (info.Amount <= 0f) return;
            HudSignals.EmitDamage(transform.position + Vector3.up * 1.2f, info.Amount);
            _hp = Mathf.Max(0f, _hp - info.Amount);

            int shouldBeLeft = _hp <= 0f ? 0 : Mathf.CeilToInt(LockCount * (_hp / _maxHp) - 0.0001f);
            // Locks break at 2/3, 1/3 and 0: with 3 locks, "left" = ceil(3 * hp/max) (hp 66% -> 2 left once strictly below 2/3).
            shouldBeLeft = Mathf.Clamp(shouldBeLeft, 0, LockCount);
            while (LocksLeft > shouldBeLeft)
            {
                LocksLeft--;
                PopLock(LocksLeft);
            }

            if (_hp <= 0f) Burst();
            else { UpdateLabel(); UpdateHealthRing(); }
        }

        private void PopLock(int index)
        {
            if (index < 0 || index >= _locks.Count) return;
            _lockPopT[index] = 0f;
            HudSignals.EmitFittingDestroyed(_locks[index].position);   // spark burst, same cue a destroyed fitting uses
        }

        private void Burst()
        {
            State = CacheState.Opened;
            _flashT = 0f;
            _lidT = 0f;
            Vector3 at = transform.position;
            PickupDirector director = PickupDirector.EnsureInstalled();
            BurstPickupCount = director.SpawnCacheBurst(at, PartsAmount).Count;
            if (PartsAmount > 0) HudSignals.EmitPickup(at + Vector3.up * 1.4f, "+" + PartsAmount + " PARTS", GoldColor);
            HudSignals.EmitFittingDestroyed(at + Vector3.up * Height);
            ApplyStateVisuals();
        }

        // ---------------------------------------------------------------- visuals

        private void ApplyStateVisuals()
        {
            bool sealedNow = State == CacheState.Sealed;
            bool unsealed = State == CacheState.Unsealed;
            bool opened = State == CacheState.Opened;

            if (_shield != null) _shield.gameObject.SetActive(sealedNow || _shatterT >= 0f);
            if (_dashedRing != null) _dashedRing.gameObject.SetActive(sealedNow);
            if (_solidRing != null) _solidRing.gameObject.SetActive(unsealed);
            if (_healthRing != null) _healthRing.gameObject.SetActive(unsealed);
            if (_column != null) _column.gameObject.SetActive(unsealed);
            foreach (Renderer p in _pips) if (p != null) p.gameObject.SetActive(sealedNow);

            Color slit = sealedNow ? RedColor : CyanColor;
            foreach (Renderer r in _slitRenderers) Paint(r, opened ? DeadColor : slit, additive: unsealed);
            if (_coreRenderer != null) { Paint(_coreRenderer, sealedNow ? RedColor : CyanColor, additive: !sealedNow); _coreRenderer.enabled = !opened; }
            if (opened) foreach (Renderer r in _bodyRenderers) Paint(r, DeadColor, additive: false);

            for (int i = 0; i < _locks.Count; i++)
                if (_locks[i] != null) _locks[i].gameObject.SetActive(i < LocksLeft && !opened);

            UpdateLabel();
            UpdateHealthRing();
            if (_flash != null) _flash.gameObject.SetActive(_flashT >= 0f);
        }

        private void UpdateLabel()
        {
            if (_label == null) return;
            switch (State)
            {
                case CacheState.Sealed:
                    _label.gameObject.SetActive(true);
                    _label.color = new Color(1f, 0.45f, 0.5f);
                    _label.text = !_populated ? "SEALED"
                        : ProducerAlive ? "SEALED · DESTROY THE SOURCE"
                        : "SEALED · " + RobotsLeft + (RobotsLeft == 1 ? " ROBOT LEFT" : " ROBOTS LEFT");
                    for (int i = 0; i < _pips.Count; i++)
                        if (_pips[i] != null) Paint(_pips[i], i < RobotsLeft ? RedColor : PipDeadColor, additive: false);
                    break;
                case CacheState.Unsealed:
                    _label.gameObject.SetActive(true);
                    _label.color = new Color(0.45f, 0.95f, 1f);
                    _label.text = LocksLeft >= LockCount ? "UNSEALED · SHOOT TO OPEN"
                        : LocksLeft + (LocksLeft == 1 ? " LOCK LEFT" : " LOCKS LEFT");
                    break;
                default:
                    _label.gameObject.SetActive(_flashT >= 0f);   // "+n PARTS" flies up from the pickup toast; the label is gone
                    break;
            }
            FitLabel();
        }

        private void UpdateHealthRing()
        {
            if (_ringSegments.Count == 0) return;
            float frac = _maxHp > 0f ? _hp / _maxHp : 0f;
            int lit = Mathf.CeilToInt(frac * _ringSegments.Count);
            for (int i = 0; i < _ringSegments.Count; i++)
                if (_ringSegments[i] != null) _ringSegments[i].enabled = i < lit;
        }

        private void TickAnimations(float dt)
        {
            if (_shatterT >= 0f && _shield != null)
            {
                _shatterT += dt;
                float t = Mathf.Clamp01(_shatterT / ShatterSeconds);
                float grow = 1f + t * 0.35f;
                _shield.localScale = DomeScale * grow;
                if (t >= 1f) { _shatterT = -1f; _shield.gameObject.SetActive(false); }
            }

            for (int i = 0; i < _lockPopT.Length; i++)
            {
                if (_lockPopT[i] < 0f || i >= _locks.Count || _locks[i] == null) continue;
                _lockPopT[i] += dt;
                float t = Mathf.Clamp01(_lockPopT[i] / LockPopSeconds);
                Vector3 outDir = new Vector3(_lockHome[i].x, 0f, _lockHome[i].z).normalized;
                _locks[i].gameObject.SetActive(true);
                _locks[i].localPosition = _lockHome[i] + outDir * (t * 0.9f) + Vector3.up * (Mathf.Sin(t * Mathf.PI) * 0.7f);
                if (t >= 1f) { _lockPopT[i] = -1f; _locks[i].gameObject.SetActive(false); }
            }

            if (_flashT >= 0f && _flash != null)
            {
                _flashT += dt;
                float t = Mathf.Clamp01(_flashT / BurstFlashSeconds);
                _flash.localScale = Vector3.one * Mathf.Lerp(0.3f, 3.2f, t);
                if (t >= 1f) { _flashT = -1f; _flash.gameObject.SetActive(false); UpdateLabel(); }
                else _flash.gameObject.SetActive(true);
            }

            if (_lidT >= 0f && _lidT < LidFlySeconds && _lid != null)
            {
                _lidT += dt;
                float t = Mathf.Clamp01(_lidT / LidFlySeconds);
                _lid.localPosition = _lidHome + new Vector3(0.9f * t, Mathf.Sin(t * Mathf.PI) * 1.4f - 0.05f * t, 0.35f * t);
                _lid.localRotation = Quaternion.Euler(0f, 0f, 38f * t);
            }
        }
    }
}
