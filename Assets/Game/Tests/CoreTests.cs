using System.Collections.Generic;
using NUnit.Framework;
using Tank.Core.Combat;
using Tank.Core.Common;
using Tank.Core.Events;
using Tank.Core.Hit;
using Tank.Core.Match;
using Tank.Core.Movement;
using UnityEngine;

namespace Tank.Tests
{
    public sealed class EventBusTests
    {
        struct Ping { public int Value; }

        [Test]
        public void DeliversToSubscribersAndStopsAfterDispose()
        {
            var bus = new EventBus(); int sum = 0;
            var sub = bus.Subscribe<Ping>(e => sum += e.Value);
            bus.Publish(new Ping { Value = 2 }); bus.Publish(new Ping { Value = 3 });
            sub.Dispose();
            bus.Publish(new Ping { Value = 100 });
            Assert.AreEqual(5, sum);
        }

        [Test]
        public void HandlerMayUnsubscribeWhileDelivering()
        {
            var bus = new EventBus(); int calls = 0; System.IDisposable self = null;
            self = bus.Subscribe<Ping>(e => { calls++; self.Dispose(); });
            bus.Subscribe<Ping>(e => calls += 10);
            bus.Publish(new Ping()); bus.Publish(new Ping());
            Assert.AreEqual(21, calls);
        }
    }

    public sealed class MovementTests
    {
        static readonly MovementSettings Cfg = new MovementSettings(13f, 100f, 7f, 15f, 1f);     // the values of the reference character controller

        [Test]
        public void ReachesTopSpeedAndNeverExceedsIt()
        {
            var s = new KinematicState(); var m = new DirectDriveMovement(); float max = 0f;
            for (int i = 0; i < 60; i++) { m.Step(ref s, Vector2.right, 1f, Cfg, 1f / 30f); max = Mathf.Max(max, s.Speed); }
            Assert.AreEqual(13f, s.Speed, 0.01f);
            Assert.LessOrEqual(max, 13.001f);
        }

        [Test]
        public void ReleasingTheStickBrakesSmoothlyToAStop()
        {
            var s = new KinematicState { Velocity = new Vector2(13f, 0f) }; var m = new DirectDriveMovement();
            m.Step(ref s, Vector2.zero, 1f, Cfg, 1f / 30f);
            Assert.Greater(s.Speed, 8f);                       // one tick later it has not stopped dead
            for (int i = 0; i < 60; i++) m.Step(ref s, Vector2.zero, 1f, Cfg, 1f / 30f);
            Assert.Less(s.Speed, 0.2f);
        }

        [Test]
        public void HullTurnsTowardTheTravelDirection()
        {
            var s = new KinematicState { Yaw = 0f }; var m = new DirectDriveMovement();
            for (int i = 0; i < 20; i++) m.Step(ref s, Vector2.right, 1f, Cfg, 1f / 30f);       // +x is yaw 90
            Assert.AreEqual(90f, s.Yaw, 1f);
        }
    }

    public sealed class HitWorldTests
    {
        [Test]
        public void EllipseKeepsACircleInsideAndRemovesOutwardVelocity()
        {
            var shape = new EllipseBoundary(50f, 30f);
            Vector2 p = new Vector2(70f, 0f), v = new Vector2(10f, 4f);
            shape.PushOut(ref p, ref v, 1f);
            Assert.AreEqual(49f, p.x, 0.01f);
            Assert.LessOrEqual(v.x, 0.001f);
            Assert.AreEqual(4f, v.y, 0.01f);                    // the tangential part survives: a slide
        }

        [Test]
        public void EllipseRaycastFindsTheRim()
        {
            var shape = new EllipseBoundary(50f, 30f);
            Assert.AreEqual(50f, shape.Raycast(Vector2.zero, Vector2.right, 200f), 0.01f);
            Assert.AreEqual(30f, shape.Raycast(Vector2.zero, Vector2.up, 200f), 0.01f);
        }

        [Test]
        public void ObbPushesACircleOutAndRotates()
        {
            var box = new ObbShape(new Vector2(10f, 0f), new Vector2(2f, 4f), 0f);
            Vector2 p = new Vector2(7.5f, 0f), v = new Vector2(5f, 0f);       // 0.5 from the left face, radius 1 overlaps
            box.PushOut(ref p, ref v, 1f);
            Assert.AreEqual(7f, p.x, 0.01f);
            Assert.LessOrEqual(v.x, 0.001f);

            var turned = new ObbShape(Vector2.zero, new Vector2(1f, 5f), 90f);   // the long half-extent (5) now lies along world x: the box spans x -5..5
            Assert.AreEqual(15f, turned.Raycast(new Vector2(-20f, 0f), Vector2.right, 100f), 0.01f);
        }

        [Test]
        public void ObbRayStartingInsideEndsAtOnce()
        {
            var box = new ObbShape(Vector2.zero, new Vector2(2f, 2f), 0f);
            Assert.AreEqual(0f, box.Raycast(Vector2.zero, Vector2.right, 50f), 1e-4f);
        }

