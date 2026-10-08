namespace Tank.Gameplay
{
    /// <summary>Which tank the person at this machine controls. Camera, HUD and sound read it; none of them owns a tank.</summary>
    public interface ILocalPlayer { int TankId { get; } }

    public sealed class LocalPlayer : ILocalPlayer { public int TankId { get; set; } = -1; }

    /// <summary>How far between the last two simulation ticks this frame is (0..1), for smooth rendering at any frame rate.</summary>
    public interface IRenderClock { float Alpha { get; } float Step { get; } }

    /// <summary>One fixed step of whatever drives the game on this machine: the full rules offline and on a server, prediction and interpolation on a client.</summary>
    public interface ISimulationDriver { void Tick(float dt); }

    /// <summary>Published after every simulation tick so views can latch the new state.</summary>
    public readonly struct SimulationTicked { }
}
