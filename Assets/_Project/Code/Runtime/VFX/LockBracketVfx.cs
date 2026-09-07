using UnityEngine;
using MaxWorlds.Enemies;

namespace MaxWorlds.VFX
{
    /// <summary>
    /// The LPPE's lock-on tell (MV-702) — four corner brackets that snap onto a
    /// <see cref="RobotEnemy"/> the instant <see cref="MaxWorlds.Weapons.SeekerPulse"/> locks onto it,
    /// then fade. MV-708's own commit named this item and deferred it explicitly: "a bespoke
    /// Seeker-reticle bracket... the reticle/muzzle-glow polish is pure cosmetic surface not covered by
    /// any AC" — this is that bracket.
    /// </summary>
    public sealed class LockBracketVfx : MonoBehaviour
    {
        private const float LifetimeSeconds = 0.25f;
        private const float BracketSize = 0.9f;
        private const float CornerLength = 0.28f;

        /// <summary>The same cyan-white as the bolt (<c>SeekerPulse.BoltColor</c>) — the bracket and the
        /// shot it precedes read as one weapon, not two.</summary>
        private static readonly Color BracketColor = new Color(0.55f, 0.95f, 1f);

        private Transform _target;
        private float _age;
        private LineRenderer[] _corners;

        /// <summary>Snap a bracket onto <paramref name="target"/>. A no-op if nothing locked — a miss
        /// shows no reticle, which is itself the tell that the shot is going in unguided.</summary>
        public static void Show(RobotEnemy target)
        {
            if (target == null) return;

            var go = new GameObject("LockBracket");
            var vfx = go.AddComponent<LockBracketVfx>();
            vfx._target = target.transform;
            vfx.Build();
        }

        private void Build()
        {
            Material mat = VfxMaterials.Additive(VfxMaterials.Glow());

            _corners = new LineRenderer[4];
            _corners[0] = NewCorner(mat, new Vector2(-1, -1), new Vector2(1, 0), new Vector2(0, 1));
            _corners[1] = NewCorner(mat, new Vector2(1, -1), new Vector2(-1, 0), new Vector2(0, 1));
            _corners[2] = NewCorner(mat, new Vector2(1, 1), new Vector2(-1, 0), new Vector2(0, -1));
            _corners[3] = NewCorner(mat, new Vector2(-1, 1), new Vector2(1, 0), new Vector2(0, -1));

            Follow();
        }

        private LineRenderer NewCorner(Material mat, Vector2 corner, Vector2 armA, Vector2 armB)
        {
            var lr = new GameObject("Corner").AddComponent<LineRenderer>();
            lr.transform.SetParent(transform, false);
            lr.positionCount = 3;
            lr.widthMultiplier = 0.03f;
            lr.useWorldSpace = false;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.sharedMaterial = mat;
            lr.startColor = BracketColor;
            lr.endColor = BracketColor;

            float half = BracketSize * 0.5f;
            Vector3 c = new Vector3(corner.x * half, 0f, corner.y * half);
            lr.SetPosition(0, c + new Vector3(armA.x, 0f, armA.y) * CornerLength);
            lr.SetPosition(1, c);
            lr.SetPosition(2, c + new Vector3(armB.x, 0f, armB.y) * CornerLength);
            return lr;
        }

        private void Update()
        {
            if (_target == null) { Destroy(gameObject); return; }
            Follow();

            _age += Time.deltaTime;
            float t = Mathf.Clamp01(_age / LifetimeSeconds);
            Color c = new Color(BracketColor.r, BracketColor.g, BracketColor.b, 1f - t);
            for (int i = 0; i < _corners.Length; i++)
            {
                if (_corners[i] == null) continue;
                _corners[i].startColor = c;
                _corners[i].endColor = c;
            }
            if (t >= 1f) Destroy(gameObject);
        }

        /// <summary>Ground-plane brackets at chest height — the fixed ~72&#176; camera reads a flat
        /// bracket as a lock ring the same way <see cref="GroundRing"/>'s danger telegraphs read as
        /// ground marks, rather than a screen-space HUD element that would have to fight the camera's
        /// own projection.</summary>
        private void Follow()
        {
            transform.position = _target.position + Vector3.up * 1.0f;
            transform.rotation = Quaternion.identity;
        }
    }
}
