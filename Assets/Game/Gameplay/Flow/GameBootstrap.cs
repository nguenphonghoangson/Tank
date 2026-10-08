using Reflex.Attributes;
using Tank.Core.Common;
using Tank.Core.Combat;
using Tank.Core.Hit;
using Tank.Core.Items;
using Tank.Gameplay.Config;
using Tank.Gameplay.Input;
using UnityEngine;

namespace Tank.Gameplay.Flow
{
    /// <summary>Entry point of a match scene: brings in the local player and the bots, then starts the flow.</summary>
    public sealed class GameBootstrap : MonoBehaviour
    {
        [SerializeField] bool autoStartOffline = true;       // off when a network menu decides how the match starts

        PlayerSpawner m_Spawner;
        MatchDirector m_Director;
        MatchConfig m_Match;
        ITankRegistry m_Tanks;
        IHitWorld m_World;
        IClock m_Clock;
        IRandom m_Random;
        IItemQuery m_Items;
        PlayerCommandSource m_Player;

        [Inject]
        void Construct(PlayerSpawner spawner, MatchDirector director, MatchConfig match, ITankRegistry tanks, IHitWorld world, IClock clock, IRandom random, IItemQuery items, PlayerCommandSource player)
        {
            m_Player = player; m_Items = items;
            m_Spawner = spawner; m_Director = director; m_Match = match; m_Tanks = tanks; m_World = world; m_Clock = clock; m_Random = random;
        }

        void Start() { if (autoStartOffline) StartOffline(); }

        /// <summary>Set by whatever started the match (the menu); empty means "You".</summary>
        public string LocalPlayerName { get; set; }

        /// <summary>Starts a local match with the given number of bots (a negative number means the scene's default).</summary>
        public void StartOffline(int bots = -1)
        {
            if (bots < 0) bots = m_Match.botCount;
            m_Spawner.Spawn(string.IsNullOrWhiteSpace(LocalPlayerName) ? "You" : LocalPlayerName, m_Player, true);
            for (int i = 0; i < bots; i++) m_Spawner.Spawn("Bot " + (i + 1), new BotCommandSource(m_Tanks, m_World, m_Clock, m_Random, m_Items), false, true);
            m_Director.Begin();
        }
    }
}
