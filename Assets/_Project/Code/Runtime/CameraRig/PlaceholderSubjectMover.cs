using UnityEngine;

namespace MaxWorlds.CameraRig
{
    /// <summary>
    /// TEMPORARY (YT-33 verification only). Moves a stand-in subject along a
    /// figure-eight so the camera's follow + look-ahead can be eyeballed before
    /// real player movement lands in YT-34. Delete this component (and the
    /// placeholder subject) once Max is the follow target.
    /// </summary>
    public sealed class PlaceholderSubjectMover : MonoBehaviour
    {
        [SerializeField] private float speed = 1.2f;
        [SerializeField] private Vector2 extents = new Vector2(6f, 4f);

        private Vector3 _origin;
        private float _t;

        private void Awake() => _origin = transform.position;

        private void Update()
        {
            _t += Time.deltaTime * speed;
            float x = Mathf.Sin(_t) * extents.x;
            float z = Mathf.Sin(_t * 2f) * extents.y;
            transform.position = _origin + new Vector3(x, 0f, z);
        }
    }
}
