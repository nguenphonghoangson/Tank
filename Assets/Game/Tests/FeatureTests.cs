using NUnit.Framework;
using Tank.Core.Combat;
using Tank.Core.Common;
using Tank.Core.Cores;
using Tank.Core.Events;
using Tank.Core.Hit;
using Tank.Core.Items;
using Tank.Core.Match;
using Tank.Core.Movement;
using UnityEngine;

namespace Tank.Tests
{
    sealed class World
    {
        public readonly EventBus Bus = new EventBus();
        public readonly SimulationClock Clock = new SimulationClock();
        public readonly TankRegistry Tanks = new TankRegistry();
        public readonly CombatService Combat;
        public readonly TankSettings Settings = new TankSettings(100, 720f, 28f, 0.22f, 4f, 1f, 2f, 3f, 1.4f, 1.5f);
        public readonly MovementSettings Move = new MovementSettings(13f, 100f, 7f, 15f, 1f);
        public World() { Combat = new CombatService(Tanks, Bus, Clock); }
        public TankModel Add(int id, Vector2 at, bool bot = false)
        {
            var t = new TankModel(id, "P" + id, id % 4, 100) { IsBot = bot };
            t.Body.Position = at; Tanks.Add(t); return t;
        }
        public void Advance(float seconds, ITickable system, float dt = 1f / 30f) { for (float t = 0f; t < seconds; t += dt) { Clock.Tick(dt); system.Tick(dt); } }
    }

    public sealed class ItemTests
    {
        static readonly IItemCatalog Catalog = new ArrayItemCatalog(new[]
        {
            new ItemSpec(0, "repair", ItemEffectKind.Repair, 40f, 0f, 0, 1f, 5f),
            new ItemSpec(1, "shield", ItemEffectKind.Shield, 50f, 12f, 0, 1f, 5f),
        });

        static ItemSystem Make(World w, IItemEffect[] effects, int forceItem)
        {
            // a random source that always rolls the wanted item
            return new ItemSystem(Catalog, new ItemSlotLayout(new[] { new Vector2(10f, 0f) }), w.Tanks, w.Bus, w.Clock, new FixedRandom(forceItem == 0 ? 0.1f : 0.9f), new ItemSettings(2f), effects);
        }

        sealed class FixedRandom : IRandom { readonly float m_V; public FixedRandom(float v) { m_V = v; } public float Value() { return m_V; } public float Range(float a, float b) { return a + (b - a) * m_V; } }

        [Test]
        public void ARepairKitIsLeftAloneByATankAtFullHealth()
        {
            var w = new World(); TankModel t = w.Add(1, new Vector2(10f, 0f));
            var items = Make(w, new IItemEffect[] { new RepairEffect(), new ShieldEffect() }, 0);
            w.Advance(1f, items);
            Assert.AreEqual(0, items.Slots[0].ItemId);          // there, and still there: nothing was wasted
        }

        [Test]
        public void ARepairKitHealsThenTheSlotRefillsAfterItsRespawnTime()
        {
            var w = new World(); TankModel t = w.Add(1, new Vector2(10f, 0f)); t.Health.Damage(60);
            var items = Make(w, new IItemEffect[] { new RepairEffect(), new ShieldEffect() }, 0);
            ItemTaken? taken = null; w.Bus.Subscribe<ItemTaken>(e => taken = e);
            w.Advance(0.5f, items);
            Assert.IsTrue(taken.HasValue);
            Assert.AreEqual(80, t.Health.Current);
            Assert.AreEqual(-1, items.Slots[0].ItemId);
            t.Body.Position = new Vector2(50f, 0f);
            w.Advance(8f, items);                                // respawn is 5 s +/- 20%
            Assert.AreEqual(0, items.Slots[0].ItemId);
        }

        [Test]
        public void AShieldSoaksDamageBeforeHealthIsTouched()
        {
            var w = new World(); TankModel t = w.Add(1, new Vector2(10f, 0f)); w.Add(2, new Vector2(40f, 0f));
            var items = Make(w, new IItemEffect[] { new RepairEffect(), new ShieldEffect() }, 1);
            w.Advance(0.5f, items);
            Assert.AreEqual(50, t.Status.Shield);
            w.Combat.ApplyDamage(1, 2, 30);
            Assert.AreEqual(100, t.Health.Current); Assert.AreEqual(20, t.Status.Shield);
            w.Combat.ApplyDamage(1, 2, 30);
            Assert.AreEqual(90, t.Health.Current); Assert.AreEqual(0, t.Status.Shield);
        }
    }

