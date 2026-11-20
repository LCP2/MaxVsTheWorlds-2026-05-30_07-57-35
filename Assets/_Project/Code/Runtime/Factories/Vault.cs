using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Pickups;
using MaxWorlds.Rendering;
using MaxWorlds.UI;
using MaxWorlds.VFX;

namespace MaxWorlds.Factories
{
    /// <summary>
    /// A vault (MV-1321) — a 4-wall greybox enclosure with a gold door, opened by walking Max within
    /// <see cref="OpenRadius"/> of the door while he holds the VaultKey its own area's KEY elite dropped
    /// (<see cref="VaultKeyState"/>). Opening slides the door <see cref="DoorSlideSeconds"/> s, consumes
    /// the key, and places the vault's cache (2 Supercell + 6 PowerCell) at its centre — never at build,
    /// so a vault never telegraphs its contents through a wall. One key per vault; a key only opens the
    /// vault in the area it dropped in (<see cref="VaultKeyState"/>'s own id-bound holding, resolved from
    /// <see cref="NearestId"/> at drop time).
    ///
    /// The wall/door primitives are built directly by <see cref="MaxWorlds.Arena.MapRuntime.BuildVault"/>
    /// (same "MapRuntime builds the box, AddComponent wires behaviour" split <see cref="DestructibleCover"/>
    /// already uses) — this component owns state, the door's slide, the padlock icon and the cache drop.
    /// </summary>
    [MaxWorlds.Core.PerfSection("factories")]
    public sealed class Vault : MonoBehaviour
    {
        public enum VaultState { Closed, Open }

        public const float DoorWidth = 2.4f;     // ticket: "a 2.4 m gold door"
        public const float WallThickness = 0.8f; // ticket: "0.8 m thick"
        public const float WallHeight = 1.5f;    // ticket: "1.5 m tall (cover height, nobody hidden)"

        private const float OpenRadius = 1.5f;          // ticket: "comes within 1.5 m of its door"
        private const float DoorSlideSeconds = 0.5f;    // ticket: "slides open over 0.5 s"
        private const float IconHeight = 2f;            // ticket: "floats 2 m above the door"
        private const float IconDiameter = 0.8f;        // ">= 32 px" at 48 px/m (docs/DECISIONS.md), same constant RepairBay/ChallengePad use
        private const float OpenIconLingerSeconds = 2f; // MV-1403: the floating word reads OPEN for 2 s after the door opens, then goes
        private const float LabelHeight = 1.9f;         // MV-1403: floats just above the 1.5 m walls, centred over the vault
        private const float LabelCharacterSize = 0.14f; // with fontSize 100 -> capitals ~1 m tall = ~48 px at 48 px/m (>= the 32 px world-icon floor)
        private const int LabelFontSize = 100;
        private const float PadlockPitchDegrees = 72f;  // faces the fixed ~72 degree top-down camera
        private const int SupercellCount = 2;           // ticket: "2 Supercell"
        private const int PowerCellCount = 6;           // ticket: "6 PowerCell"
        private const float CacheScatterRadius = 0.9f;  // same ring spacing DestructibleCover's own secret cache uses

        private static readonly Color ClosedIconColor = new Color(0.90f, 0.70f, 0.18f); // gold padlock
        private static readonly Color OpenIconColor = new Color(1.00f, 0.92f, 0.55f);   // brighter gold flash for the 2s open window

        public string Id { get; private set; } = string.Empty;

        /// <summary>Post-build readout, never an authored constant — same "Tier 2" idiom as
        /// <see cref="RepairBay.State"/>.</summary>
        public VaultState State { get; private set; } = VaultState.Closed;

        private Transform _door;
        private Collider _doorCollider;
        private Vector3 _doorClosedLocal;
        private Vector3 _doorOpenLocal;
        private float _doorSlideT; // 0 (closed) .. 1 (open)
        private CameraFacingFlare _icon;
        private Transform _padlock;   // MV-1403: padlock built from primitives, floats above the door while locked
        private TextMesh _label;      // MV-1403: floating gold "VAULT" (then "OPEN" for 2 s) over the vault's centre
        private float _openIconTimer = -1f; // -1 = not counting down
        private Transform _player;

        // --- Static registry — same "the map builds them, Build() registers them, a fresh level wipes
        // them" shape as RepairBay.Registered. ---
        private static readonly List<Vault> Registered = new List<Vault>(4);

        public static void ResetRegistry() => Registered.Clear();

        /// <summary>The stable ids of every registered vault already OPEN this run — what a mid-run
        /// checkpoint capture persists. Same shape as <see cref="RepairBay.UsedIds"/>.</summary>
        public static string[] OpenedIds()
        {
            var ids = new List<string>();
            foreach (Vault v in Registered)
                if (v != null && v.State == VaultState.Open && !string.IsNullOrEmpty(v.Id)) ids.Add(v.Id);
            return ids.ToArray();
        }

