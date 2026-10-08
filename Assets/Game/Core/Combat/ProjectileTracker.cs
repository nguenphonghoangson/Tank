using System.Collections.Generic;
using Tank.Core.Common;
using Tank.Core.Events;
using UnityEngine;

namespace Tank.Core.Combat
{
    /// <summary>
    /// Follows projectiles from the events about them, so the picture of "what is flying" does not depend on who ran the rules:
    /// the server, an offline game and a remote client all see the same ProjectileFired / ProjectileImpact and get the same answer.
    /// It advances shots only to draw them; damage was already decided by the ProjectileSystem on the authority.
    /// </summary>
    public sealed class ProjectileTracker : IProjectileQuery, ITickable
    {
        readonly IWeaponCatalog m_Catalog;
        readonly List<Projectile> m_Active = new List<Projectile>();

        public ProjectileTracker(IEventBus bus, IWeaponCatalog catalog)
        {
            m_Catalog = catalog;
            bus.Subscribe<ProjectileFired>(OnFired);
            bus.Subscribe<ProjectileImpact>(OnImpact);
        }

        public IReadOnlyList<Projectile> Active => m_Active;
        public void Clear() { m_Active.Clear(); }

        void OnFired(ProjectileFired e)
        {
            WeaponSpec spec = m_Catalog.Get(e.WeaponId);
            m_Active.Add(new Projectile { Id = e.ProjectileId, OwnerId = e.OwnerId, WeaponId = e.WeaponId, Position = e.Origin, Direction = e.Direction, Speed = spec.ProjectileSpeed, Remaining = spec.Range });
        }

        void OnImpact(ProjectileImpact e)
        {
            for (int i = m_Active.Count - 1; i >= 0; i--) if (m_Active[i].Id == e.ProjectileId) { m_Active.RemoveAt(i); return; }
        }

        public void Tick(float dt)
        {
            for (int i = m_Active.Count - 1; i >= 0; i--)
            {
                Projectile p = m_Active[i];
                float step = Mathf.Min(p.Speed * dt, p.Remaining);
                p.Position += p.Direction * step;
                p.Remaining -= step;
                if (p.Remaining <= 1e-4f) m_Active.RemoveAt(i);        // out of range with no impact reported (a lost datagram): stop drawing it
            }
        }
    }
}
