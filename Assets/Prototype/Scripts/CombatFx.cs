using System.Collections.Generic;
using UnityEngine;

namespace TankGame.Prototype
{
    /// <summary>Tiny pool: components are instantiated up front and recycled, so combat does not allocate.</summary>
    public sealed class Pool<T> where T : Component
    {
        readonly T m_Prefab;
        readonly Transform m_Parent;
        readonly Stack<T> m_Free = new Stack<T>();

        public Pool(T prefab, Transform parent, int prewarm)
        {
            m_Prefab = prefab;
            m_Parent = parent;
            for (int i = 0; i < prewarm; i++)
            {
                T c = Object.Instantiate(prefab, parent);
                c.gameObject.SetActive(false);
                m_Free.Push(c);
            }
        }

        public T Get()
        {
            T c = m_Free.Count > 0 ? m_Free.Pop() : Object.Instantiate(m_Prefab, m_Parent);
            c.gameObject.SetActive(true);
            return c;
        }

        public void Release(T c)
        {
            if (!c.gameObject.activeSelf) return;
            c.gameObject.SetActive(false);
            m_Free.Push(c);
        }
    }

    /// <summary>
    /// Combat feel hub: owns the projectile/VFX pools, hit-stop, camera shake access and the audio hooks.
    /// Audio clips are optional; leave them empty and the hooks do nothing.
    /// </summary>
    public sealed class CombatFx : MonoBehaviour
    {
        [Header("Prefabs")]
        public Projectile projectilePrefab;
        public PooledFx muzzlePrefab;
        public PooledFx impactPrefab;
        public PooledFx hitPrefab;
        public PooledFx explosionPrefab;
        public PooledFx pickupPrefab;   // optional
        public PooledFx dashPrefab;     // optional

        [Header("Feel")]
        public CameraRig cameraRig;
        [Range(0.01f, 0.5f)] public float hitStopTimeScale = 0.05f;

        [Header("Audio hooks (optional)")]
        public AudioSource audioSource;
        public AudioClip fireClip;
        public AudioClip hitClip;
        public AudioClip explodeClip;

        Pool<Projectile> m_Projectiles;
        Pool<PooledFx> m_Muzzle, m_Impact, m_Hit, m_Explosion, m_Pickup, m_Dash;
        float m_HitStopUntil, m_NextHitStopAt;

        void Awake()
        {
            m_Projectiles = new Pool<Projectile>(projectilePrefab, transform, 24);
            m_Muzzle = new Pool<PooledFx>(muzzlePrefab, transform, 6);
            m_Impact = new Pool<PooledFx>(impactPrefab, transform, 12);
            m_Hit = new Pool<PooledFx>(hitPrefab, transform, 8);
            m_Explosion = new Pool<PooledFx>(explosionPrefab, transform, 5);
            if (pickupPrefab != null) m_Pickup = new Pool<PooledFx>(pickupPrefab, transform, 3);
            if (dashPrefab != null) m_Dash = new Pool<PooledFx>(dashPrefab, transform, 4);
        }

        void OnDisable()
        {
            Time.timeScale = 1f;
        }

        void Update()
        {
            if (m_HitStopUntil > 0f && Time.unscaledTime >= m_HitStopUntil)
            {
                m_HitStopUntil = 0f;
                Time.timeScale = 1f;
            }
        }

        public void SpawnProjectile(TankUnit owner, Vector3 position, Vector3 direction, WeaponDef weapon)
        {
            m_Projectiles.Get().Launch(owner, this, position, direction, weapon);
        }

        public void ReleaseProjectile(Projectile p) { m_Projectiles.Release(p); }

        public void SpawnMuzzle(Vector3 position, Vector3 direction)
        {
            m_Muzzle.Get().Begin(m_Muzzle, position, Quaternion.LookRotation(direction));
            Play(fireClip, position);
        }

        public void SpawnImpact(Vector3 position, Vector3 normal, bool onTank)
        {
            Pool<PooledFx> pool = onTank ? m_Hit : m_Impact;
            pool.Get().Begin(pool, position, Quaternion.LookRotation(normal));
            if (onTank) Play(hitClip, position);
        }

        public void SpawnExplosion(Vector3 position)
        {
            m_Explosion.Get().Begin(m_Explosion, position, Quaternion.identity);
            Play(explodeClip, position);
        }

        public void SpawnPickup(Vector3 position)
        {
            if (m_Pickup != null) m_Pickup.Get().Begin(m_Pickup, position, Quaternion.identity);
        }

        public void SpawnDash(Vector3 position, Vector3 direction)
        {
            if (m_Dash != null) m_Dash.Get().Begin(m_Dash, position, Quaternion.LookRotation(direction));
        }

        /// <summary>Freeze-frame: a short near-stop of game time. Longer for kills than for hits.</summary>
        public void HitStop(float seconds)
        {
            if (seconds <= 0f) return;
            // at most one freeze every 0.4 s, so fast weapons never turn into a slideshow
            if (Time.unscaledTime < m_NextHitStopAt) return;
            m_NextHitStopAt = Time.unscaledTime + 0.4f;
            m_HitStopUntil = Mathf.Max(m_HitStopUntil, Time.unscaledTime + seconds);
            Time.timeScale = hitStopTimeScale;
        }

        public void Shake(float trauma)
        {
            if (cameraRig != null) cameraRig.AddTrauma(trauma);
        }

        /// <summary>Camera shake that fades with distance from the camera target, for events the local player did not cause.</summary>
        public void ShakeAt(Vector3 worldPosition, float trauma)
        {
            if (cameraRig == null || cameraRig.target == null) return;
            float d = Vector3.Distance(worldPosition, cameraRig.target.position);
            float k = 1f - Mathf.InverseLerp(6f, 28f, d);
            if (k > 0f) cameraRig.AddTrauma(trauma * k);
        }

        void Play(AudioClip clip, Vector3 position)
        {
            if (clip != null && audioSource != null) audioSource.PlayOneShot(clip);
        }
    }
}