        /// <summary>Re-applies a checkpoint's already-recorded OPEN set onto whichever currently-
        /// registered, still-CLOSED vaults carry a matching <see cref="Id"/> — called by
        /// <see cref="MaxWorlds.Save.SaveSystem.RestoreCheckpoint"/> on RESUME. Same shape as
        /// <see cref="RepairBay.ApplyCheckpointUsedIds"/>. A no-op for an empty/null set. Never places a
        /// second cache — exactly the ticket's own "an opened vault stays open with no second cache".</summary>
        public static void ApplyCheckpointOpenedIds(IReadOnlyList<string> openedIds)
        {
            if (openedIds == null || openedIds.Count == 0) return;

            foreach (Vault v in Registered)
            {
                if (v == null || v.State != VaultState.Closed || string.IsNullOrEmpty(v.Id)) continue;
                for (int i = 0; i < openedIds.Count; i++)
                {
                    if (openedIds[i] != v.Id) continue;
                    v.ApplyCheckpointOpened();
                    break;
                }
            }
        }

        /// <summary>The nearest registered, still-CLOSED vault to <paramref name="pos"/>, or null if none
        /// is registered — what a KEY elite's death drop (<see cref="MaxWorlds.Enemies.RobotEnemy.Die"/>/
        /// <see cref="MaxWorlds.Enemies.RobotEnemy.Despawn"/>) resolves its dropped key's own vault
        /// binding from. "Nearest" stands in for "the vault in this area" (a tight-slice world authors
        /// one vault per area) without needing a separate area-lookup plumbed through the drop.</summary>
        public static string NearestId(Vector3 pos)
        {
            Vault nearest = null;
            float best = float.MaxValue;
            foreach (Vault v in Registered)
            {
                if (v == null || v.State != VaultState.Closed) continue;
                float d = (v.transform.position - pos).sqrMagnitude;
                if (d < best) { best = d; nearest = v; }
            }
            return nearest != null ? nearest.Id : null;
        }

        /// <summary>Wires this vault to the door <see cref="MaxWorlds.Arena.MapRuntime.BuildVault"/> just
        /// built — called right after AddComponent, same "post-build configure call" convention as
        /// <see cref="DestructibleCover.Configure"/>.</summary>
        public void Configure(string id, Transform door, Vector3 doorClosedLocal, Vector3 doorOpenLocal)
        {
            Id = id ?? string.Empty;
            _door = door;
            _doorClosedLocal = doorClosedLocal;
            _doorOpenLocal = doorOpenLocal;
        }

        private void Awake() => Build();

        /// <summary>Builds the padlock icon and registers. Exposed publicly, same reasoning as
        /// <see cref="RepairBay.Build"/> — <c>Awake</c> never runs as a side effect of <c>AddComponent</c>
        /// outside Play mode, so an EditMode test calls this directly.</summary>
        public void Build()
        {
            if (_icon != null) return; // already built

            if (_door != null)
            {
                _door.localPosition = _doorClosedLocal;
                _doorCollider = _door.GetComponent<Collider>();
            }

            _icon = CameraFacingFlare.Create($"{name} Icon");
            BuildPadlock();
            BuildLabel();
            UpdateIcon();

            if (!Registered.Contains(this)) Registered.Add(this);
        }

        private void OnDestroy() => Registered.Remove(this);

        private void Update() => Tick(Time.deltaTime);

        /// <summary>The per-frame step, pulled out of <see cref="Update"/> with an explicit
        /// <paramref name="dt"/> — same "Tick(dt), not a bare Update()" shape as <see cref="RepairBay.Tick"/>,
        /// so an EditMode test can drive this with no live Unity frame loop.</summary>
        public void Tick(float dt)
        {
            TickDoorSlide(dt);
            TickOpenIconLinger(dt);

            if (State != VaultState.Closed) return;

            Transform player = ResolvePlayer();
            if (player == null) return;
            if (!VaultKeyState.HeldFor(Id)) return;

            float d = Vector3.Distance(Flat(player.position), Flat(transform.position + DoorLocalCenter()));
            if (d <= OpenRadius) Open();
        }

        private Vector3 DoorLocalCenter() => _door != null ? _door.localPosition : Vector3.zero;

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        private Transform ResolvePlayer()
        {
            if (_player != null) return _player;
            GameObject p = GameObject.FindGameObjectWithTag("Player");
            _player = p != null ? p.transform : null;
            return _player;
        }