    public sealed class CoreTests
    {
        static readonly ICoreCatalog Catalog = new ArrayCoreCatalog(new[]
        {
            new CoreSpec(0, "plating", "", new CoreModifiers { MaxHpBonus = 40, SpeedMult = 0.92f, DamageMult = 1f, FireIntervalMult = 1f, AmmoMult = 1f, DashCooldownMult = 1f }),
            new CoreSpec(1, "overdrive", "", new CoreModifiers { SpeedMult = 1.18f, DamageMult = 1f, FireIntervalMult = 1f, AmmoMult = 1f, DashCooldownMult = 1f }),
            new CoreSpec(2, "heavy", "", new CoreModifiers { SpeedMult = 1f, DamageMult = 1.3f, FireIntervalMult = 1.15f, AmmoMult = 1f, DashCooldownMult = 1f }),
            new CoreSpec(3, "afterburner", "", new CoreModifiers { SpeedMult = 1f, DamageMult = 1f, FireIntervalMult = 1f, AmmoMult = 1f, DashCooldownMult = 0.55f }),
        });

        static CoreOfferSystem Make(World w, out MatchSession session, out KdaLedger ledger)
        {
            ledger = new KdaLedger(new KdaRankingPolicy(), w.Bus);
            session = new MatchSession(new MatchSettings(1f, 80f), ledger, w.Bus);
            return new CoreOfferSystem(Catalog, w.Tanks, w.Bus, w.Clock, new SystemRandom(7), new BotsPickAutomatically(), new MatchSettings(1f, 80f), new CoreSettings(4, 12f));
        }

        [Test]
        public void ModifiersOfSeveralCoresCombine()
        {
            CoreModifiers m = CoreMask.Combine(Catalog, (ushort)(0b0011));
            Assert.AreEqual(40, m.MaxHpBonus);
            Assert.AreEqual(0.92f * 1.18f, m.SpeedMult, 1e-4f);
        }

        [Test]
        public void ACoreThatRaisesMaxHealthAlsoFillsIt()
        {
            var t = new TankModel(1, "a", 0, 100);
            t.SetCores(CoreMask.With(0, 0), Catalog);
            Assert.AreEqual(140, t.Health.Max); Assert.AreEqual(140, t.Health.Current);
        }

        [Test]
        public void EachPhaseOffersThreeCoresTheTankDoesNotHaveAndAPickAddsOne()
        {
            var w = new World(); TankModel t = w.Add(1, Vector2.zero);
            CoreOfferSystem system = Make(w, out MatchSession session, out KdaLedger ledger);
            CoreOffered? offered = null; w.Bus.Subscribe<CoreOffered>(e => offered = e);
            session.StartWarmup();
            w.Advance(2f, new ActionTickable(dt => session.Tick(dt)));       // warmup over, playing
            w.Advance(0.2f, system);
            Assert.IsTrue(offered.HasValue);
            Assert.AreEqual(3, offered.Value.CoreIds.Length);
            system.Pick(1, 1);
            Assert.AreEqual(1, CoreMask.Count(t.Cores));
            Assert.IsTrue(CoreMask.Has(t.Cores, offered.Value.CoreIds[1]));
        }

        [Test]
        public void NothingIsOfferedAgainUntilTheNextPhase()
        {
            var w = new World(); w.Add(1, Vector2.zero);
            CoreOfferSystem system = Make(w, out MatchSession session, out KdaLedger ledger);
            int offers = 0; w.Bus.Subscribe<CoreOffered>(e => offers++);
            session.StartWarmup();
            var both = new ActionTickable(dt => { session.Tick(dt); system.Tick(dt); });
            w.Advance(2f, both);
            system.Pick(1, 0);
            w.Advance(15f, both);                                 // still inside phase 0 (20 s long)
            Assert.AreEqual(1, offers);
            w.Advance(8f, both);                                  // into phase 1
            Assert.AreEqual(2, offers);
        }

        [Test]
        public void BotsPickAtOnceAndAnUnansweredOfferPicksItself()
        {
            var w = new World(); TankModel bot = w.Add(1, Vector2.zero, true); TankModel person = w.Add(2, new Vector2(30f, 0f));
            CoreOfferSystem system = Make(w, out MatchSession session, out KdaLedger ledger);
            session.StartWarmup();
            var both = new ActionTickable(dt => { session.Tick(dt); system.Tick(dt); });
            w.Advance(2f, both);
            Assert.AreEqual(1, CoreMask.Count(bot.Cores));
            Assert.AreEqual(0, CoreMask.Count(person.Cores));
            w.Advance(13f, both);                                 // the 12 s offer ran out
            Assert.AreEqual(1, CoreMask.Count(person.Cores));
        }

