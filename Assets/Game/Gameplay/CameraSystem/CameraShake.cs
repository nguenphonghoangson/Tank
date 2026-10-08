using System;
using System.Collections.Generic;
using Tank.Core.Combat;
using Tank.Core.Events;
using UnityEngine;

namespace Tank.Gameplay.CameraSystem
{
    /// <summary>What anything else may ask of the camera. The game depends on this, not on Cinemachine.</summary>
    public interface ICameraShaker { void Shake(float strength); }

    /// <summary>Decides when the camera shakes: hits on the local tank, and explosions near it, weaker the further away they are.</summary>
    public sealed class CameraShakePresenter : IDisposable
    {
        const float HearingRange = 25f;

        readonly ICameraShaker m_Shaker;
        readonly ILocalPlayer m_Local;
        readonly ITankRegistry m_Tanks;
        readonly List<IDisposable> m_Subscriptions = new List<IDisposable>();

        public CameraShakePresenter(IEventBus bus, ICameraShaker shaker, ILocalPlayer local, ITankRegistry tanks)
        {
            m_Shaker = shaker; m_Local = local; m_Tanks = tanks;
            m_Subscriptions.Add(bus.Subscribe<ProjectileImpact>(e => ShakeAt(e.Point, e.HitTank ? 0.12f : 0.08f)));
            m_Subscriptions.Add(bus.Subscribe<TankDamaged>(e => { if (e.VictimId == m_Local.TankId) m_Shaker.Shake(0.18f); }));
            m_Subscriptions.Add(bus.Subscribe<TankKilled>(e =>
            {
                if (e.VictimId == m_Local.TankId) { m_Shaker.Shake(0.45f); return; }
                TankModel victim = m_Tanks.Find(e.VictimId);
                if (victim != null) ShakeAt(victim.Position, 0.25f);
            }));
        }

        void ShakeAt(Vector2 point, float strength)
        {
            TankModel me = m_Tanks.Find(m_Local.TankId);
            if (me == null) return;
            float d = (me.Position - point).magnitude;
            if (d < HearingRange) m_Shaker.Shake(strength * (1f - d / HearingRange));
        }

        public void Dispose() { foreach (IDisposable s in m_Subscriptions) s.Dispose(); m_Subscriptions.Clear(); }
    }
}
