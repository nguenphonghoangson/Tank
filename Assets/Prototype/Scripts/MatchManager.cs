using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace TankGame.Prototype
{
    /// <summary>
    /// Runs one match: loads the map, spawns the participants, steps the control points, keeps territory and score,
    /// respawns the dead and decides the winner. Territory control decides the match; score only ranks players.
    /// Prototype scope: no networking, no matchmaking. A real flow would call StartMatch(config) from a lobby.
    /// </summary>
    public sealed class MatchManager : MonoBehaviour
    {
        public enum MatchState { Playing, Ended }

        [Header("Setup")]
        public MapDef map;
        public TankUnit tankPrefab;
        public WeaponDef weapon;
        public CombatFx fx;
        public CameraRig cameraRig;
        public PlayerTankInput localInput;
        public MatchMode mode = MatchMode.TwoTeams2v2;
        public bool allBots;     // test switch: the local player is driven by a bot too

        [Header("Rules")]
        public float matchSeconds = 240f;
        public float respawnSeconds = 3f;
        public float captureSeconds = 6f;
        public float dominationSeconds = 25f;
        public float unattendedDecaySeconds = 45f;   // an owned point with no defender loses its hold over this long
        public ScoreConfig score = new ScoreConfig();

        public MatchState State { get; private set; }
        public MatchConfig Config { get; private set; }
        public MapLayout Layout { get; private set; }
        public PlayerStats[] Players { get; private set; }
        public TankUnit[] Tanks { get; private set; }
        public BotBrain[] Bots { get; private set; }
        public float TimeLeft { get; private set; }
        public float Elapsed { get; private set; }
        public float[] CurrentShare { get; private set; }
        public float[] AverageShare { get; private set; }
        public float NeutralShare { get; private set; }
        public int WinnerTeam { get; private set; } = -1;
        public bool IsDraw { get; private set; }
        public string EndReason { get; private set; }
        public int DominatingTeam { get; private set; } = -1;
        public float DominationTimer { get; private set; }
        public int LocalSlot { get; private set; } = -1;
        public int MatchesStarted { get; private set; }

        public const int LogSize = 6;
        public readonly string[] Log = new string[LogSize];
        public readonly float[] LogTime = new float[LogSize];

        GameObject m_MapInstance;
        float[] m_ControlSeconds, m_RespawnAt;
        int[] m_Counts;
        uint[] m_Presence;
        Color[] m_TeamColors;
        int m_LogHead;

        void Start()
        {
            StartMatch(MatchConfig.Preset(mode));
        }

        public Color TeamColor(int team) { return m_TeamColors[team]; }
        public string TeamName(int team) { return Config.Teams[team].name; }

        public int TeamScore(int team)
        {
            int s = 0;
            for (int i = 0; i < Players.Length; i++) if (Players[i].team == team) s += Players[i].score;
            return s;
        }

        public float LocalRespawnIn => LocalSlot >= 0 && Tanks[LocalSlot].IsDead ? Mathf.Max(0f, m_RespawnAt[LocalSlot] - Time.time) : 0f;

        // ------------------------------------------------------------------ lifecycle

        public void StartMatch(MatchConfig config)
        {
            if (!config.Validate(out string error)) { Debug.LogError("Invalid match config: " + error); return; }
            Teardown();
            Time.timeScale = 1f;
            Config = config;
            MatchesStarted++;

            int teamCount = config.Teams.Count, n = config.Participants.Count;
            m_TeamColors = new Color[teamCount];
            for (int t = 0; t < teamCount; t++) m_TeamColors[t] = config.Teams[t].color;
            m_Counts = new int[teamCount];
            m_ControlSeconds = new float[teamCount];
            CurrentShare = new float[teamCount];
            AverageShare = new float[teamCount];
            NeutralShare = 1f;
            Elapsed = 0f;
            TimeLeft = matchSeconds;
            WinnerTeam = -1; IsDraw = false; EndReason = null; DominatingTeam = -1; DominationTimer = 0f;
            for (int i = 0; i < LogSize; i++) { Log[i] = null; LogTime[i] = -99f; }

            m_MapInstance = Instantiate(map.prefab);
            Layout = m_MapInstance.GetComponent<MapLayout>();
            m_Presence = new uint[Layout.controlPoints.Length];
            foreach (ControlPoint cp in Layout.controlPoints) cp.Init(m_TeamColors, captureSeconds, unattendedDecaySeconds);
            foreach (Pickup p in Layout.pickups) { p.fx = fx; p.ResetPickup(); p.Collected += OnPickupCollected; }

            Players = new PlayerStats[n];
            Tanks = new TankUnit[n];
            Bots = new BotBrain[n];
            m_RespawnAt = new float[n];
            LocalSlot = -1;
            var perTeam = new int[teamCount];
            for (int i = 0; i < n; i++)
            {
                ParticipantDef pd = config.Participants[i];
                Players[i] = new PlayerStats { slot = i, team = pd.teamIndex, name = pd.name, isLocal = pd.isLocal };
                Vector3 pos = SpawnPosition(pd.teamIndex, perTeam[pd.teamIndex]++, null);
                TankUnit u = Instantiate(tankPrefab, pos, FacingCenter(pos));
                u.name = "Tank_" + pd.name;
                u.team = pd.teamIndex;
                u.slot = i;
                u.displayName = pd.name;
                u.isLocal = pd.isLocal;
                u.fx = fx;
                u.SetWeapon(weapon);
                var fb = u.GetComponent<TankFeedback>();
                fb.fx = fx;
                Color c = m_TeamColors[pd.teamIndex];
                fb.SetTint(c, Color.Lerp(c, Color.white, 0.35f), new Color(c.r, c.g, c.b, 0.55f));
                var bar = u.GetComponentInChildren<HealthBar>(true);
                bar.SetColor(c);
                u.Died += OnTankDied;
                Tanks[i] = u;

                if (pd.isLocal) LocalSlot = i;
                if (!pd.isLocal || allBots)
                {
                    var bot = u.gameObject.AddComponent<BotBrain>();
                    bot.self = u;
                    bot.match = this;
                    bot.index = i;
                    Bots[i] = bot;
                }
            }

            if (LocalSlot >= 0 && !allBots)
            {
                localInput.tank = Tanks[LocalSlot];
                localInput.enabled = true;
            }
            else if (localInput != null) localInput.enabled = false;
            if (cameraRig != null) cameraRig.target = LocalSlot >= 0 ? Tanks[LocalSlot].transform : Tanks[0].transform;

            State = MatchState.Playing;
            AddLog("Match start: " + (config.IsSolo ? "free-for-all" : config.Teams.Count + " teams") + ", " + n + " players");
        }

        void Teardown()
        {
            if (Tanks != null)
                foreach (TankUnit t in Tanks) if (t != null) Destroy(t.gameObject);
            if (Layout != null)
                foreach (Pickup p in Layout.pickups) if (p != null) p.Collected -= OnPickupCollected;
            if (m_MapInstance != null) Destroy(m_MapInstance);
            Tanks = null; Layout = null; m_MapInstance = null;
        }

        void OnDestroy() { Teardown(); }

        // ------------------------------------------------------------------ update

        void Update()
        {
            Keyboard kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.f5Key.wasPressedThisFrame) StartMatch(MatchConfig.Preset(mode));
                if (kb.mKey.wasPressedThisFrame)
                {
                    mode = (MatchMode)(((int)mode + 1) % Enum.GetValues(typeof(MatchMode)).Length);
                    StartMatch(MatchConfig.Preset(mode));
                }
            }
            if (State != MatchState.Playing) return;

            float dt = Time.deltaTime;
            Elapsed += dt;
            TimeLeft -= dt;
            StepPoints(dt);
            StepTerritory(dt);
            StepRespawns();
            if (State == MatchState.Playing && TimeLeft <= 0f) End("Time up", -1);
        }

        void StepPoints(float dt)
        {
            ControlPoint[] points = Layout.controlPoints;
            for (int p = 0; p < points.Length; p++)
            {
                ControlPoint cp = points[p];
                Array.Clear(m_Counts, 0, m_Counts.Length);
                uint mask = 0;
                for (int i = 0; i < Tanks.Length; i++)
                {
                    TankUnit t = Tanks[i];
                    if (t.IsDead || !cp.Contains(t.transform.position)) continue;
                    m_Counts[t.team]++;
                    mask |= 1u << i;
                }
                m_Presence[p] = mask;

                CapturePointModel.Change ch = cp.Step(dt, m_Counts, out int team);
                switch (ch)
                {
                    case CapturePointModel.Change.Captured:
                        AwardPresent(mask, team, score.capture, 0);
                        AddLog(TeamName(team) + " captured " + cp.label);
                        break;
                    case CapturePointModel.Change.Decayed:
                        AddLog(cp.label + " fell back to neutral: " + TeamName(team) + " left it undefended");
                        break;
                    case CapturePointModel.Change.Neutralized:
                        AddLog(TeamName(team) + " neutralized " + cp.label);
                        break;
                    case CapturePointModel.Change.ContestStarted:
                        if (cp.Owner >= 0)
                        {
                            for (int i = 0; i < Tanks.Length; i++)
                                if ((mask & (1u << i)) != 0 && Players[i].team != cp.Owner) { Players[i].score += score.contest; Players[i].contests++; }
                            AddLog(cp.label + " is contested");
                        }
                        break;
                }
            }
        }

        void AwardPresent(uint mask, int team, int points, int kind)
        {
            for (int i = 0; i < Tanks.Length; i++)
            {
                if ((mask & (1u << i)) == 0 || Players[i].team != team) continue;
                Players[i].score += points;
                Players[i].captures++;
            }
        }

        void StepTerritory(float dt)
        {
            ControlPoint[] points = Layout.controlPoints;
            float total = 0f, unowned = 0f;
            Array.Clear(CurrentShare, 0, CurrentShare.Length);
            for (int p = 0; p < points.Length; p++)
            {
                total += points[p].weight;
                if (points[p].Owner >= 0) CurrentShare[points[p].Owner] += points[p].weight; else unowned += points[p].weight;
            }
            if (total <= 0f) return;
            for (int t = 0; t < CurrentShare.Length; t++)
            {
                CurrentShare[t] /= total;
                m_ControlSeconds[t] += CurrentShare[t] * dt;
                AverageShare[t] = Elapsed > 0f ? m_ControlSeconds[t] / Elapsed : 0f;
            }
            NeutralShare = unowned / total;

            // domination: one team holds every point without a break
            int dom = points[0].Owner;
            for (int p = 1; p < points.Length && dom >= 0; p++) if (points[p].Owner != dom) dom = -1;
            if (dom >= 0 && dom == DominatingTeam) DominationTimer += dt;
            else { DominatingTeam = dom; DominationTimer = 0f; }
            if (DominatingTeam >= 0 && DominationTimer >= dominationSeconds) End("Domination", DominatingTeam);
        }

        void StepRespawns()
        {
            for (int i = 0; i < Tanks.Length; i++)
                if (Tanks[i].IsDead && Time.time >= m_RespawnAt[i])
                {
                    Vector3 pos = SpawnPosition(Players[i].team, i, Tanks[i]);
                    Tanks[i].Respawn(pos, FacingCenter(pos));
                }
        }

        // ------------------------------------------------------------------ events

        void OnTankDied(TankUnit victim)
        {
            PlayerStats v = Players[victim.slot];
            v.deaths++;
            m_RespawnAt[victim.slot] = Time.time + respawnSeconds;

            TankUnit killer = victim.Killer;
            if (killer != null && killer.team != victim.team && State == MatchState.Playing)
            {
                PlayerStats k = Players[killer.slot];
                k.kills++;
                k.score += score.kill;
                for (int s = 0; s < Players.Length; s++)
                    if ((victim.AssistMask & (1 << s)) != 0 && Players[s].team != victim.team) { Players[s].assists++; Players[s].score += score.assist; }

                // defense: the victim died at a point the killer's team owns
                foreach (ControlPoint cp in Layout.controlPoints)
                    if (cp.Owner == killer.team && cp.Contains(victim.transform.position, score.defenseRadiusExtra))
                    {
                        k.defenses++;
                        k.score += score.defense;
                        break;
                    }
                AddLog(k.name + " destroyed " + v.name);
            }
            else AddLog(v.name + " was destroyed");
        }

        void OnPickupCollected(Pickup p, TankUnit by)
        {
            AddLog(by.displayName + " took " + p.label);
        }

        void End(string reason, int forcedWinner)
        {
            State = MatchState.Ended;
            EndReason = reason;
            int best = -1;
            if (forcedWinner >= 0) best = forcedWinner;
            else
            {
                float top = -1f; bool tie = false;
                for (int t = 0; t < AverageShare.Length; t++)
                {
                    if (AverageShare[t] > top + 0.0005f) { top = AverageShare[t]; best = t; tie = false; }
                    else if (Mathf.Abs(AverageShare[t] - top) <= 0.0005f) tie = true;
                }
                if (tie)
                {
                    // equal control: combat score decides; still equal means a draw
                    int bestScore = -1; bool scoreTie = false; best = -1;
                    for (int t = 0; t < AverageShare.Length; t++)
                    {
                        if (Mathf.Abs(AverageShare[t] - top) > 0.0005f) continue;
                        int sc = TeamScore(t);
                        if (sc > bestScore) { bestScore = sc; best = t; scoreTie = false; }
                        else if (sc == bestScore) scoreTie = true;
                    }
                    if (scoreTie) best = -1;
                }
            }
            WinnerTeam = best;
            IsDraw = best < 0;
            for (int i = 0; i < Tanks.Length; i++)
            {
                if (Bots[i] != null) Bots[i].enabled = false;
                Tanks[i].Command = default;
            }
            if (localInput != null) localInput.enabled = false;
            AddLog(IsDraw ? "Draw" : TeamName(WinnerTeam) + " wins (" + reason + ")");
        }

        void AddLog(string line)
        {
            Log[m_LogHead] = line;
            LogTime[m_LogHead] = Time.time;
            m_LogHead = (m_LogHead + 1) % LogSize;
        }

        /// <summary>Newest entry first.</summary>
        public int LogIndexFromNewest(int i) { return ((m_LogHead - 1 - i) % LogSize + LogSize) % LogSize; }

        // ------------------------------------------------------------------ spawning

        Vector3 SpawnPosition(int team, int memberIndex, TankUnit respawning)
        {
            SpawnGroup g = Layout.spawnGroups[team % Layout.spawnGroups.Length];
            if (respawning == null) return g.points[memberIndex % g.points.Length].position;

            // respawn at the point of the team's group that is farthest from living enemies
            Vector3 best = g.points[0].position;
            float bestDist = -1f;
            foreach (Transform p in g.points)
            {
                float nearest = float.MaxValue;
                bool occupied = false;
                for (int i = 0; i < Tanks.Length; i++)
                {
                    TankUnit t = Tanks[i];
                    if (t == respawning || t.IsDead) continue;
                    float d = Vector3.Distance(p.position, t.transform.position);
                    if (d < 3.5f) occupied = true;                     // never spawn on top of a living tank
                    if (t.team != team) nearest = Mathf.Min(nearest, d);
                }
                float score = occupied ? nearest - 1000f : nearest;
                if (score > bestDist) { bestDist = score; best = p.position; }
            }
            return best;
        }

        static Quaternion FacingCenter(Vector3 pos)
        {
            Vector3 d = -pos; d.y = 0f;
            return d.sqrMagnitude > 0.01f ? Quaternion.LookRotation(d.normalized) : Quaternion.identity;
        }
    }
}
