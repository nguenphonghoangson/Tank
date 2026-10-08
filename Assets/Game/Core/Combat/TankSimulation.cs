using Tank.Core.Events;
using Tank.Core.Hit;
using Tank.Core.Movement;
using UnityEngine;

namespace Tank.Core.Combat
{
    /// <summary>Advances one tank by one tick: dash, drive, collide, aim. Pure rules, driven by the intent the input layer left on the model.</summary>
    public sealed class TankSimulation
    {
        readonly IMovementModel m_Movement;
        readonly IHitWorld m_World;
        readonly MovementSettings m_MoveSettings;
        readonly TankSettings m_Tank;
        readonly IEventBus m_Bus;

        public TankSimulation(IMovementModel movement, IHitWorld world, MovementSettings moveSettings, TankSettings tank, IEventBus bus)
        {
            m_Movement = movement; m_World = world; m_MoveSettings = moveSettings; m_Tank = tank; m_Bus = bus;
        }

        public void Step(TankModel t, float dt)
        {
            if (!t.IsAlive) return;
            TankIntent intent = t.Intent;

            DashState dash = t.Dash;
            dash.Cooldown = Mathf.Max(0f, dash.Cooldown - dt);
            if (intent.Dash && dash.Cooldown <= 0f && dash.Time <= 0f)
            {
                dash.Yaw = intent.Move.sqrMagnitude > 0.0025f ? Mathf.Atan2(intent.Move.x, intent.Move.y) * Mathf.Rad2Deg : t.Body.Yaw;
                dash.Time = m_Tank.DashDuration;
                dash.Cooldown = m_Tank.DashCooldown * t.Mods.DashCooldownMult;
                m_Bus.Publish(new TankDashed(t.Id, t.Body.Position, new Vector2(Mathf.Sin(dash.Yaw * Mathf.Deg2Rad), Mathf.Cos(dash.Yaw * Mathf.Deg2Rad))));
            }

            KinematicState body = t.Body;
            if (dash.Time > 0f)
            {
                var dir = new Vector2(Mathf.Sin(dash.Yaw * Mathf.Deg2Rad), Mathf.Cos(dash.Yaw * Mathf.Deg2Rad));
                body.Position += dir * (m_Tank.DashSpeed * dt);
                body.Velocity = dir * m_MoveSettings.MaxSpeed;           // leave the dash running, not stopped
                dash.Time = Mathf.Max(0f, dash.Time - dt);
            }
            else
            {
                float speedMult = (t.Status.SpeedTime > 0f ? m_Tank.SpeedBuffMultiplier : 1f) * t.Mods.SpeedMult;
                m_Movement.Step(ref body, intent.Move, speedMult, m_MoveSettings, dt);
            }
            t.Status.SpeedTime = Mathf.Max(0f, t.Status.SpeedTime - dt);

            m_World.ResolveCircle(ref body.Position, ref body.Velocity, m_MoveSettings.Radius);
            t.Body = body;
            t.Dash = dash;
            t.TurretYaw = Mathf.MoveTowardsAngle(t.TurretYaw, intent.AimYaw, m_Tank.TurretTurnSpeed * dt);
        }
    }
}
