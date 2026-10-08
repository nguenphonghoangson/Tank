using System.Collections.Generic;
using Tank.Core.Combat;
using Tank.Core.Common;
using Tank.Core.Hit;
using Tank.Core.Input;
using Tank.Core.Items;
using UnityEngine;

namespace Tank.Gameplay.Input
{
    /// <summary>A simple opponent: close in on the nearest enemy, keep a fighting distance while circling, shoot when lined up with a clear line, dash now and then.</summary>
    public sealed class BotCommandSource : ICommandSource
    {
        const float PreferredDistance = 13f, FireRange = 32f, AimToleranceDegrees = 7f;

        readonly ITankRegistry m_Tanks;
        readonly IHitWorld m_World;
        readonly IClock m_Clock;
        readonly IRandom m_Random;
        readonly IItemQuery m_Items;
        readonly MoveCommand m_Move = new MoveCommand();
        readonly AimCommand m_Aim = new AimCommand();
        readonly FireCommand m_Fire = new FireCommand();
        readonly DashCommand m_Dash = new DashCommand();
        float m_StrafeSign = 1f, m_NextStrafeChange, m_NextDash, m_AimError;

        public BotCommandSource(ITankRegistry tanks, IHitWorld world, IClock clock, IRandom random, IItemQuery items)
        {
            m_Items = items; m_Tanks = tanks; m_World = world; m_Clock = clock; m_Random = random;
            m_StrafeSign = m_Random.Value() < 0.5f ? -1f : 1f;
        }

        public void Collect(TankModel tank, List<ITankCommand> into)
        {
            TankModel target = Nearest(tank);
            bool hurt = tank.Health.Current < tank.Health.Max * 0.5f;
            if (target == null)
            {
                Vector2? item = NearestItem(tank.Position, 60f);
                m_Move.Direction = item.HasValue ? (item.Value - tank.Position).normalized : -tank.Position.normalized * 0.5f;      // nobody to fight: fetch an item, else drift back to the middle
                into.Add(m_Move);
                return;
            }

            if (m_Clock.Now >= m_NextStrafeChange)
            {
                m_NextStrafeChange = m_Clock.Now + m_Random.Range(1.2f, 3.5f);
                if (m_Random.Value() < 0.5f) m_StrafeSign = -m_StrafeSign;
                m_AimError = m_Random.Range(-4f, 4f);
            }

            Vector2 to = target.Position - tank.Position;
            float dist = to.magnitude;
            Vector2 forward = dist > 1e-3f ? to / dist : Vector2.up;
            Vector2 side = new Vector2(forward.y, -forward.x) * m_StrafeSign;
            Vector2 move = dist > PreferredDistance + 3f ? forward : dist < PreferredDistance - 4f ? -forward : side;
            move = (move + side * 0.4f).normalized;
            if (tank.Position.magnitude > 38f) move = (move + (-tank.Position.normalized) * 1.5f).normalized;     // stay off the rim
            // out of range of the fight, or hurt: go and get an item instead
            if (dist > PreferredDistance + 6f || hurt)
            {
                Vector2? item = NearestItem(tank.Position, 45f);
                if (item.HasValue) move = (item.Value - tank.Position).normalized;
            }
            m_Move.Direction = move;
            into.Add(m_Move);

            float aim = Mathf.Atan2(to.x, to.y) * Mathf.Rad2Deg + m_AimError;
            m_Aim.Yaw = aim;
            into.Add(m_Aim);

            bool lined = Mathf.Abs(Mathf.DeltaAngle(tank.TurretYaw, aim)) < AimToleranceDegrees;
            bool clear = m_World.Raycast(tank.Position, forward, dist) >= dist - 0.1f;       // no cover between the two
            if (dist < FireRange && lined && clear) into.Add(m_Fire);

            if (dist < 9f && m_Clock.Now >= m_NextDash) { m_NextDash = m_Clock.Now + m_Random.Range(5f, 9f); into.Add(m_Dash); }
        }

        Vector2? NearestItem(Vector2 from, float range)
        {
            Vector2? best = null; float bestD = range * range;
            IReadOnlyList<ItemSlotState> slots = m_Items.Slots;
            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i].ItemId < 0) continue;
                float d = (slots[i].Position - from).sqrMagnitude;
                if (d < bestD) { bestD = d; best = slots[i].Position; }
            }
            return best;
        }

        TankModel Nearest(TankModel self)
        {
            TankModel best = null; float bestD = float.MaxValue;
            for (int i = 0; i < m_Tanks.All.Count; i++)
            {
                TankModel t = m_Tanks.All[i];
                if (t.Id == self.Id || !t.IsAlive) continue;
                float d = (t.Position - self.Position).sqrMagnitude;
                if (d < bestD) { bestD = d; best = t; }
            }
            return best;
        }
    }
}
