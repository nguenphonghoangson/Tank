using System;
using System.Collections.Generic;
using Tank.Core.Events;
using Tank.Core.Match;
using Tank.Gameplay.Config;
using Tank.Core.Combat;
using UnityEngine;

namespace Tank.Gameplay.Fx
{
    /// <summary>
    /// Listens to what happened (shots, hits, deaths, spawns, match phases) and asks the FX service for the matching effect and sound.
    /// The rules never call into it, so removing all effects is deleting this class and its registration.
    /// </summary>
    public sealed class GameFxPresenter : IDisposable
    {
        const float MuzzleHeight = 0.9f;

        readonly IFxService m_Fx;
        readonly WeaponConfig m_Weapons;
        readonly FxConfig m_Config;
        readonly ITankRegistry m_Tanks;
        readonly ItemConfig m_Items;
        readonly ILocalPlayer m_Local;
        readonly List<IDisposable> m_Subscriptions = new List<IDisposable>();

        public GameFxPresenter(IEventBus bus, IFxService fx, WeaponConfig weapons, FxConfig config, ITankRegistry tanks, ILocalPlayer local, ItemConfig items)
        {
            m_Items = items; m_Fx = fx; m_Weapons = weapons; m_Config = config; m_Tanks = tanks; m_Local = local;
            m_Subscriptions.Add(bus.Subscribe<ProjectileFired>(OnFired));
            m_Subscriptions.Add(bus.Subscribe<ProjectileImpact>(OnImpact));
            m_Subscriptions.Add(bus.Subscribe<TankDamaged>(OnDamaged));
            m_Subscriptions.Add(bus.Subscribe<TankKilled>(OnKilled));
            m_Subscriptions.Add(bus.Subscribe<TankSpawned>(e => OnSpawn(e.TankId)));
            m_Subscriptions.Add(bus.Subscribe<TankRespawned>(e => OnSpawn(e.TankId)));
            m_Subscriptions.Add(bus.Subscribe<TankDashed>(OnDashed));
            m_Subscriptions.Add(bus.Subscribe<ItemTaken>(OnItemTaken));
            m_Subscriptions.Add(bus.Subscribe<CorePicked>(OnCorePicked));
            m_Subscriptions.Add(bus.Subscribe<CoreOffered>(e => { if (e.TankId == m_Local.TankId) m_Fx.PlaySfx2D(m_Config.coreOfferedSfx); }));
            m_Subscriptions.Add(bus.Subscribe<MatchPhaseChanged>(OnPhase));
        }

        static Vector3 World(Vector2 p, float y = 0f) { return new Vector3(p.x, y, p.y); }
        static Quaternion Facing(Vector2 dir) { return dir.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(new Vector3(dir.x, 0f, dir.y)) : Quaternion.identity; }

        void OnFired(ProjectileFired e)
        {
            WeaponConfig.Entry w = m_Weapons.Presentation(e.WeaponId);
            Vector3 at = World(e.Origin, MuzzleHeight);
            m_Fx.PlayVfx(w.muzzleVfx, at, Facing(e.Direction));
            m_Fx.PlaySfx(w.shootSfx, at);
        }

        void OnImpact(ProjectileImpact e)
        {
            WeaponConfig.Entry w = m_Weapons.Presentation(e.WeaponId);
            Vector3 at = World(e.Point, 0.6f);
            m_Fx.PlayVfx(w.impactVfx, at, Quaternion.identity);
            m_Fx.PlaySfx(e.HitTank ? m_Config.tankHitSfx : w.impactSfx, at);
        }

        void OnDamaged(TankDamaged e)
        {
            TankModel victim = m_Tanks.Find(e.VictimId);
            if (victim == null || e.HpLeft <= 0) return;                        // a kill has its own effect
            m_Fx.PlayVfx(m_Config.tankHitVfx, World(victim.Position, 0.8f), Quaternion.identity);
        }

        void OnKilled(TankKilled e)
        {
            TankModel victim = m_Tanks.Find(e.VictimId);
            if (victim != null)
            {
                Vector3 at = World(victim.Position, 0.6f);
                m_Fx.PlayVfx(m_Config.tankExplosionVfx, at, Quaternion.identity);
                m_Fx.PlayVfx(m_Config.tankDebrisVfx, at, Quaternion.identity);
                m_Fx.PlaySfx(m_Config.tankExplosionSfx, at);
            }
            if (e.KillerId == m_Local.TankId && e.VictimId != e.KillerId) m_Fx.PlaySfx2D(m_Config.scoreChangeSfx);
        }

        void OnSpawn(int tankId)
        {
            TankModel t = m_Tanks.Find(tankId);
            if (t == null) return;
            Vector3 at = World(t.Position);
            m_Fx.PlayVfx(m_Config.spawnVfx, at, Quaternion.identity);
            m_Fx.PlaySfx(m_Config.spawnSfx, at);
            if (tankId == m_Local.TankId) m_Fx.PlaySfx2D(m_Config.readySfx);
        }

        void OnItemTaken(ItemTaken e)
        {
            ItemConfig.Entry item = m_Items.Presentation(e.ItemId);
            Vector3 at = World(e.Position, 0.8f);
            m_Fx.PlayVfx(item.pickupVfx, at, Quaternion.identity);
            if (e.TankId == m_Local.TankId) m_Fx.PlaySfx2D(item.pickupSfx); else m_Fx.PlaySfx(item.pickupSfx, at);
        }

        void OnCorePicked(CorePicked e)
        {
            TankModel t = m_Tanks.Find(e.TankId);
            if (t != null) m_Fx.PlayVfx(m_Config.corePickedVfx, World(t.Position, 0.5f), Quaternion.identity);
            if (e.TankId == m_Local.TankId) m_Fx.PlaySfx2D(m_Config.corePickedSfx);
        }

        void OnDashed(TankDashed e) { m_Fx.PlayVfx(m_Config.dashVfx, World(e.Position, 0.2f), Facing(e.Direction)); }

        void OnPhase(MatchPhaseChanged e)
        {
            if (e.Phase == MatchPhase.Warmup) m_Fx.PlaySfx2D(m_Config.countdownSfx);
            else if (e.Phase == MatchPhase.Ended) m_Fx.PlaySfx2D(m_Config.celebrationSfx);
        }

        public void Dispose() { foreach (IDisposable s in m_Subscriptions) s.Dispose(); m_Subscriptions.Clear(); }
    }
}
