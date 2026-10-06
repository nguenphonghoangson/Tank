using UnityEngine;

namespace TankGame.Prototype
{
    /// <summary>Fast straight-line projectile. A sphere cast each frame covers the whole step, so it cannot tunnel.</summary>
    public sealed class Projectile : MonoBehaviour
    {
        public float radius = 0.15f;
        public TrailRenderer trail;

        static readonly RaycastHit[] s_Hits = new RaycastHit[8];
        static readonly Collider[] s_Splash = new Collider[16];
        static int s_SplashStamp;

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        MeshRenderer m_Shell;
        MaterialPropertyBlock m_Block;
        int m_Damage;
        TankUnit m_Owner;
        CombatFx m_Fx;
        WeaponDef m_Weapon;
        Vector3 m_Dir;
        float m_Age, m_Life;

        public void Launch(TankUnit owner, CombatFx fx, Vector3 position, Vector3 direction, WeaponDef weapon)
        {
            m_Owner = owner;
            m_Fx = fx;
            m_Weapon = weapon;
            m_Dir = direction.normalized;
            m_Age = 0f;
            m_Life = weapon.ProjectileLife;
            m_Damage = Mathf.RoundToInt(weapon.damage * owner.DamageMultiplier);
            transform.SetPositionAndRotation(position, Quaternion.LookRotation(m_Dir));

            // each weapon has its own look: tracer size and colour
            if (m_Shell == null) { m_Shell = GetComponentInChildren<MeshRenderer>(); m_Block = new MaterialPropertyBlock(); }
            m_Shell.transform.localScale = new Vector3(0.28f * weapon.projectileScale.x, 0.28f * weapon.projectileScale.y, 1f * weapon.projectileScale.z);
            m_Shell.GetPropertyBlock(m_Block);
            m_Block.SetColor(BaseColorId, weapon.projectileColor);
            m_Shell.SetPropertyBlock(m_Block);
            if (trail != null)
            {
                trail.Clear();
                trail.startColor = weapon.projectileColor;
                trail.endColor = new Color(weapon.projectileColor.r, weapon.projectileColor.g * 0.5f, 0.1f, 0f);
                trail.widthMultiplier = weapon.projectileScale.x;
            }
        }

        void Update()
        {
            float dt = Time.deltaTime;
            float step = m_Weapon.projectileSpeed * dt;
            Vector3 pos = transform.position;

            int n = Physics.SphereCastNonAlloc(pos, radius, m_Dir, s_Hits, step, ~0, QueryTriggerInteraction.Ignore);
            int best = -1;
            float bestDist = float.MaxValue;
            TankUnit bestTank = null;
            for (int i = 0; i < n; i++)
            {
                RaycastHit h = s_Hits[i];
                TankUnit t = h.collider.GetComponentInParent<TankUnit>();
                if (t != null && (t == m_Owner || t.IsDead || t.team == m_Owner.team)) continue;
                if (h.distance < bestDist) { bestDist = h.distance; best = i; bestTank = t; }
            }

            if (best >= 0)
            {
                RaycastHit h = s_Hits[best];
                Vector3 point = h.distance > 0f ? h.point : pos + m_Dir * bestDist;
                Vector3 normal = h.normal.sqrMagnitude > 0.001f ? h.normal : -m_Dir;
                transform.position = point;
                // damage and impact effects happen on the same frame as the hit
                s_SplashStamp++;
                if (bestTank != null)
                {
                    bestTank.SplashStamp = s_SplashStamp;
                    bestTank.TakeDamage(m_Damage, point, m_Dir, m_Owner);
                    if (m_Owner.Mods.lifesteal > 0f) m_Owner.Heal(Mathf.CeilToInt(m_Damage * m_Owner.Mods.lifesteal), true);
                }
                float radius = m_Weapon.splashRadius + m_Owner.Mods.splashBonus;
                if (radius > 0f) Splash(point, radius, Mathf.Max(m_Weapon.splashDamageFactor, m_Owner.Mods.splashBonus > 0f ? 0.35f : 0f));
                m_Fx.SpawnImpact(point, normal, bestTank != null);
                m_Fx.ReleaseProjectile(this);
                return;
            }

            transform.position = pos + m_Dir * step;
            m_Age += dt;
            if (m_Age >= m_Life) m_Fx.ReleaseProjectile(this);
        }

        void Splash(Vector3 point, float radius, float factor)
        {
            int n = Physics.OverlapSphereNonAlloc(point, radius, s_Splash, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                TankUnit t = s_Splash[i].GetComponentInParent<TankUnit>();
                if (t == null || t.IsDead || t.team == m_Owner.team || t.SplashStamp == s_SplashStamp) continue;
                t.SplashStamp = s_SplashStamp;
                Vector3 closest = s_Splash[i].ClosestPoint(point);
                float falloff = 1f - Mathf.Clamp01(Vector3.Distance(point, closest) / radius);
                int dmg = Mathf.RoundToInt(m_Damage * factor * falloff);
                if (dmg > 0) t.TakeDamage(dmg, closest, (t.transform.position - point).normalized, m_Owner);
            }
        }
    }
}
