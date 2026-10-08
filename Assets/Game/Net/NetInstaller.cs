using Reflex.Core;
using Reflex.Enums;
using Tank.Core.Combat;
using Tank.Core.Common;
using Tank.Core.Cores;
using Tank.Core.Input;
using Tank.Core.Items;
using Tank.Core.Match;
using Tank.Gameplay;
using Tank.Gameplay.Flow;
using UnityEngine;
using Resolution = Reflex.Enums.Resolution;

namespace Tank.Net
{
    /// <summary>
    /// Adds the network to the game without the game knowing. It sits after GameInstaller on the same object, and a later binding wins, so
    /// it replaces exactly three things: the tick order (adds the snapshot step), which driver runs the tick, and where a client reads the clock.
    /// Remove this component and the scene plays offline, as it did before.
    /// </summary>
    public sealed class NetInstaller : MonoBehaviour, IInstaller
    {
        public void InstallBindings(ContainerBuilder b)
        {
            Singleton<MirrorSessionRole>(b, typeof(ISessionRole));
            Singleton<ClientRoster>(b, typeof(ClientRoster));
            Singleton<ClientSimulationDriver>(b, typeof(ClientSimulationDriver));
            Singleton<ServerSnapshotStep>(b, typeof(ServerSnapshotStep));
            Singleton<HostInputFeeder>(b, typeof(HostInputFeeder));

            b.RegisterFactory<SimulationPipeline>(c => new SimulationPipeline(
                c.Resolve<SimulationClock>(), c.Resolve<HostInputFeeder>(), c.Resolve<InputProcessor>(), c.Resolve<TankStepper>(), c.Resolve<ProjectileSystem>(),
                c.Resolve<ProjectileTracker>(), c.Resolve<ItemSystem>(), c.Resolve<StatusSystem>(), c.Resolve<CoreOfferSystem>(), c.Resolve<MatchSession>(), c.Resolve<ServerSnapshotStep>()), Lifetime.Singleton, Resolution.Lazy);
            Singleton<RoleSimulationDriver>(b, typeof(ISimulationDriver));
            Singleton<RoleMatchQuery>(b, typeof(IMatchQuery));
            Singleton<RoleCorePicker>(b, typeof(ICorePicker));
        }

        static void Singleton<T>(ContainerBuilder b, params System.Type[] contracts) { b.RegisterType(typeof(T), contracts, Lifetime.Singleton, Resolution.Lazy); }
    }
}