        [Test]
        public void CoresAreClearedWhenANewMatchBegins()
        {
            var w = new World(); TankModel t = w.Add(1, Vector2.zero, true);
            CoreOfferSystem system = Make(w, out MatchSession session, out KdaLedger ledger);
            session.StartWarmup();
            var both = new ActionTickable(dt => { session.Tick(dt); system.Tick(dt); });
            w.Advance(2f, both);
            Assert.AreEqual(1, CoreMask.Count(t.Cores));
            session.StartWarmup();
            Assert.AreEqual(0, t.Cores);
        }
    }

    public sealed class StatusTests
    {
        [Test]
        public void TheSpeedItemAndTheSpeedCoreBothRaiseTheTopSpeed()
        {
            var w = new World();
            var sim = new TankSimulation(new DirectDriveMovement(), new HitWorld(), w.Move, w.Settings, new NullEventBus());
            float Run(TankModel t) { t.Intent = new TankIntent { Move = Vector2.right }; for (int i = 0; i < 60; i++) sim.Step(t, 1f / 30f); return t.Body.Speed; }
            TankModel plain = w.Add(1, Vector2.zero), fast = w.Add(2, Vector2.zero);
            fast.Status.SpeedTime = 10f;
            Assert.AreEqual(13f, Run(plain), 0.05f);
            Assert.AreEqual(13f * 1.4f, Run(fast), 0.05f);
        }

        [Test]
        public void TheSpeedItemRunsOutByItself()
        {
            var w = new World();
            var sim = new TankSimulation(new DirectDriveMovement(), new HitWorld(), w.Move, w.Settings, new NullEventBus());
            TankModel t = w.Add(1, Vector2.zero); t.Status.SpeedTime = 1f;
            for (int i = 0; i < 40; i++) sim.Step(t, 1f / 30f);
            Assert.AreEqual(0f, t.Status.SpeedTime);
        }

        [Test]
        public void TheDamageBuffAndCoresScaleWhatAShotDoes()
        {
            var w = new World();
            var catalog = new ArrayWeaponCatalog(new[] { new WeaponSpec(0, "cannon", 20, 0.3f, 60f, 70f, 1, 0f, 0, 0f, 0f) });
            var projectiles = new ProjectileSystem(new HitWorld(), w.Tanks, w.Combat, w.Bus, w.Settings);
            var weapons = new WeaponSystem(catalog, projectiles, new SystemRandom(1), w.Settings);
            TankModel shooter = w.Add(1, Vector2.zero); TankModel target = w.Add(2, new Vector2(20f, 0f));
            shooter.TurretYaw = 90f; shooter.Status.DamageTime = 10f; shooter.Intent.Fire = true;
            weapons.Step(shooter, 0.05f);
            for (int i = 0; i < 30; i++) projectiles.Tick(1f / 30f);
            Assert.AreEqual(100 - 30, target.Health.Current);         // 20 x 1.5
        }

        [Test]
        public void RepairNanitesHealOnlyAfterAQuietSpell()
        {
            var w = new World(); TankModel t = w.Add(1, Vector2.zero);
            t.Mods = CoreModifiers.Identity; t.Mods.RegenPerSecond = 4f;
            w.Combat.ApplyDamage(1, 2, 40);                        // attacker 2 does not exist: fine, nobody is credited
            var status = new StatusSystem(w.Tanks, w.Clock);
            w.Advance(3f, status);
            Assert.AreEqual(60, t.Health.Current);                 // too soon after the hit
            w.Advance(5f, status);
            Assert.Greater(t.Health.Current, 60);
        }

        [Test]
        public void LifestealHealsTheShooterByAShareOfTheDamageDealt()
        {
            var w = new World(); TankModel a = w.Add(1, Vector2.zero), b = w.Add(2, new Vector2(5f, 0f));
            a.Health.Damage(50); a.Mods = CoreModifiers.Identity; a.Mods.Lifesteal = 0.25f;
            w.Combat.ApplyDamage(2, 1, 40);
            Assert.AreEqual(50 + 10, a.Health.Current);
        }
    }
}

namespace Tank.Tests
{
    public sealed class StickTests
    {
        [Test]
        public void ADragInsideTheDeadZoneIsNothing()
        {
            Assert.AreEqual(Vector2.zero, Tank.Core.Input.StickMath.Evaluate(Vector2.zero, new Vector2(5f, 0f), 100f, 0.12f));
        }