        [Test]
        public void TankDrivingIntoAWallAtAnAngleSlidesAlongIt()
        {
            var world = new HitWorld(new IHitShape[] { new EllipseBoundary(60f, 40f) });
            var s = new KinematicState { Position = new Vector2(58.5f, 0f), Velocity = Vector2.zero };
            var cfg = new MovementSettings(13f, 100f, 7f, 15f, 1f); var m = new DirectDriveMovement();
            Vector2 start = s.Position;
            for (int i = 0; i < 60; i++)
            {
                m.Step(ref s, new Vector2(1f, 1f).normalized, 1f, cfg, 1f / 30f);        // 45 degrees into the rim
                world.ResolveCircle(ref s.Position, ref s.Velocity, cfg.Radius);
            }
            Assert.Greater(s.Position.y - start.y, 10f);         // it travelled along the wall instead of stalling against it
            Assert.Greater(s.Speed, 5f);
        }
    }

    public sealed class CombatTests
    {
        static readonly TankSettings TS = new TankSettings(100, 720f, 28f, 0.22f, 4f, 1f, 2f, 3f, 1.4f, 1.5f);

        sealed class Fixture
        {
            public readonly EventBus Bus = new EventBus();
            public readonly ManualClock Clock = new ManualClock();
            public readonly TankRegistry Tanks = new TankRegistry();
            public readonly CombatService Combat;
            public Fixture() { Combat = new CombatService(Tanks, Bus, Clock); }
            public TankModel Add(int id, Vector2 at, int hp = 100) { var t = new TankModel(id, "P" + id, id % 4, hp); t.Body.Position = at; Tanks.Add(t); return t; }
        }

        [Test]
        public void KillReportsKillerAndRecentAssistersOnly()
        {
            var f = new Fixture(); f.Add(1, Vector2.zero); f.Add(2, Vector2.zero); f.Add(3, Vector2.zero); f.Add(4, Vector2.zero);
            TankKilled? killed = null; f.Bus.Subscribe<TankKilled>(e => killed = e);
            f.Combat.ApplyDamage(1, 3, 10);                 // old hit, will fall out of the window
            f.Clock.Advance(CombatService.AssistWindowSeconds + 1f);
            f.Combat.ApplyDamage(1, 2, 10);                 // recent hit: assist
            f.Combat.ApplyDamage(1, 4, 100);                // the kill
            Assert.IsTrue(killed.HasValue);
            Assert.AreEqual(4, killed.Value.KillerId);
            CollectionAssert.AreEquivalent(new[] { 2 }, killed.Value.AssistIds);
        }

        [Test]
        public void DeadTanksTakeNoFurtherDamage()
        {
            var f = new Fixture(); f.Add(1, Vector2.zero, 10); f.Add(2, Vector2.zero);
            int kills = 0; f.Bus.Subscribe<TankKilled>(e => kills++);
            f.Combat.ApplyDamage(1, 2, 50); f.Combat.ApplyDamage(1, 2, 50);
            Assert.AreEqual(1, kills);
        }

        [Test]
        public void ProjectileHitsTheFirstTankAndNeverItsOwner()
        {
            var f = new Fixture(); var shooter = f.Add(1, Vector2.zero); var target = f.Add(2, new Vector2(20f, 0f));
            var world = new HitWorld(new IHitShape[] { new EllipseBoundary(100f, 100f) });
            var sys = new ProjectileSystem(world, f.Tanks, f.Combat, f.Bus, TS);
            sys.Spawn(1, new WeaponSpec(0, "cannon", 25, 0.3f, 60f, 70f, 1, 0f, 0, 0f, 0f), new Vector2(1f, 0f), Vector2.right, ShotModifiers.None);
            for (int i = 0; i < 30; i++) sys.Tick(1f / 30f);
            Assert.AreEqual(75, target.Health.Current);
            Assert.AreEqual(100, shooter.Health.Current);
            Assert.AreEqual(0, sys.Active.Count);
        }

        [Test]
        public void ProjectileStopsAtCover()
        {
            var f = new Fixture(); f.Add(1, Vector2.zero); var target = f.Add(2, new Vector2(30f, 0f));
            var world = new HitWorld(new IHitShape[] { new EllipseBoundary(100f, 100f), new ObbShape(new Vector2(15f, 0f), new Vector2(1f, 3f), 0f) });
            var sys = new ProjectileSystem(world, f.Tanks, f.Combat, f.Bus, TS);
            bool impactOnCover = false; f.Bus.Subscribe<ProjectileImpact>(e => impactOnCover = !e.HitTank && Mathf.Abs(e.Point.x - 14f) < 0.5f);
            sys.Spawn(1, new WeaponSpec(0, "cannon", 25, 0.3f, 60f, 70f, 1, 0f, 0, 0f, 0f), new Vector2(1f, 0f), Vector2.right, ShotModifiers.None);
            for (int i = 0; i < 30; i++) sys.Tick(1f / 30f);
            Assert.IsTrue(impactOnCover);
            Assert.AreEqual(100, target.Health.Current);
        }

