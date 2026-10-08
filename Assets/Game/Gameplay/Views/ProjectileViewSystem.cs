using System.Collections.Generic;
using Reflex.Attributes;
using Tank.Core.Combat;
using Tank.Core.Events;
using Tank.Gameplay.Config;
using Tank.Gameplay.Fx;
using UnityEngine;

namespace Tank.Gameplay.Views
{
    /// <summary>Shows every projectile in flight with the weapon's own prefab, from a pool, positioned from the simulation and nudged forward by the frame's share of a tick.</summary>
    public sealed class ProjectileViewSystem : MonoBehaviour
    {
        readonly Dictionary<int, GameObject> m_Views = new Dictionary<int, GameObject>();
        ObjectPool m_Pool; WeaponConfig m_Weapons; IProjectileQuery m_Query; IRenderClock m_Clock;

        [Inject]
        void Construct(IEventBus bus, WeaponConfig weapons, IProjectileQuery query, IRenderClock clock)
        {
            m_Pool = new ObjectPool(transform); m_Weapons = weapons; m_Query = query; m_Clock = clock;
            bus.Subscribe<ProjectileFired>(OnFired);
            bus.Subscribe<ProjectileImpact>(OnImpact);
        }

        void OnFired(ProjectileFired e)
        {
            GameObject go = m_Pool.Get(m_Weapons.Presentation(e.WeaponId).projectilePrefab, new Vector3(e.Origin.x, 0.9f, e.Origin.y), Quaternion.LookRotation(new Vector3(e.Direction.x, 0f, e.Direction.y)));
            if (go != null) m_Views[e.ProjectileId] = go;
        }

        void OnImpact(ProjectileImpact e)
        {
            if (m_Views.TryGetValue(e.ProjectileId, out GameObject go)) { m_Pool.Release(go); m_Views.Remove(e.ProjectileId); }
        }

        void LateUpdate()
        {
            if (m_Clock == null) return;
            float ahead = m_Clock.Alpha * m_Clock.Step;
            IReadOnlyList<Projectile> active = m_Query.Active;
            for (int i = 0; i < active.Count; i++)
            {
                Projectile p = active[i];
                if (!m_Views.TryGetValue(p.Id, out GameObject go)) continue;
                Vector2 pos = p.Position + p.Direction * (p.Speed * ahead);
                go.transform.position = new Vector3(pos.x, 0.9f, pos.y);
            }
        }
    }
}