        [Test]
        public void StrengthRisesSmoothlyAndIsCappedAtOne()
        {
            Vector2 half = Tank.Core.Input.StickMath.Evaluate(Vector2.zero, new Vector2(56f, 0f), 100f, 0.12f);
            Vector2 far = Tank.Core.Input.StickMath.Evaluate(Vector2.zero, new Vector2(900f, 0f), 100f, 0.12f);
            Assert.Greater(half.magnitude, 0.4f); Assert.Less(half.magnitude, 0.6f);
            Assert.AreEqual(1f, far.magnitude, 1e-4f);
        }

        [Test]
        public void TheYawMatchesTheWorldWhereUpOnTheScreenIsForward()
        {
            Assert.AreEqual(0f, Tank.Core.Input.StickMath.YawDegrees(Vector2.up), 1e-3f);
            Assert.AreEqual(90f, Tank.Core.Input.StickMath.YawDegrees(Vector2.right), 1e-3f);
            Assert.AreEqual(180f, Mathf.Abs(Tank.Core.Input.StickMath.YawDegrees(Vector2.down)), 1e-3f);
        }
    }
}

namespace Tank.Tests
{
    public sealed class DashTests
    {
        static TankSimulation Sim(World w, EventBus bus) { return new TankSimulation(new DirectDriveMovement(), new HitWorld(), w.Move, w.Settings, bus); }

        [Test]
        public void ADashCoversMoreGroundThanDrivingAndPublishesOneEvent()
        {
            var w = new World(); var bus = new EventBus(); var sim = Sim(w, bus);
            int dashes = 0; bus.Subscribe<TankDashed>(e => dashes++);
            TankModel dasher = w.Add(1, Vector2.zero), driver = w.Add(2, Vector2.zero);
            dasher.Intent = new TankIntent { Move = Vector2.right, Dash = true };
            driver.Intent = new TankIntent { Move = Vector2.right };
            for (int i = 0; i < 8; i++) { sim.Step(dasher, 1f / 30f); sim.Step(driver, 1f / 30f); }     // 0.27 s: the dash lasts 0.22 s
            Assert.Greater(dasher.Body.Position.x, driver.Body.Position.x + 1f);
            Assert.AreEqual(1, dashes);                          // holding the button does not re-trigger while it cools down
        }

        [Test]
        public void TheDashNeedsItsCooldownBeforeItCanBeUsedAgain()
        {
            var w = new World(); var sim = Sim(w, new EventBus());
            TankModel t = w.Add(1, Vector2.zero);
            t.Intent = new TankIntent { Move = Vector2.right, Dash = true };
            sim.Step(t, 1f / 30f);
            Assert.AreEqual(w.Settings.DashCooldown, t.Dash.Cooldown, 0.05f);
            t.Intent.Dash = false;                                               // let go; a held button would simply dash again the moment it is ready
            for (int i = 0; i < 60; i++) sim.Step(t, 1f / 30f);                  // 2 s later it is still cooling down
            Assert.Greater(t.Dash.Cooldown, 1.5f);
            for (int i = 0; i < 90; i++) sim.Step(t, 1f / 30f);                  // past the 4 s
            Assert.AreEqual(0f, t.Dash.Cooldown, 0.1f);
        }

        [Test]
        public void AnAfterburnerCoreShortensTheCooldown()
        {
            var w = new World(); var sim = Sim(w, new EventBus());
            TankModel t = w.Add(1, Vector2.zero);
            t.Mods = Tank.Core.Cores.CoreModifiers.Identity; t.Mods.DashCooldownMult = 0.55f;
            t.Intent = new TankIntent { Move = Vector2.right, Dash = true };
            sim.Step(t, 1f / 30f);
            Assert.AreEqual(w.Settings.DashCooldown * 0.55f, t.Dash.Cooldown, 0.05f);
        }

        [Test]
        public void ADashStopsAtAWall()
        {
            var w = new World(); var sim = new TankSimulation(new DirectDriveMovement(), new HitWorld(new IHitShape[] { new EllipseBoundary(10f, 10f) }), w.Move, w.Settings, new EventBus());
            TankModel t = w.Add(1, new Vector2(7f, 0f));
            t.Intent = new TankIntent { Move = Vector2.right, Dash = true };
            for (int i = 0; i < 20; i++) sim.Step(t, 1f / 30f);
            Assert.LessOrEqual(t.Body.Position.x, 9.01f);        // the rim is at 10, a 1 m tank stops at 9
        }
    }
}
