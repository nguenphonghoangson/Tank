using Reflex.Core;
using Reflex.Enums;
using Resolution = Reflex.Enums.Resolution;
using Tank.Core.Combat;
using Tank.Core.Common;
using Tank.Core.Cores;
using Tank.Core.Items;
using Tank.Core.Events;
using Tank.Core.Hit;
using Tank.Core.Input;
using Tank.Core.Match;
using Tank.Core.Movement;
using Tank.Gameplay.CameraSystem;
using Tank.Gameplay.Config;
using Tank.Gameplay.Cores;
using Tank.Gameplay.Fx;
using Tank.Gameplay.Input;
using Tank.Gameplay.Items;
using Tank.Gameplay.UI;
using Tank.Gameplay.Views;
using UnityEngine;

namespace Tank.Gameplay.Flow
{
    /// <summary>
    /// The composition root: the single place that knows which implementation stands behind each interface. Core classes ask for
    /// interfaces in their constructors and never find out where they come from.
    /// </summary>
    public sealed class GameInstaller : MonoBehaviour, IInstaller
    {
        [Header("Config")]
        [SerializeField] TankConfig tank;
        [SerializeField] WeaponConfig weapons;
        [SerializeField] MatchConfig match;
        [SerializeField] ArenaConfig arena;
        [SerializeField] PlayerPalette palette;
        [SerializeField] FxConfig fx;
        [SerializeField] ItemConfig items;
        [SerializeField] CoreConfig cores;

        [Header("Scene objects")]
        [SerializeField] GameLoop loop;
        [SerializeField] FxService fxService;
        [SerializeField] TankViewSystem tankViews;
        [SerializeField] ProjectileViewSystem projectileViews;
        [SerializeField] CinemachineShaker shaker;
        [SerializeField] ItemViewSystem itemViews;
        [SerializeField] UiTheme theme;

        [Header("Prefabs")]
        [SerializeField] ViewPrefabs viewPrefabs;

