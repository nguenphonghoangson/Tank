using System.Collections.Generic;
using Tank.Core.Combat;
using Tank.Core.Input;
using Tank.Core.Netcode;

namespace Tank.Net
{
    /// <summary>
    /// A command source fed by the network: the server queues the input frames a client sends and hands the simulation one per tick,
    /// so a remote player is driven through the same input pipeline as a local one.
    /// </summary>
    public sealed class NetworkCommandSource : ICommandSource
    {
        const int MaxQueued = 4;               // a longer backlog is latency we would only be adding; the oldest frames are dropped

        readonly Queue<InputFrame> m_Queue = new Queue<InputFrame>();
        readonly MoveCommand m_Move = new MoveCommand();
        readonly AimCommand m_Aim = new AimCommand();
        readonly FireCommand m_Fire = new FireCommand();
        readonly DashCommand m_Dash = new DashCommand();
        InputFrame m_Last;
        bool m_HasLast;
        uint m_LastQueued;

        /// <summary>The last frame the simulation has consumed; sent back so the owner knows how much of its prediction is confirmed.</summary>
        public uint AckSeq { get; private set; }
        public int Starved { get; private set; }

        public void Enqueue(InputFrame frame)
        {
            if (frame.Seq <= m_LastQueued) return;                 // redundant copy of a frame we already have
            m_Queue.Enqueue(frame); m_LastQueued = frame.Seq;
        }

        public void Collect(TankModel tank, List<ITankCommand> into)
        {
            while (m_Queue.Count > MaxQueued) m_Queue.Dequeue();
            InputFrame f;
            if (m_Queue.Count > 0) { f = m_Queue.Dequeue(); m_Last = f; m_HasLast = true; AckSeq = f.Seq; }
            else if (m_HasLast) { f = m_Last; f.Buttons = 0; Starved++; }      // nothing arrived: keep driving the same way, but never repeat a shot or a dash
            else return;

            TankIntent intent = f.ToIntent();
            m_Move.Direction = intent.Move; into.Add(m_Move);
            m_Aim.Yaw = intent.AimYaw; into.Add(m_Aim);
            if (intent.Fire) into.Add(m_Fire);
            if (intent.Dash) into.Add(m_Dash);
        }
    }
}
