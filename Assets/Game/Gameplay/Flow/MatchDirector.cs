using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Tank.Core.Combat;
using Tank.Core.Events;
using Tank.Core.Match;
using Tank.Gameplay.Config;

namespace Tank.Gameplay.Flow
{
    /// <summary>
    /// Everything about a match that waits: respawning a dead tank after a delay, starting the next match after the results.
    /// The rules (MatchSession, ledger) stay tick-driven and synchronous; the waiting lives here, written as plain async code.
    /// </summary>
    public sealed class MatchDirector : IDisposable
    {
        readonly MatchSession m_Session;
        readonly TankLifecycle m_Lifecycle;
        readonly ITankRegistry m_Tanks;
        readonly IStatsLedger m_Ledger;
        readonly ProjectileSystem m_Projectiles;
        readonly MatchConfig m_Match;
        readonly TankConfig m_Tank;
        readonly CancellationTokenSource m_Cancel = new CancellationTokenSource();
        readonly IDisposable m_Killed, m_Ended;

        public MatchDirector(IEventBus bus, MatchSession session, TankLifecycle lifecycle, ITankRegistry tanks, IStatsLedger ledger, ProjectileSystem projectiles, MatchConfig match, TankConfig tank)
        {
            m_Session = session; m_Lifecycle = lifecycle; m_Tanks = tanks; m_Ledger = ledger; m_Projectiles = projectiles; m_Match = match; m_Tank = tank;
            m_Killed = bus.Subscribe<TankKilled>(e => RespawnLater(e.VictimId, m_Cancel.Token).Forget());
            m_Ended = bus.Subscribe<MatchEnded>(e => RestartLater(m_Cancel.Token).Forget());
        }

        public void Begin() { m_Session.StartWarmup(); }

        async UniTaskVoid RespawnLater(int tankId, CancellationToken ct)
        {
            await UniTask.Delay(TimeSpan.FromSeconds(m_Tank.respawnSeconds), cancellationToken: ct).SuppressCancellationThrow();
            if (ct.IsCancellationRequested || m_Session.Phase == MatchPhase.Ended) return;
            TankModel t = m_Tanks.Find(tankId);
            if (t != null && !t.IsAlive) m_Lifecycle.Respawn(t);
        }

        async UniTaskVoid RestartLater(CancellationToken ct)
        {
            await UniTask.Delay(TimeSpan.FromSeconds(m_Match.restartDelaySeconds), cancellationToken: ct).SuppressCancellationThrow();
            if (ct.IsCancellationRequested) return;
            m_Ledger.Reset();
            m_Projectiles.Clear();
            for (int i = 0; i < m_Tanks.All.Count; i++) m_Lifecycle.Respawn(m_Tanks.All[i]);
            m_Session.StartWarmup();
        }

        public void Dispose() { m_Cancel.Cancel(); m_Cancel.Dispose(); m_Killed.Dispose(); m_Ended.Dispose(); }
    }
}