        private void Open()
        {
            State = VaultState.Open;
            VaultKeyState.Take(Id);
            if (_doorCollider != null) _doorCollider.enabled = false;
            if (_door != null) _door.gameObject.layer = 0; // Default — same "remove it from CoverLayer" idiom DestructibleCover.Break uses
            _openIconTimer = OpenIconLingerSeconds;
            UpdateIcon();
            if (_label != null) _label.text = "OPEN";
            PlaceCache();
        }

        /// <summary>Silently applies a checkpoint's already-recorded OPEN (MV-1321) — same split
        /// <see cref="DestructibleCover.ApplyCheckpointDestroyed"/> keeps: everything <see cref="Open"/>
        /// does EXCEPT placing a second cache — its pickups are gone the instant the level rebuilds and
        /// nothing re-creates them from this call — and except the icon's temporary open-flash, which
        /// would be meaningless on a vault that was already open before this session started.</summary>
        private void ApplyCheckpointOpened()
        {
            State = VaultState.Open;
            _doorSlideT = 1f;
            if (_door != null)
            {
                _door.localPosition = _doorOpenLocal;
                if (_doorCollider != null) _doorCollider.enabled = false;
                _door.gameObject.layer = 0;
            }
            UpdateIcon();
            HideLabel();
        }

        private void TickDoorSlide(float dt)
        {
            // MV-1403: only an OPEN vault slides its door. This used to run unconditionally, so every
            // vault's door slid away 0.5 s after the first frame, key or no key.
            if (_door == null || State != VaultState.Open || _doorSlideT >= 1f) return;
            _doorSlideT = Mathf.Min(1f, _doorSlideT + dt / DoorSlideSeconds);
            _door.localPosition = Vector3.Lerp(_doorClosedLocal, _doorOpenLocal, _doorSlideT);
        }

        private void TickOpenIconLinger(float dt)
        {
            if (_openIconTimer < 0f) return;
            _openIconTimer -= dt;
            if (_openIconTimer <= 0f)
            {
                _openIconTimer = -1f;
                HideLabel();
            }
        }

        private void UpdateIcon()
        {
            if (_icon == null) return;
            if (State == VaultState.Closed)
            {
                _icon.Show(IconCenter, IconDiameter, ClosedIconColor);
                if (_padlock != null) _padlock.gameObject.SetActive(true);
                return;
            }

            // MV-1403: "The padlock goes when it opens."
            _icon.Hide();
            if (_padlock != null) _padlock.gameObject.SetActive(false);
        }

        private void HideLabel()
        {
            if (_label != null) _label.gameObject.SetActive(false);
        }

        /// <summary>The floating word, centred over the vault (MV-1403). A legacy <see cref="TextMesh"/> on
        /// the built-in font, same "no font asset, renders headless" reasoning <see cref="HudFont"/> gives.</summary>
        private void BuildLabel()
        {
            var go = new GameObject($"{name} Label");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, LabelHeight, 0f);
            go.transform.localRotation = Quaternion.Euler(PadlockPitchDegrees, 0f, 0f);

            _label = go.AddComponent<TextMesh>();
            _label.font = HudFont.Get();
            _label.fontSize = LabelFontSize;
            _label.characterSize = LabelCharacterSize;
            _label.fontStyle = FontStyle.Bold;
            _label.anchor = TextAnchor.MiddleCenter;
            _label.alignment = TextAlignment.Center;
            _label.color = ClosedIconColor;
            _label.text = "VAULT";

            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                mr.sharedMaterial = _label.font.material;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
            }

