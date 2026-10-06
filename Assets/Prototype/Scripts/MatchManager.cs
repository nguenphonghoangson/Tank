using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace TankGame.Prototype
{
    /// <summary>
    /// Runs one match: loads the map, spawns the participants and plays four phases. Each phase starts with a core pick
    /// (a random offer of three per player, the match pauses for the human pick), then runs for a fixed time. Flags give
    /// score every second to the team holding them, kills/captures/assists/defense/contests add to the total, the last
    /// phase is worth more and plays faster. Highest total score wins. Prototype scope: no networking.
    /// </summary>
    public sealed class MatchManager : MonoBehaviour
    {
        public enum MatchState { Picking, Playing, Ended }

        [Header("Setup")]
        public MapDef map;
        public TankUnit tankPrefab;
        public WeaponDef weapon;
        public CombatFx fx;
        public CameraRig cameraRig;
        public PlayerTankInput localInput;
        public MobileTankInput mobileInput;
        public bool forceMobileControls;   // use touch controls even when not on a phone (editor tests)
        public MatchMode mode = MatchMode.TwoTeams2v2;
        public bool allBots;             // test switch: the local player is driven by a bot too
        public bool coresEnabled = true; // test switch: skip core picking
        public int seed;                 // 0 = random every match; any other value makes offers and items reproducible

        [Header("Rules")]
        public float matchSeconds = 240f;          // total play time, split evenly over the phases
        public float captureSeconds = 6f;
        public float unattendedDecaySeconds = 45f;
        public float dominationSeconds = 0f;       // > 0 ends the match when one team holds every flag this long (off by default)
        public float flagIncomePerSecond = 3f;     // score per second per owned flag (times its weight and the phase multiplier)
        public float coreChoiceSeconds = 15f;
        public ScoreConfig score = new ScoreConfig();
        public PhaseDef[] phases =
        {
            new PhaseDef { name = "Phase 1", scoreMultiplier = 1f,    tempo = 1f,    respawnSeconds = 3f,   itemRespawnScale = 1f },
            new PhaseDef { name = "Phase 2", scoreMultiplier = 1f,    tempo = 1f,    respawnSeconds = 3f,   itemRespawnScale = 1f },
            new PhaseDef { name = "Phase 3", scoreMultiplier = 1.25f, tempo = 1.15f, respawnSeconds = 2.5f, itemRespawnScale = 0.8f },
            new PhaseDef { name = "FINAL",   scoreMultiplier = 2f,    tempo = 1.5f,  respawnSeconds = 1.5f, itemRespawnScale = 0.5f },
        };

        public MatchState State { get; private set; }
        public MatchConfig Config { get; private set; }
        public MapLayout Layout { get; private set; }
        public PlayerStats[] Players { get; private set; }
        public TankUnit[] Tanks { get; private set; }
        public BotBrain[] Bots { get; private set; }
        public float TimeLeft { get; private set; }
        public float Elapsed { get; private set; }
        public int PhaseIndex { get; private set; }
        public float PhaseTimeLeft { get; private set; }
        public PhaseDef CurrentPhase => phases[Mathf.Clamp(PhaseIndex, 0, phases.Length - 1)];
        public float[] CurrentShare { get; private set; }
        public float[] AverageShare { get; private set; }
        public float[] TeamIncome { get; private set; }
        public float NeutralShare { get; private set; }
        public int WinnerTeam { get; private set; } = -1;
        public bool IsDraw { get; private set; }
        public string EndReason { get; private set; }
        public int DominatingTeam { get; private set; } = -1;
        public float DominationTimer { get; private set; }
        public int LocalSlot { get; private set; } = -1;
        public int MatchesStarted { get; private set; }
        public CoreDef[][] Offers { get; private set; }
        public float PickTimeLeft => Mathf.Max(0f, m_PickEnds - Time.unscaledTime);

        /// <summary>The three cores the local player may choose from right now (null when not picking or already picked).</summary>
        public CoreDef[] LocalOffers => State == MatchState.Picking && LocalSlot >= 0 && !allBots && !m_Picked[LocalSlot] ? Offers[LocalSlot] : null;

        public const int LogSize = 6;
        public readonly string[] Log = new string[LogSize];
        public readonly float[] LogTime = new float[LogSize];

        GameObject m_MapInstance;
        float[] m_ControlSeconds, m_RespawnAt, m_Power;
        int[] m_Counts;
        uint[] m_Presence;
        bool[] m_Picked;
        Color[] m_TeamColors;
        System.Random m_Rng;
        float m_PickEnds;
        int m_LogHead;

        public void Restart() { StartMatch(MatchConfig.Preset(mode)); }

        public void NextMode()
        {
            mode = (MatchMode)(((int)mode + 1) % Enum.GetValues(typeof(MatchMode)).Length);
            StartMatch(MatchConfig.Preset(mode));
        }

        /// <summary>Test hook: finish the match now.</summary>
        public void EndNow() { if (State != MatchState.Ended) End("Test", -1); }

        public bool UseTouch => Application.isMobilePlatform || forceMobileControls;

        void Start()
        {
            if (Application.isMobilePlatform)
            {
                MobilePerformance.Apply();
                Application.targetFrameRate = 60;
                Screen.sleepTimeout = SleepTimeout.NeverSleep;
            }
            StartMatch(MatchConfig.Preset(mode));
        }

        public Color TeamColor(int team) { return m_TeamColors[team]; }
        public string TeamName(int team) { return Config.Teams[team].name; }

        public int CombatScore(int team)
        {
            int s = 0;
            for (int i = 0; i < Players.Length; i++) if (Players[i].team == team) s += Players[i].score;
            return s;
        }

        /// <summary>Everything the team has earned: player awards plus flag income.</summary>
        public int TeamScore(int team) { return CombatScore(team) + Mathf.RoundToInt(TeamIncome[team]); }

        public float LocalRespawnIn => LocalSlot >= 0 && Tanks[LocalSlot].IsDead ? Mathf.Max(0f, m_RespawnAt[LocalSlot] - Time.time) : 0f;

        // ------------------------------------------------------------------ lifecycle

        public void StartMatch(MatchConfig config)
        {
            if (!config.Validate(out string error)) { Debug.LogError("Invalid match config: " + error); return; }
            Teardown();
            Time.timeScale = 1f;
            if (fx != null) { fx.Paused = false; fx.CancelHitStop(); }
            Config = config;
            MatchesStarted++;
            m_Rng = seed != 0 ? new System.Random(seed) : new System.Random();

            int teamCount = config.Teams.Count, n = config.Participants.Count;
            m_TeamColors = new Color[teamCount];
            for (int t = 0; t < teamCount; t++) m_TeamColors[t] = config.Teams[t].color;
            m_Counts = new int[teamCount];
            m_Power = new float[teamCount];
            m_ControlSeconds = new float[teamCount];
            CurrentShare = new float[teamCount];
            AverageShare = new float[teamCount];
            TeamIncome = new float[teamCount];
            NeutralShare = 1f;
            Elapsed = 0f;
            TimeLeft = matchSeconds;
            WinnerTeam = -1; IsDraw = false; EndReason = null; DominatingTeam = -1; DominationTimer = 0f;
            for (int i = 0; i < LogSize; i++) { Log[i] = null; LogTime[i] = -99f; }

            m_MapInstance = Instantiate(map.prefab);
            Layout = m_MapInstance.GetComponent<MapLayout>();
            m_Presence = new uint[Layout.controlPoints.Length];
            foreach (ControlPoint cp in Layout.controlPoints) cp.Init(m_TeamColors, captureSeconds, unattendedDecaySeconds);
            foreach (Pickup p in Layout.pickups)
            {
                p.fx = fx;
                p.Collected += OnPickupCollected;
                p.Roll(m_Rng, m_Rng.NextDouble() < 0.7);     // some slots start empty so the map never looks the same twice
            }

            Players = new PlayerStats[n];
            Tanks = new TankUnit[n];
            Bots = new BotBrain[n];
            m_RespawnAt = new float[n];
            m_Picked = new bool[n];
            Offers = new CoreDef[n][];
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

            bool human = LocalSlot >= 0 && !allBots;
            if (localInput != null) localInput.enabled = false;
            if (mobileInput != null) mobileInput.enabled = false;
            if (human)
            {
                if (UseTouch && mobileInput != null) { mobileInput.tank = Tanks[LocalSlot]; mobileInput.enabled = true; }
                else { localInput.tank = Tanks[LocalSlot]; localInput.enabled = true; }
            }
            if (cameraRig != null) cameraRig.target = LocalSlot >= 0 ? Tanks[LocalSlot].transform : Tanks[0].transform;

            State = MatchState.Playing;
            AddLog("Match start: " + (config.IsSolo ? "free-for-all" : config.Teams.Count + " teams") + ", " + n + " players");
            BeginPhase(0);
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

        // ------------------------------------------------------------------ phases and core picking

        void BeginPhase(int index)
        {
            PhaseIndex = index;
            PhaseDef ph = CurrentPhase;
            PhaseTimeLeft = matchSeconds / phases.Length;
            foreach (ControlPoint cp in Layout.controlPoints)
            {
                cp.Model.CaptureSeconds = captureSeconds / ph.tempo;
                cp.Model.DecaySeconds = unattendedDecaySeconds / ph.tempo;
            }
            foreach (Pickup p in Layout.pickups) p.respawnScale = ph.itemRespawnScale;
            AddLog(ph.name + (ph.scoreMultiplier > 1f ? "  score x" + ph.scoreMultiplier.ToString("0.##") : "") + (ph.tempo > 1f ? "  faster" : ""));

            if (!coresEnabled) { State = MatchState.Playing; return; }

            // three random cores per player (never one the tank already has); bots choose at once
            for (int i = 0; i < Tanks.Length; i++)
            {
                var pool = new System.Collections.Generic.List<CoreDef>(CoreLibrary.All);
                foreach (CoreDef owned in Tanks[i].Cores) pool.Remove(owned);
                for (int k = pool.Count - 1; k > 0; k--) { int j = m_Rng.Next(k + 1); CoreDef tmp = pool[k]; pool[k] = pool[j]; pool[j] = tmp; }
                Offers[i] = pool.GetRange(0, Mathf.Min(3, pool.Count)).ToArray();
                m_Picked[i] = false;
                if (Bots[i] != null) Pick(i, m_Rng.Next(Offers[i].Length));
            }
            if (AllPicked()) { State = MatchState.Playing; return; }

            // waiting for the human: the match is frozen until they choose (or the countdown ends)
            State = MatchState.Picking;
            m_PickEnds = Time.unscaledTime + coreChoiceSeconds;
            if (fx != null) { fx.CancelHitStop(); fx.Paused = true; }
            Time.timeScale = 0f;
        }

        bool AllPicked()
        {
            for (int i = 0; i < m_Picked.Length; i++) if (!m_Picked[i]) return false;
            return true;
        }

        void Pick(int slot, int offerIndex)
        {
            if (m_Picked[slot]) return;
            CoreDef c = Offers[slot][Mathf.Clamp(offerIndex, 0, Offers[slot].Length - 1)];
            Tanks[slot].AddCore(c);
            Players[slot].cores++;
            m_Picked[slot] = true;
            if (slot == LocalSlot || Players[slot].isLocal) AddLog("You took " + c.name);
        }

        /// <summary>Called by the HUD when the local player chooses one of the offered cores.</summary>
        public void PickCore(int offerIndex)
        {
            if (State != MatchState.Picking || LocalSlot < 0 || m_Picked[LocalSlot]) return;
            Pick(LocalSlot, offerIndex);
            if (AllPicked()) EndPicking();
        }

        void EndPicking()
        {
            State = MatchState.Playing;
            Time.timeScale = 1f;
            if (fx != null) fx.Paused = false;
        }

        // ------------------------------------------------------------------ update

        void Update()
        {
            Keyboard kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.f5Key.wasPressedThisFrame) Restart();
                if (kb.mKey.wasPressedThisFrame) NextMode();
            }

            if (State == MatchState.Picking)
            {
                if (Time.unscaledTime >= m_PickEnds)
                {
                    for (int i = 0; i < m_Picked.Length; i++) if (!m_Picked[i]) Pick(i, m_Rng.Next(Offers[i].Length));
                    EndPicking();
                }
                return;
            }
            if (State != MatchState.Playing) return;

            float dt = Time.deltaTime;
            Elapsed += dt;
            TimeLeft -= dt;
            PhaseTimeLeft -= dt;
            StepPoints(dt);
            StepTerritory(dt);
            StepRespawns();
            if (State == MatchState.Playing && PhaseTimeLeft <= 0f)
            {
                if (PhaseIndex + 1 < phases.Length) BeginPhase(PhaseIndex + 1);
                else End("Time up", -1);
            }
        }

        int Pts(float basePoints) { return Mathf.RoundToInt(basePoints * CurrentPhase.scoreMultiplier); }

        void StepPoints(float dt)
        {
            ControlPoint[] points = Layout.controlPoints;
            for (int p = 0; p < points.Length; p++)
            {
                ControlPoint cp = points[p];
                Array.Clear(m_Counts, 0, m_Counts.Length);
                Array.Clear(m_Power, 0, m_Power.Length);
                uint mask = 0;
                for (int i = 0; i < Tanks.Length; i++)
                {
                    TankUnit t = Tanks[i];
                    if (t.IsDead || !cp.Contains(t.transform.position)) continue;
                    m_Counts[t.team]++;
                    m_Power[t.team] += t.Mods.captureMult;
                    mask |= 1u << i;
                }
                m_Presence[p] = mask;

                CapturePointModel.Change ch = cp.Step(dt, m_Counts, m_Power, out int team);
                switch (ch)
                {
                    case CapturePointModel.Change.Captured:
                        AwardPresent(mask, team, Pts(score.capture));
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
                                if ((mask & (1u << i)) != 0 && Players[i].team != cp.Owner) { Players[i].score += Pts(score.contest); Players[i].contests++; }
                            AddLog(cp.label + " is contested");
                        }
                        break;
                }
            }
        }

        void AwardPresent(uint mask, int team, int points)
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
                if (points[p].Owner >= 0)
                {
                    CurrentShare[points[p].Owner] += points[p].weight;
                    // the flag pays its owner every second; later phases pay more
                    TeamIncome[points[p].Owner] += flagIncomePerSecond * points[p].weight * CurrentPhase.scoreMultiplier * dt;
                }
                else unowned += points[p].weight;
            }
            if (total <= 0f) return;
            for (int t = 0; t < CurrentShare.Length; t++)
            {
                CurrentShare[t] /= total;
                m_ControlSeconds[t] += CurrentShare[t] * dt;
                AverageShare[t] = Elapsed > 0f ? m_ControlSeconds[t] / Elapsed : 0f;
            }
            NeutralShare = unowned / total;

            if (dominationSeconds <= 0f) return;
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
            m_RespawnAt[victim.slot] = Time.time + CurrentPhase.respawnSeconds;

            TankUnit killer = victim.Killer;
            if (killer != null && killer.team != victim.team && State != MatchState.Ended)
            {
                PlayerStats k = Players[killer.slot];
                k.kills++;
                k.score += Pts(score.kill * killer.Mods.killScoreMult);
                for (int s = 0; s < Players.Length; s++)
                    if ((victim.AssistMask & (1 << s)) != 0 && Players[s].team != victim.team) { Players[s].assists++; Players[s].score += Pts(score.assist * Tanks[s].Mods.killScoreMult); }

                // defense: the victim died at a point the killer's team owns
                foreach (ControlPoint cp in Layout.controlPoints)
                    if (cp.Owner == killer.team && cp.Contains(victim.transform.position, score.defenseRadiusExtra))
                    {
                        k.defenses++;
                        k.score += Pts(score.defense);
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
            Time.timeScale = 1f;
            EndReason = reason;
            int best = -1;
            if (forcedWinner >= 0) best = forcedWinner;
            else
            {
                // highest total score wins; equal scores fall back to who held more flags over the match, then a draw
                int top = int.MinValue; bool tie = false;
                for (int t = 0; t < TeamIncome.Length; t++)
                {
                    int sc = TeamScore(t);
                    if (sc > top) { top = sc; best = t; tie = false; }
                    else if (sc == top) tie = true;
                }
                if (tie)
                {
                    float topShare = -1f; bool shareTie = false; best = -1;
                    for (int t = 0; t < TeamIncome.Length; t++)
                    {
                        if (TeamScore(t) != top) continue;
                        if (AverageShare[t] > topShare + 0.0005f) { topShare = AverageShare[t]; best = t; shareTie = false; }
                        else if (Mathf.Abs(AverageShare[t] - topShare) <= 0.0005f) shareTie = true;
                    }
                    if (shareTie) best = -1;
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
            if (mobileInput != null) mobileInput.enabled = false;
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
                float sc = occupied ? nearest - 1000f : nearest;
                if (sc > bestDist) { bestDist = sc; best = p.position; }
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