        [Test]
        public void SplashHurtsNearbyTanksButNotTheShooter()
        {
            var f = new Fixture(); var shooter = f.Add(1, Vector2.zero); var direct = f.Add(2, new Vector2(20f, 0f)); var near = f.Add(3, new Vector2(20f, 3f));
            var world = new HitWorld(new IHitShape[] { new EllipseBoundary(100f, 100f) });
            var sys = new ProjectileSystem(world, f.Tanks, f.Combat, f.Bus, TS);
            sys.Spawn(1, new WeaponSpec(3, "rocket", 50, 1f, 60f, 70f, 1, 0f, 0, 5f, 0.5f), new Vector2(1f, 0f), Vector2.right, ShotModifiers.None);
            for (int i = 0; i < 30; i++) sys.Tick(1f / 30f);
            Assert.AreEqual(50, direct.Health.Current);
            Assert.AreEqual(75, near.Health.Current);
            Assert.AreEqual(100, shooter.Health.Current);
        }

        [Test]
        public void SpecialWeaponReturnsToTheDefaultWhenItsAmmoRunsOut()
        {
            var f = new Fixture(); var t = f.Add(1, Vector2.zero);
            var catalog = new ArrayWeaponCatalog(new[]
            {
                new WeaponSpec(0, "cannon", 25, 0.3f, 60f, 70f, 1, 0f, 0, 0f, 0f),
                new WeaponSpec(1, "mg", 8, 0.1f, 70f, 48f, 1, 0f, 2, 0f, 0f),
            });
            var world = new HitWorld(); var sys = new ProjectileSystem(world, f.Tanks, f.Combat, f.Bus, TS);
            var weapons = new WeaponSystem(catalog, sys, new SystemRandom(1), TS);
            weapons.Equip(t, 1); t.Intent.Fire = true;
            for (int i = 0; i < 10; i++) weapons.Step(t, 0.1f);
            Assert.AreEqual(0, t.Weapon.WeaponId);
            Assert.AreEqual(0, t.Weapon.Ammo);
        }
    }

    public sealed class MatchTests
    {
        [Test]
        public void KdaDecidesTheRankingAndTheWinner()
        {
            var bus = new EventBus(); var ledger = new KdaLedger(new KdaRankingPolicy(), bus);
            foreach (int id in new[] { 1, 2, 3 }) ledger.Register(id);
            bus.Publish(new TankKilled(2, 1, new[] { 3 }));     // 1: K1, 2: D1, 3: A1
            bus.Publish(new TankKilled(3, 1, new int[0]));      // 1: K2, 3: D1
            Assert.AreEqual(1, ledger.Ranked[0].Id);
            Assert.AreEqual(1, ledger.RankOf(1));
            Assert.IsTrue(ledger.TryGetWinner(out int winner)); Assert.AreEqual(1, winner);
            Assert.AreEqual(2f, ledger.Get(1).Kda, 1e-4f);      // two kills, no deaths: deaths floored at 1
        }

        [Test]
        public void ATieOnEverythingIsNoWinner()
        {
            var bus = new EventBus(); var ledger = new KdaLedger(new KdaRankingPolicy(), bus);
            ledger.Register(1); ledger.Register(2);
            bus.Publish(new TankKilled(2, 1, new int[0])); bus.Publish(new TankKilled(1, 2, new int[0]));
            Assert.IsFalse(ledger.TryGetWinner(out int _));
        }

        [Test]
        public void SessionRunsWarmupPlayingEndedAndNamesTheWinner()
        {
            var bus = new EventBus(); var ledger = new KdaLedger(new KdaRankingPolicy(), bus);
            ledger.Register(1); ledger.Register(2);
            var session = new MatchSession(new MatchSettings(3f, 60f), ledger, bus);
            var phases = new List<MatchPhase>(); bus.Subscribe<MatchPhaseChanged>(e => phases.Add(e.Phase));
            int ended = -2; bus.Subscribe<MatchEnded>(e => ended = e.WinnerId);

            session.StartWarmup();
            for (int i = 0; i < 100; i++) session.Tick(0.05f);                 // 5 s: warmup over, playing
            Assert.AreEqual(MatchPhase.Playing, session.Phase);
            bus.Publish(new TankKilled(2, 1, new int[0]));
            for (int i = 0; i < 1300; i++) session.Tick(0.05f);                // past the 60 s
            Assert.AreEqual(MatchPhase.Ended, session.Phase);
            CollectionAssert.AreEqual(new[] { MatchPhase.Warmup, MatchPhase.Playing, MatchPhase.Ended }, phases);
            Assert.AreEqual(1, ended);
            Assert.AreEqual(1, session.WinnerId);
        }
    }
}
