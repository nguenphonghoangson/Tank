using Tank.Core.Match;

namespace Tank.Gameplay.Flow
{
    /// <summary>Runs the whole rule set: offline play, a dedicated server and a host all drive the game this way.</summary>
    public sealed class LocalSimulationDriver : ISimulationDriver
    {
        readonly SimulationPipeline m_Pipeline;
        public LocalSimulationDriver(SimulationPipeline pipeline) { m_Pipeline = pipeline; }
        public void Tick(float dt) { m_Pipeline.Tick(dt); }
    }
}
