using System.Collections.Generic;
using UnityEngine;
using MaxWorlds.Core;
using MaxWorlds.Rendering;
using MaxWorlds.VFX;

namespace MaxWorlds.Weapons
{
    /// <summary>
    /// UNDERTOW's charged shot (MV-714) — a slow spinning bubble that travels straight forward and
    /// implodes on impact or at max range (<see cref="CavitationImplosion"/>). Free-flying and not
    /// pooled, same lifetime shape as <see cref="PlayerRocket"/>: short-lived, self-destroys on
    /// detonation. Unlike <see cref="PlayerRocket"/> it never homes — the spec's "slow spinning bubble"
    /// travels the barrel's own facing, not toward a locked target.
    /// </summary>
    public sealed class CavitationBubble : MonoBehaviour
    {
        private const float ContactRadius = 0.35f;

        /// <summary>Cyan-white (spec: "Muzzle and lance colour are cyan-white so they stay legible
        /// against World 3's orange machinery") — the same colour <see cref="CavitationImplosion"/>'s
        /// impact ring uses, so the travelling bubble and the ring it leaves behind read as one weapon.</summary>
        private static readonly Color BodyColor = new Color(0.55f, 0.95f, 1f);

        private static readonly List<CavitationBubble> s_active = new List<CavitationBubble>();

        /// <summary>Every bubble currently in flight — <c>UndertowTests</c> counts these to prove AC4's
        /// cooldown refusal never spawned a second bubble, the same static-registry shape
        /// <see cref="PlayerRocket.Active"/> already uses.</summary>
        public static IReadOnlyList<CavitationBubble> Active => s_active;

        /// <summary>Force-clears every tracked bubble — <c>[TearDown]</c> hygiene, same shape as
        /// <see cref="PlayerRocket.DestroyAllActive"/>.</summary>
        public static void DestroyAllActive()
        {
            for (int i = s_active.Count - 1; i >= 0; i--)
            {
                if (s_active[i] != null) DestroyImmediate(s_active[i].gameObject);
            }
            s_active.Clear();
        }

        private Vector3 _origin;
        private Vector3 _dir;
        private float _speed;
        private float _maxRange;
        private float _damage;
        private float _pullRadius;
        private float _pullDistance;
        private float _staggerSeconds;
        private bool _detonated;

        /// <summary>Launch one bubble from <paramref name="origin"/> along <paramref name="dir"/>.
        /// Mirrors <see cref="PlayerRocket.Fire"/>'s static-builder shape.</summary>
        public static CavitationBubble Fire(Vector3 origin, Vector3 dir, float speed, float maxRange,
            float damage, float pullRadius, float pullDistance, float staggerSeconds)
        {
            var go = new GameObject("CavitationBubble (stand-in)");
            go.transform.position = origin;
            go.transform.rotation = Quaternion.LookRotation(dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.forward, Vector3.up);

            BuildVisual(go.transform);

            var bubble = go.AddComponent<CavitationBubble>();
            bubble.Init(origin, dir, speed, maxRange, damage, pullRadius, pullDistance, staggerSeconds);
            s_active.Add(bubble);
            return bubble;
        }

        private static void BuildVisual(Transform parent)
        {
            parent.gameObject.AddComponent<KeepsOwnMaterial>();

            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = "Bubble";
            var col = sphere.GetComponent<Collider>();
            if (col != null)
            {
                if (Application.isPlaying) Destroy(col);
                else DestroyImmediate(col);
            }
            sphere.transform.SetParent(parent, false);
            sphere.transform.localScale = Vector3.one * (ContactRadius * 2f);
            var mat = MaterialLibrary.Tinted(SurfaceKind.Metal, BodyColor);
            if (mat != null) sphere.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }

        private void Init(Vector3 origin, Vector3 dir, float speed, float maxRange, float damage,
            float pullRadius, float pullDistance, float staggerSeconds)
        {
            _origin = origin;
            _dir = dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.forward;
            _speed = speed;
            _maxRange = maxRange;
            _damage = damage;
            _pullRadius = pullRadius;
            _pullDistance = pullDistance;
            _staggerSeconds = staggerSeconds;
        }

        private void Update()
        {
            if (_detonated) return;
            float dt = Time.deltaTime;

            Vector3 from = transform.position;
            Vector3 next = from + _dir * (_speed * dt);

            if (HomingSteering.BlockedByGeometry(from, next, out RaycastHit hit))
            {
                Detonate(hit.point);
                return;
            }

            transform.position = next;

            if ((transform.position - _origin).sqrMagnitude >= _maxRange * _maxRange) Detonate(transform.position);
        }

        private void Detonate(Vector3 point)
        {
            _detonated = true;
            s_active.Remove(this);
            CavitationImplosion.Apply(point, _damage, _pullRadius, _pullDistance, _staggerSeconds);
            Destroy(gameObject);
        }
    }
}
