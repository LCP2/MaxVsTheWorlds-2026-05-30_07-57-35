using UnityEngine;

namespace MaxWorlds.VFX
{
    /// <summary>
    /// Eight additive shards radiating outward from a point (MV-1131, the new-weapon moment's gun
    /// flash) — thin quads fanned evenly around the live camera's own view axis, each stretching from
    /// the centre out to its own authored length. Purely decorative (no assertion drives its shape), so
    /// this stays a single disposable component rather than a reusable library the way
    /// <see cref="GroundRing"/> is for ground marks.
    /// </summary>
    public sealed class ShardBurst : MonoBehaviour
    {
        private const int Count = 8;
        private Transform[] _shards;
        private MeshRenderer[] _renderers;
        private MaterialPropertyBlock _mpb;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        public static ShardBurst Create(string name)
        {
            var root = new GameObject(name);
            var burst = root.AddComponent<ShardBurst>();
            burst._mpb = new MaterialPropertyBlock();
            burst._shards = new Transform[Count];
            burst._renderers = new MeshRenderer[Count];

            Material mat = VfxMaterials.Additive(VfxMaterials.Glow());
            for (int i = 0; i < Count; i++)
            {
                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quad.name = $"Shard {i}";
                quad.transform.SetParent(root.transform, worldPositionStays: false);

                var col = quad.GetComponent<Collider>();
                if (col != null)
                {
                    if (Application.isPlaying) Destroy(col);
                    else DestroyImmediate(col);
                }

                var mr = quad.GetComponent<MeshRenderer>();
                mr.sharedMaterial = mat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;

                burst._shards[i] = quad.transform;
                burst._renderers[i] = mr;
            }

            burst.Hide();
            return burst;
        }

        public bool Visible => gameObject.activeSelf;

        /// <summary>Fan the shards out from <paramref name="pos"/>, each between <paramref name="minLength"/>
        /// and <paramref name="maxLength"/> m long (evenly spread across the eight), <paramref name="width"/>
        /// m wide, at <paramref name="alpha"/> — facing whichever camera is live, same full-rotation
        /// billboard <see cref="CameraFacingFlare"/> uses.</summary>
        public void Show(Vector3 pos, float minLength, float maxLength, float width, Color color, float alpha)
        {
            if (!gameObject.activeSelf) gameObject.SetActive(true);

            Camera cam = Camera.main;
            Quaternion camRot = cam != null ? cam.transform.rotation : Quaternion.identity;
            Color c = color; c.a = alpha;

            for (int i = 0; i < Count; i++)
            {
                float t = Count <= 1 ? 0f : (float)i / (Count - 1);
                float length = Mathf.Lerp(minLength, maxLength, t);
                float angle = i * (360f / Count);

                Transform shard = _shards[i];
                // Spin around the camera's own forward axis so the fan reads flat against the lens,
                // then slide the quad out along its own local-up so it radiates from pos rather than
                // being centred on it.
                shard.rotation = camRot * Quaternion.Euler(0f, 0f, angle);
                shard.position = pos + shard.up * (length * 0.5f);
                shard.localScale = new Vector3(width, length, 1f);

                _renderers[i].GetPropertyBlock(_mpb);
                _mpb.SetColor(BaseColorId, c);
                _renderers[i].SetPropertyBlock(_mpb);
            }
        }

        public void Hide()
        {
            if (gameObject.activeSelf) gameObject.SetActive(false);
        }
    }
}