        public void InstallBindings(ContainerBuilder b)
        {
            // configuration assets
            b.RegisterValue(tank); b.RegisterValue(match); b.RegisterValue(arena); b.RegisterValue(palette); b.RegisterValue(fx);
            b.RegisterValue(weapons, new[] { typeof(WeaponConfig), typeof(IWeaponCatalog) });
            b.RegisterValue(tank.ToMovement(viewPrefabs.tank.Hitbox)); b.RegisterValue(tank.ToTank(viewPrefabs.tank.Hitbox));      // the hitbox is read from the prefab, where it can be seen
            b.RegisterValue(viewPrefabs);
            b.RegisterValue(items, new[] { typeof(ItemConfig), typeof(IItemCatalog) });
            b.RegisterValue(cores, new[] { typeof(CoreConfig), typeof(ICoreCatalog) });
            b.RegisterValue(new ItemSettings(match.itemPickupRadius)); b.RegisterValue(new CoreSettings(match.corePhases, match.coreOfferSeconds));
            b.RegisterValue(new MatchSettings(match.warmupSeconds, match.durationSeconds));
            b.RegisterValue(new ItemSlotLayout(arena.itemSlots));

            // scene objects the container hands out as interfaces
            b.RegisterValue(loop, new[] { typeof(IRenderClock) });
            b.RegisterValue(fxService, new[] { typeof(IFxService) });
            b.RegisterValue(tankViews); b.RegisterValue(projectileViews); b.RegisterValue(itemViews); b.RegisterValue(theme);
            b.RegisterValue(shaker, new[] { typeof(ICameraShaker) });

            // core services
            Singleton<EventBus>(b, typeof(IEventBus));
            Singleton<SimulationClock>(b, typeof(IClock), typeof(SimulationClock));
            b.RegisterFactory<IRandom>(_ => new SystemRandom(match.randomSeed), Lifetime.Singleton, Resolution.Lazy);
            b.RegisterFactory<IHitWorld>(_ => new HitWorld(arena.BuildShapes()), Lifetime.Singleton, Resolution.Lazy);
            b.RegisterFactory<ISpawnPolicy>(_ => new FarthestSpawnPolicy(arena.spawnPoints), Lifetime.Singleton, Resolution.Lazy);
            Singleton<DirectDriveMovement>(b, typeof(IMovementModel));
            Singleton<TankRegistry>(b, typeof(ITankRegistry), typeof(TankRegistry));
            Singleton<CombatService>(b, typeof(ICombatService));
            Singleton<ProjectileSystem>(b, typeof(IProjectileSpawner), typeof(ProjectileSystem));
            b.RegisterType(typeof(ProjectileTracker), new[] { typeof(IProjectileQuery), typeof(ProjectileTracker) }, Lifetime.Singleton, Resolution.Eager);     // eager: it follows shots from the first one on every machine
            Singleton<WeaponSystem>(b, typeof(WeaponSystem));
            Singleton<TankSimulation>(b, typeof(TankSimulation));
            Singleton<TankStepper>(b, typeof(TankStepper));
            Singleton<InputProcessor>(b, typeof(InputProcessor));
            Singleton<TankLifecycle>(b, typeof(TankLifecycle));
            Singleton<StatusSystem>(b, typeof(StatusSystem));
            b.RegisterType(typeof(ItemSystem), new[] { typeof(ItemSystem) }, Lifetime.Singleton, Resolution.Eager);          // eager: it refills the map when a match begins
            b.RegisterType(typeof(ItemSlotMirror), new[] { typeof(IItemQuery) }, Lifetime.Singleton, Resolution.Eager);      // everyone reads the map's items from the events, so a client needs no item rules
            b.RegisterType(typeof(RepairEffect), new[] { typeof(IItemEffect) }, Lifetime.Singleton, Resolution.Lazy);
            b.RegisterType(typeof(ShieldEffect), new[] { typeof(IItemEffect) }, Lifetime.Singleton, Resolution.Lazy);
            b.RegisterType(typeof(SpeedEffect), new[] { typeof(IItemEffect) }, Lifetime.Singleton, Resolution.Lazy);
            b.RegisterType(typeof(DamageEffect), new[] { typeof(IItemEffect) }, Lifetime.Singleton, Resolution.Lazy);
            b.RegisterType(typeof(WeaponEffect), new[] { typeof(IItemEffect) }, Lifetime.Singleton, Resolution.Lazy);
            Singleton<BotsPickAutomatically>(b, typeof(ICoreOfferPolicy));
            b.RegisterType(typeof(CoreOfferSystem), new[] { typeof(CoreOfferSystem), typeof(ICorePicker) }, Lifetime.Singleton, Resolution.Eager);    // eager: it watches the match phases from the start
            b.RegisterType(typeof(LocalCoreOffers), new[] { typeof(LocalCoreOffers) }, Lifetime.Singleton, Resolution.Eager);
            Singleton<KdaRankingPolicy>(b, typeof(IRankingPolicy));
            b.RegisterType(typeof(KdaLedger), new[] { typeof(IRankingQuery), typeof(IStatsLedger), typeof(KdaLedger) }, Lifetime.Singleton, Resolution.Eager);     // eager: it must be listening from the first kill
            b.RegisterFactory<MatchSession>(c => new MatchSession(new MatchSettings(match.warmupSeconds, match.durationSeconds), c.Resolve<IRankingQuery>(), c.Resolve<IEventBus>()),
                new[] { typeof(MatchSession), typeof(IMatchQuery) }, Lifetime.Singleton, Resolution.Lazy);
            b.RegisterFactory<SimulationPipeline>(c => new SimulationPipeline(
                c.Resolve<SimulationClock>(), c.Resolve<InputProcessor>(), c.Resolve<TankStepper>(), c.Resolve<ProjectileSystem>(), c.Resolve<ProjectileTracker>(), c.Resolve<ItemSystem>(), c.Resolve<StatusSystem>(), c.Resolve<CoreOfferSystem>(), c.Resolve<MatchSession>()), Lifetime.Singleton, Resolution.Lazy);
            Singleton<LocalSimulationDriver>(b, typeof(ISimulationDriver), typeof(LocalSimulationDriver));

            // gameplay glue
            b.RegisterValue(new LobbyState());
            Singleton<UiPointerBlocker>(b, typeof(IPointerBlocker));
            Singleton<TouchControls>(b, typeof(TouchControls));
            Singleton<PlayerCommandSource>(b, typeof(PlayerCommandSource));
            Singleton<LocalPlayer>(b, typeof(ILocalPlayer), typeof(LocalPlayer));
            Singleton<PlayerSpawner>(b, typeof(PlayerSpawner));
            Singleton<MatchDirector>(b, typeof(MatchDirector));
            b.RegisterType(typeof(GameFxPresenter), new[] { typeof(GameFxPresenter) }, Lifetime.Singleton, Resolution.Eager);
            b.RegisterType(typeof(CameraShakePresenter), new[] { typeof(CameraShakePresenter) }, Lifetime.Singleton, Resolution.Eager);
        }

        static void Singleton<T>(ContainerBuilder b, params System.Type[] contracts) { b.RegisterType(typeof(T), contracts, Lifetime.Singleton, Resolution.Lazy); }
    }
}
