using UnityEngine;

namespace MaxWorlds.VFX
{
    /// <summary>
    /// Shock's zigzag tell (MV-702; spec: "yellow zigzag flash"). <see cref="MaxWorlds.Enemies.RobotEnemy.Stun"/>
    /// already flashes the robot's own body colour (<c>SetTell</c>) — the same greybox idiom every tell
    /// in that class uses — but a colour flash alone doesn't say WHICH effect just landed once more than
    /// one tell shares a colour family. This is the shape that does: a bent line hovering over the
    /// stunned robot for the stun's own duration.
    /// </summary>
    public sealed class ShockZigzagVfx : MonoBehaviour
    {
        /// <summary><c>RobotEnemy.ShockTell</c>, to the digit — the zigzag and the body flash read as
        /// one effect.</summary>
        private static readonly Color ZigzagColor = new Color(1f, 0.86f, 0.1f);

        private Transform _owner;
        private float _timer;
        private LineRenderer _line;

        /// <summary>Show (or refresh) the zigzag over <paramref name="owner"/> for <paramref name="seconds"/>.
        /// One per owner: a fresh Shock hit within the combo window extends an existing zigzag rather
        /// than stacking a second one on top of itself, mirroring <c>RobotEnemy.Stun</c>'s own
        /// "never shortens, never stacks" convention.</summary>
        public static void Show(Transform owner, float seconds)
        {
            if (owner == null || seconds <= 0f) return;

            var existing = owner.GetComponentInChildren<ShockZigzagVfx>();
            if (existing != null) { existing._timer = seconds; return; }

            var go = new GameObject("ShockZigzag");
            var vfx = go.AddComponent<ShockZigzagVfx>();
            vfx._owner = owner;
            vfx._timer = seconds;
            vfx.Build();
        }

        private void Build()
        {
            _line = gameObject.AddComponent<LineRenderer>();
            _line.positionCount = 5;
            _line.widthMultiplier = 0.05f;
            _line.useWorldSpace = false;
            _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _line.receiveShadows = false;
            _line.sharedMaterial = VfxMaterials.Additive(VfxMaterials.Glow());
            _line.startColor = ZigzagColor;
            _line.endColor = ZigzagColor;

            // Flat in XZ, not a vertical bolt: the fixed ~72-degree top-down camera foreshortens
            // anything standing straight up almost to a dot, the same reason GroundRing's telegraphs
            // lie flat rather than standing up.
            _line.SetPosition(0, new Vector3(-0.18f, 0f, 0.3f));
            _line.SetPosition(1, new Vector3(0.1f, 0f, 0.15f));
            _line.SetPosition(2, new Vector3(-0.1f, 0f, 0f));
            _line.SetPosition(3, new Vector3(0.1f, 0f, -0.15f));
            _line.SetPosition(4, new Vector3(-0.18f, 0f, -0.3f));
        }

        private void Update()
        {
            if (_owner == null) { Destroy(gameObject); return; }

            transform.position = _owner.position + Vector3.up * 1.6f;

            _timer -= Time.deltaTime;
            if (_timer <= 0f) Destroy(gameObject);
        }
    }
}
