using UnityEngine;

namespace MaxWorlds.VFX
{
    /// <summary>
    /// MV-584: shrinks the GameObject it's attached to down to nothing over a fixed duration, then
    /// destroys it. Used only for the Blinker teleport's departure ghost — a temporary body clone left
    /// behind at the point a robot blinked FROM, with no owner left to tick it once the real robot has
    /// already snapped away to its landing point (<see cref="RobotRig"/> disposes of the original the
    /// instant it's pooled, which the ghost must survive independently of).
    /// </summary>
    public sealed class TeleportGhostCollapse : MonoBehaviour
    {
        private float _duration;
        private float _t;
        private Vector3 _startScale;

        public void Begin(float duration)
        {
            _duration = Mathf.Max(duration, 0.01f);
            _startScale = transform.localScale;
        }

        private void Update()
        {
            _t += Time.deltaTime;
            float u = Mathf.Clamp01(_t / _duration);
            transform.localScale = Vector3.Lerp(_startScale, Vector3.zero, u);
            if (u >= 1f) Destroy(gameObject);
        }
    }
}
