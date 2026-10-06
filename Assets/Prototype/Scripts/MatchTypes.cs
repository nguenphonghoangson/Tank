using System;
using System.Collections.Generic;
using UnityEngine;

namespace TankGame.Prototype
{
    public enum MatchMode { TwoTeams2v2, TwoTeams3v2, ThreeTeams221, Solo5 }

    /// <summary>Point values for ranking/MVP. Territory decides the winner; these never do (except as a tie-break). All tunable.</summary>
    [Serializable]
    public sealed class ScoreConfig
    {
        public int capture = 100;
        public int kill = 50;
        public int assist = 25;
        public int defense = 40;     // kill an enemy standing at (or next to) a point your team owns
        public int contest = 20;     // start contesting a point owned by another team
        public float defenseRadiusExtra = 4f;
    }

    public sealed class TeamDef
    {
        public string name;
        public Color color;
    }

    public sealed class ParticipantDef
    {
        public string name;
        public int teamIndex;
        public bool isLocal;
    }

    /// <summary>
    /// Who plays and on which team. Everything downstream (territory, score, win) only knows team indices,
    /// so solo (one team per player), 2 teams and 3 teams all use the same objective system.
    /// </summary>
    public sealed class MatchConfig
    {
        public const int MaxPlayers = 5;

        public readonly List<TeamDef> Teams = new List<TeamDef>();
        public readonly List<ParticipantDef> Participants = new List<ParticipantDef>();

        static readonly string[] TeamNames = { "Blue", "Red", "Green", "Yellow", "Purple" };
        static readonly Color[] Palette =
        {
            new Color(0.20f, 0.55f, 0.98f), new Color(0.95f, 0.28f, 0.22f), new Color(0.30f, 0.85f, 0.38f),
            new Color(0.98f, 0.82f, 0.20f), new Color(0.72f, 0.42f, 0.95f),
        };

        public bool IsSolo => Teams.Count == Participants.Count;

        /// <summary>Teams of the given sizes; the first participant (team 0) is the local player, everyone else is a bot.</summary>
        public static MatchConfig FromTeamSizes(params int[] sizes)
        {
            var c = new MatchConfig();
            int bot = 0;
            for (int t = 0; t < sizes.Length; t++)
            {
                c.Teams.Add(new TeamDef { name = TeamNames[t % TeamNames.Length], color = Palette[t % Palette.Length] });
                for (int i = 0; i < sizes[t]; i++)
                {
                    bool local = t == 0 && i == 0;
                    c.Participants.Add(new ParticipantDef { name = local ? "You" : "Bot " + (++bot), teamIndex = t, isLocal = local });
                }
            }
            return c;
        }

        /// <summary>Every player is their own team.</summary>
        public static MatchConfig Solo(int count)
        {
            var c = new MatchConfig();
            int bot = 0;
            for (int i = 0; i < count; i++)
            {
                string n = i == 0 ? "You" : "Bot " + (++bot);
                c.Teams.Add(new TeamDef { name = n, color = Palette[i % Palette.Length] });
                c.Participants.Add(new ParticipantDef { name = n, teamIndex = i, isLocal = i == 0 });
            }
            return c;
        }

        public static MatchConfig Preset(MatchMode mode)
        {
            switch (mode)
            {
                case MatchMode.TwoTeams3v2: return FromTeamSizes(3, 2);
                case MatchMode.ThreeTeams221: return FromTeamSizes(2, 2, 1);
                case MatchMode.Solo5: return Solo(5);
                default: return FromTeamSizes(2, 2);
            }
        }

        public bool Validate(out string error)
        {
            error = null;
            if (Participants.Count < 1 || Participants.Count > MaxPlayers) error = "1 to " + MaxPlayers + " players required";
            else if (Teams.Count < 1) error = "at least one team required";
            else
                foreach (var p in Participants)
                    if (p.teamIndex < 0 || p.teamIndex >= Teams.Count) { error = "participant '" + p.name + "' has an invalid team"; break; }
            return error == null;
        }
    }

    /// <summary>Per-player numbers for the scoreboard. Enough to build ranking later.</summary>
    public sealed class PlayerStats
    {
        public int slot, team;
        public string name;
        public bool isLocal;
        public int kills, deaths, assists, captures, defenses, contests, score;
    }
}
