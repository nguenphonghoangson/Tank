using System.Collections.Generic;
using Tank.Core.Events;
using UnityEngine;

namespace Tank.Core.Combat
{
    public interface ISpawnPolicy
    {
        Vector2 Pick(IReadOnlyList<TankModel> tanks, int forTankId);
    }

    /// <summary>Picks the spawn point whose nearest living enemy is furthest away.</summary>
    public sealed class FarthestSpawnPolicy : ISpawnPolicy
    {
        readonly Vector2[] m_Points;
        public FarthestSpawnPolicy(Vector2[] points) { m_Points = points; }

        public Vector2 Pick(IReadOnlyList<TankModel> tanks, int forTankId)
        {
            Vector2 best = m_Points.Length > 0 ? m_Points[0] : Vector2.zero;
            float bestScore = -1f;
            foreach (Vector2 p in m_Points)
            {
                float nearest = float.MaxValue;
                for (int i = 0; i < tanks.Count; i++)
                    if (tanks[i].Id != forTankId && tanks[i].IsAlive) nearest = Mathf.Min(nearest, (tanks[i].Position - p).magnitude);
                if (nearest > bestScore) { bestScore = nearest; best = p; }
            }
            return best;
        }
    }

    /// <summary>Puts a tank into the arena (first time or after a death) in a clean state.</summary>
    public sealed class TankLifecycle
    {
        readonly ITankRegistry m_Tanks;
        readonly ISpawnPolicy m_Spawns;
        readonly IEventBus m_Bus;

        public TankLifecycle(ITankRegistry tanks, ISpawnPolicy spawns, IEventBus bus) { m_Tanks = tanks; m_Spawns = spawns; m_Bus = bus; }

        public void Spawn(TankModel t) { Place(t); m_Bus.Publish(new TankSpawned(t.Id)); }
        public void Respawn(TankModel t) { Place(t); m_Bus.Publish(new TankRespawned(t.Id)); }

        void Place(TankModel t)
        {
            Vector2 p = m_Spawns.Pick(m_Tanks.All, t.Id);
            t.Health.Restore();
            t.Weapon.Reset();
            t.Dash = default;
            t.Status = default;
            t.Intent = default;
            t.Body = new Movement.KinematicState { Position = p, Velocity = Vector2.zero, Yaw = Mathf.Atan2(-p.x, -p.y) * Mathf.Rad2Deg };     // facing the middle
            t.TurretYaw = t.Body.Yaw;
            t.Intent.AimYaw = t.TurretYaw;
        }
    }
}
