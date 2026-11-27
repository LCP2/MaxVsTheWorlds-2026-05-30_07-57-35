using UnityEngine;
using MaxWorlds.VFX;

namespace MaxWorlds.Enemies
{
    /// <summary>
    /// MV-1429: the City MARKSMAN Bolter's weapon. While Max is 9-13 m away, in sight and on the same
    /// combat level and the 2.5 s cooldown is ready, it stops and draws a red aim line from its rail to Max
    /// for 0.6 s (tracking him; the direction locks for the last 0.1 s), then fires ONE straight bolt along
    /// the locked direction (<see cref="BolterBolt.FireMarksman"/>). A plain class with a public
    /// <see cref="Tick"/> seam, owned and ticked by <see cref="RobotEnemy"/> every awake frame.
    /// </summary>
    public sealed class MarksmanRifle
    {
        public const float BandMin = 9f;
        public const float BandMax = 13f;
        public const float AimSeconds = 0.6f;
        public const float LockSeconds = 0.1f;
        public const float CooldownSeconds = 2.5f;
        public const float AimLineWidth = 0.08f;
        public const float MuzzleHeight = 1.3f * RobotBodies.SmallRangedScale;   // MV-1477: follows the 75% body
        public const float MuzzleForward = 1.45f * RobotBodies.SmallRangedScale;

        private float _cooldown;
        private float _aimElapsed;
        private bool _aiming;
        private Vector3 _lockedDir;
        private LineRenderer _line;

        public bool IsAiming => _aiming;
        public float AimElapsed => _aimElapsed;
        public int Shots { get; private set; }
        public BolterBolt LastBolt { get; private set; }

        /// <summary>The aim line (null until the first aim) — read by tests and captures.</summary>
        public LineRenderer AimLine => _line;

        /// <summary>Advance by <paramref name="dt"/>. <paramref name="feet"/> is the robot's ground position.
        /// Returns true on the tick a bolt is fired.</summary>
        public bool Tick(float dt, Vector3 feet, Transform target, bool hasSight, bool sameLevel, float hitRadius)
        {
            if (_cooldown > 0f) _cooldown = Mathf.Max(0f, _cooldown - dt);

            if (target == null || !sameLevel) { Cancel(); return false; }

            Vector3 flat = target.position - feet; flat.y = 0f;
            float dist = flat.magnitude;
            Vector3 dir = dist > 1e-3f ? flat / dist : Vector3.forward;

            if (!_aiming)
            {
                if (!hasSight || _cooldown > 0f || dist < BandMin || dist > BandMax + 0.01f) return false;
                _aiming = true;
                _aimElapsed = 0f;
                _lockedDir = dir;
            }
            else if (!hasSight || dist < BandMin)
            {
                Cancel();   // Max closed in (it backs off) or ducked behind cover: the shot is dropped
                return false;
            }

            _aimElapsed += dt;
            if (_aimElapsed < AimSeconds - LockSeconds) _lockedDir = dir;   // tracking; locked for the last 0.1 s

            Vector3 muzzle = new Vector3(feet.x, feet.y + MuzzleHeight, feet.z) + _lockedDir * MuzzleForward;
            ShowLine(muzzle, _lockedDir, Mathf.Max(1f, dist - MuzzleForward));

            if (_aimElapsed < AimSeconds) return false;

            LastBolt = BolterBolt.FireMarksman(muzzle, _lockedDir, target, hitRadius);
            Shots++;
            _cooldown = CooldownSeconds;
            _aiming = false;
            _aimElapsed = 0f;
            HideLine();
            return true;
        }

        private void ShowLine(Vector3 from, Vector3 dir, float length)
        {
            if (_line == null)
            {
                var go = new GameObject("MarksmanAimLine");
                _line = go.AddComponent<LineRenderer>();
                _line.sharedMaterial = VfxMaterials.Additive(VfxMaterials.LineGlow());
                _line.useWorldSpace = true;
                _line.positionCount = 2;
                _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _line.receiveShadows = false;
                _line.widthMultiplier = AimLineWidth;
                _line.startColor = _line.endColor = HostileShotLook.Red;
            }
            _line.enabled = true;
            _line.SetPosition(0, from);
            _line.SetPosition(1, from + dir * length);
        }

        private void HideLine()
        {
            if (_line != null) _line.enabled = false;
        }

        /// <summary>Drop a live aim with no shot (Max left the band, sight broke, the robot died/was captured).</summary>
        public void Cancel()
        {
            _aiming = false;
            _aimElapsed = 0f;
            HideLine();
        }

        /// <summary>Destroy the aim-line object (robot teardown / test cleanup).</summary>
        public void Dispose()
        {
            Cancel();
            if (_line == null) return;
            if (Application.isPlaying) Object.Destroy(_line.gameObject);
            else Object.DestroyImmediate(_line.gameObject);
            _line = null;
        }
    }
}