            // A vault already open at build (checkpoint resume) never shows it; ApplyCheckpointOpened hides it.
            go.SetActive(State == VaultState.Closed);
        }

        /// <summary>A padlock from three primitives (body + two-post-and-bar shackle), floating above the
        /// door and tilted to face the fixed top-down camera � no collider, shown only while Closed.</summary>
        private void BuildPadlock()
        {
            var root = new GameObject($"{name} Padlock");
            root.transform.SetParent(transform, false);
            root.transform.localPosition = DoorLocalCenter() + Vector3.up * IconHeight;
            root.transform.localRotation = Quaternion.Euler(PadlockPitchDegrees, 0f, 0f);
            _padlock = root.transform;

            Material gold = MaterialLibrary.Tinted(SurfaceKind.Metal, new Color(1f, 0.82f, 0.25f));
            AddPadlockPart(root.transform, "Body", new Vector3(0f, -0.12f, 0f), new Vector3(0.62f, 0.46f, 0.12f), gold);
            AddPadlockPart(root.transform, "ShackleL", new Vector3(-0.17f, 0.22f, 0f), new Vector3(0.10f, 0.30f, 0.10f), gold);
            AddPadlockPart(root.transform, "ShackleR", new Vector3(0.17f, 0.22f, 0f), new Vector3(0.10f, 0.30f, 0.10f), gold);
            AddPadlockPart(root.transform, "ShackleTop", new Vector3(0f, 0.38f, 0f), new Vector3(0.44f, 0.10f, 0.10f), gold);
        }

        private static void AddPadlockPart(Transform parent, string partName, Vector3 localPos, Vector3 size, Material mat)
        {
            GameObject part = GameObject.CreatePrimitive(PrimitiveType.Cube);
            part.name = partName;
            Collider c = part.GetComponent<Collider>();
            if (c != null) Object.DestroyImmediate(c); // visual only � never blocks Max or a shot
            part.transform.SetParent(parent, false);
            part.transform.localPosition = localPos;
            part.transform.localScale = size;
            part.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }

        private Vector3 IconCenter => transform.position + DoorLocalCenter() + Vector3.up * IconHeight;

        /// <summary>2 Supercell + 6 PowerCell at the vault's own centre (ticket Change 2) — placed only
        /// on a genuine <see cref="Open"/>, never on <see cref="ApplyCheckpointOpened"/>'s silent resume
        /// re-apply, same "placed at OPEN, not at build, never a second cache" shape
        /// <see cref="DestructibleCover.PlaceSecretCache"/>'s own split already established.</summary>
        private void PlaceCache()
        {
            PickupDirector director = PickupDirector.EnsureInstalled();
            for (int i = 0; i < SupercellCount; i++)
            {
                float ang = i * (Mathf.PI * 2f / SupercellCount);
                Vector3 off = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * CacheScatterRadius;
                director.PlacePickupAt(PickupKind.Supercell, transform.position + off);
            }
            for (int i = 0; i < PowerCellCount; i++)
            {
                float ang = i * (Mathf.PI * 2f / PowerCellCount) + 0.3f; // offset so cells don't co-locate with the Supercells above
                Vector3 off = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * (CacheScatterRadius * 1.4f);
                director.PlacePickupAt(PickupKind.PowerCell, transform.position + off);
            }
        }
    }

    /// <summary>MV-1321: holds the single VaultKey Max can carry at once, bound to the vault id it opens
    /// (ticket: "a key only opens the vault in the area it dropped in") — same static/event-driven shape
    /// as <see cref="MaxWorlds.Weapons.PendingMorphingModule"/>. A fresh run needs no explicit reset the
    /// way RigState/PickupWallet do (same reasoning that class's own doc comment gives); EditMode tests
    /// call <see cref="Reset"/> directly for isolation.</summary>
    public static class VaultKeyState
    {
        private static string s_heldVaultId;

        /// <summary>The vault id this key opens, or null while nothing is held.</summary>
        public static string HeldVaultId => s_heldVaultId;

        public static bool IsHeld => !string.IsNullOrEmpty(s_heldVaultId);

        public static bool HeldFor(string vaultId) => !string.IsNullOrEmpty(vaultId) && s_heldVaultId == vaultId;

        /// <summary>Fired whenever the held key is banked, taken, or reset.</summary>
        public static event System.Action Changed;

        /// <summary>Bank a collected VaultKey (<see cref="PickupDirector.Collect"/>'s own
        /// <see cref="PickupKind.VaultKey"/> case). A key for a DIFFERENT vault replaces the held one
        /// outright — this tight slice carries at most one at a time.</summary>
        public static void SetHeld(string vaultId)
        {
            if (string.IsNullOrEmpty(vaultId) || s_heldVaultId == vaultId) return;
            s_heldVaultId = vaultId;
            Changed?.Invoke();
        }

        /// <summary>Consumes the held key — <see cref="Vault.Open"/>'s own entry point. Returns false if
        /// nothing was held for this vault.</summary>
        public static bool Take(string vaultId)
        {
            if (!HeldFor(vaultId)) return false;
            s_heldVaultId = null;
            Changed?.Invoke();
            return true;
        }

        /// <summary>Restore the held-key id from a captured checkpoint (MV-1321) — a cold-boot RESUME
        /// between collecting a key and reaching its vault must not silently lose it.</summary>
        public static void Restore(string vaultId)
        {
            string normalized = string.IsNullOrEmpty(vaultId) ? null : vaultId;
            if (s_heldVaultId == normalized) return;
            s_heldVaultId = normalized;
            Changed?.Invoke();
        }

        /// <summary>Back to a fresh run's baseline: nothing held. Test isolation and a new run.</summary>
        public static void Reset()
        {
            if (s_heldVaultId == null) return;
            s_heldVaultId = null;
            Changed?.Invoke();
        }
    }
}
