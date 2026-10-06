using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace TankGame.Prototype.Editor
{
    /// <summary>
    /// Scripted check of the objective match (no human input): the capture rules in isolation, then a full bot-only
    /// match in Play mode with an overview camera, then startup checks of the other team configurations.
    /// Run MatchSmokeTest.Begin(); output goes to Library/AgentKit/match_smoke/.
    /// </summary>
    [InitializeOnLoad]
    public static class MatchSmokeTest
    {
        const string KActive = "TankMatch.Smoke.Active";
        const string KPrevScene = "TankMatch.Smoke.PrevScene";
        const string ScenePath = "Assets/Scenes/Match_Prototype.unity";
        const float MatchSeconds = 100f;

        struct Due { public double time; public string name; }

        static readonly List<Due> s_Due = new List<Due>();
        static readonly List<string> s_Msgs = new List<string>();
        static readonly List<string> s_Presets = new List<string>();
        static int s_Step, s_StartFrame, s_Errors, s_PresetIndex, s_ContestedFrames, s_Frames;
        static double s_T0, s_StepT;
        static string s_Dir;
        static MatchManager s_Match;
        static CameraRig s_Rig;
        static GameObject s_Anchor;
        static float s_FirstCaptureAt = -1f, s_MaxShare;
        static string s_ModelResult, s_LayoutResult;
        static TankUnit s_Tank;
        static int s_ShotsFired, s_ShotsBeforeReload, s_AmmoAtReload, s_AmmoAfterReload, s_HpBefore, s_HpAfter;
        static float s_DashDistance, s_DashCooldown, s_ReloadSeconds;
        static double s_ReloadStart;
        static bool s_ReloadSeen, s_PickupConsumed;
        static Vector3 s_Pos0;
        static float s_TopSpeed, s_TurnMinSpeed, s_TurnMaxLateral, s_StopSeconds;
        static float s_MgShots, s_MgAmmoUsed;
        static int s_RocketShots, s_ShieldHpAfter1, s_HpAfterShield2, s_PrimaryRestored;
        static bool s_RevertedToPrimary, s_FlagDecayed;
        static float s_SpeedMult, s_DamageMult, s_FlagDecayAt = -1f;
        static double s_PhaseT;
        static Touchscreen s_Touch;
        static float s_TouchMoved, s_TouchAimDot, s_TouchDashCd;
        static bool s_TouchFired, s_TouchIdle, s_TouchSticksSeen;
        static int s_TouchShots, s_TouchCountSeen, s_TouchDevices;
        static int s_MaxPhase, s_CoresMin, s_PickState = -1, s_PickScale = -1, s_PickCores = -1, s_ItemKinds;
        static string s_OffersA, s_OffersB, s_ItemsA, s_ItemsB;
        static bool s_WinnerTopScore, s_FinalMultiplier;
        static float s_IncomeTotal;
        static int s_Phase;
        static MatchMode[] s_Modes = { MatchMode.TwoTeams3v2, MatchMode.ThreeTeams221, MatchMode.Solo5 };

        static bool s_Hooked, s_HudOnly;

        static MatchSmokeTest()
        {
            EditorApplication.playModeStateChanged += OnState;
            // after the Play-mode domain reload the state event can be missed: hook from here too
            EditorApplication.delayCall += () => { if (SessionState.GetBool(KActive, false) && EditorApplication.isPlaying && !s_Hooked) Hook(); };
        }

        public static string Begin()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return "already in play mode";
            Scene prev = SceneManager.GetActiveScene();
            if (prev.isDirty) return "active scene has unsaved changes; not switching";
            SessionState.SetString(KPrevScene, prev.path);
            SessionState.SetBool(KActive, true);
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            EditorApplication.EnterPlaymode();
            return "started";
        }

        static void OnState(PlayModeStateChange change)
        {
            if (!SessionState.GetBool(KActive, false)) return;
            if (change == PlayModeStateChange.EnteredPlayMode) { if (!s_Hooked) Hook(); }
            else if (change == PlayModeStateChange.EnteredEditMode)
            {
                Unhook();
                SessionState.SetBool(KActive, false);
                string prev = SessionState.GetString(KPrevScene, "");
                if (!string.IsNullOrEmpty(prev)) EditorSceneManager.OpenScene(prev, OpenSceneMode.Single);
            }
        }

        static void Hook()
        {
            s_Hooked = true;
            s_Dir = Path.Combine(Directory.GetCurrentDirectory(), "Library", "AgentKit", "match_smoke");
            Directory.CreateDirectory(s_Dir);
            s_Step = 0; s_T0 = Time.realtimeSinceStartupAsDouble; s_StartFrame = Time.frameCount;
            s_Due.Clear(); s_Msgs.Clear(); s_Presets.Clear();
            s_Errors = 0; s_PresetIndex = 0; s_ContestedFrames = 0; s_Frames = 0; s_MaxPhase = 0; s_FirstCaptureAt = -1f; s_MaxShare = 0f; s_ReloadSeen = false; s_PickupConsumed = false; s_FlagDecayed = false; s_FlagDecayAt = -1f;
            s_HudOnly = SessionState.GetBool("TankMatch.Smoke.HudOnly", false);
            s_ModelResult = ModelSelfTest();
            s_LayoutResult = LayoutSelfTest();
            Application.logMessageReceived += OnLog;
            EditorApplication.update += Tick;
        }

        static void Unhook()
        {
            s_Hooked = false;
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= OnLog;
        }

        static void OnLog(string condition, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            {
                s_Errors++;
                if (s_Msgs.Count < 8) s_Msgs.Add(type + ": " + condition);
            }
        }

        // ------------------------------------------------------------------ capture rules in isolation

        static string LayoutSelfTest()
        {
            // phones (several aspect ratios, with and without a notch) and desktop: nothing may overlap, everything stays on screen
            var cases = new[]
            {
                new object[] { "phone19.5:9", 857f, 440f, new Rect(34f, 0f, 857f - 68f, 440f - 14f), true },
                new object[] { "phone16:9", 782f, 440f, new Rect(0f, 0f, 782f, 440f), true },
                new object[] { "phone4:3", 587f, 440f, new Rect(0f, 0f, 587f, 440f), true },
                new object[] { "desktop16:9", 1280f, 720f, new Rect(0f, 0f, 1280f, 720f), false },
                new object[] { "desktop4:3", 960f, 720f, new Rect(0f, 0f, 960f, 720f), false },
            };
            var sb = new StringBuilder();
            foreach (object[] c in cases)
            {
                HudLayout l = HudLayout.Compute((float)c[1], (float)c[2], (Rect)c[3], (bool)c[4]);
                var items = new System.Collections.Generic.List<Rect>(l.Reserved());
                int bad = 0;
                for (int i = 0; i < items.Count; i++)
                {
                    if (items[i].xMin < l.safe.xMin - 0.01f || items[i].xMax > l.safe.xMax + 0.01f || items[i].yMin < l.safe.yMin - 0.01f || items[i].yMax > l.safe.yMax + 0.01f) bad++;
                    for (int j = i + 1; j < items.Count; j++) if (items[i].Overlaps(items[j])) bad++;
                }
                if ((bool)c[4])
                {
                    Rect[] thumbs = l.ThumbZones();
                    for (int t = 0; t < thumbs.Length; t++)
                    {
                        foreach (Rect r in items) if (r.Overlaps(thumbs[t])) bad++;
                        if (thumbs[t].xMin < l.safe.xMin || thumbs[t].xMax > l.safe.xMax || thumbs[t].yMax > l.safe.yMax) bad++;
                    }
                    if (thumbs[0].Overlaps(thumbs[1])) bad++;
                }
                sb.Append(c[0]).Append('=').Append(bad).Append(' ');
            }
            return sb.ToString().Trim();
        }

        static string ModelSelfTest()
        {
            var sb = new StringBuilder();
            // 1: one team alone captures a neutral point in about CaptureSeconds
            var m = new CapturePointModel { CaptureSeconds = 6f };
            var c = new int[3];
            c[0] = 1;
            float t = 0f; int guard = 0;
            while (m.Owner != 0 && guard++ < 10000) { m.Tick(0.02f, c, out _); t += 0.02f; }
            sb.Append("neutral_capture_1_tank_s=").Append(t.ToString("0.00")).Append(';');
            // 2: two tanks capture faster than one but less than twice as fast (diminishing returns)
            var m2 = new CapturePointModel { CaptureSeconds = 6f };
            c[0] = 2; float t2 = 0f; guard = 0;
            while (m2.Owner != 0 && guard++ < 10000) { m2.Tick(0.02f, c, out _); t2 += 0.02f; }
            sb.Append("neutral_capture_2_tanks_s=").Append(t2.ToString("0.00")).Append(';');
            // 3: two teams inside freezes progress and reports a contest once
            var m3 = new CapturePointModel { CaptureSeconds = 6f };
            c[0] = 1; for (int i = 0; i < 150; i++) m3.Tick(0.02f, c, out _);
            float before = m3.CapProgress;
            c[1] = 1; int contestEvents = 0;
            for (int i = 0; i < 150; i++) if (m3.Tick(0.02f, c, out _) == CapturePointModel.Change.ContestStarted) contestEvents++;
            sb.Append("contest_frozen=").Append(Mathf.Approximately(before, m3.CapProgress) ? "true" : "false").Append(";contest_events=").Append(contestEvents).Append(';');
            // 4: an owned point must be neutralized first, then captured: flip takes about twice as long
            var m4 = new CapturePointModel { CaptureSeconds = 6f, Owner = 0, OwnerHold = 1f };
            c[0] = 0; c[1] = 1; float t4 = 0f; guard = 0; bool neutralized = false;
            while (m4.Owner != 1 && guard++ < 20000)
            {
                if (m4.Tick(0.02f, c, out _) == CapturePointModel.Change.Neutralized) neutralized = true;
                t4 += 0.02f;
            }
            sb.Append("flip_owned_point_s=").Append(t4.ToString("0.00")).Append(";passed_through_neutral=").Append(neutralized ? "true" : "false").Append(';');
            // 4b: an owned point nobody stands on bleeds its hold away and goes neutral after DecaySeconds
            var m4b = new CapturePointModel { CaptureSeconds = 6f, DecaySeconds = 45f, Owner = 0, OwnerHold = 1f };
            var none = new int[3]; float t4b = 0f; guard = 0; bool decayed = false;
            while (m4b.Owner != CapturePointModel.None && guard++ < 20000)
            {
                if (m4b.Tick(0.02f, none, out _) == CapturePointModel.Change.Decayed) decayed = true;
                t4b += 0.02f;
            }
            sb.Append("undefended_to_neutral_s=").Append(t4b.ToString("0.0")).Append(";decayed_event=").Append(decayed ? "true" : "false").Append(';');
            // 4c: a defender standing on a weakened point refills it faster than an attacker captures
            var m4c = new CapturePointModel { CaptureSeconds = 6f, Owner = 0, OwnerHold = 0.2f };
            var own = new int[3]; own[0] = 1; float t4c = 0f; guard = 0;
            while (m4c.OwnerHold < 1f && guard++ < 20000) { m4c.Tick(0.02f, own, out _); t4c += 0.02f; }
            sb.Append("defender_refill_from_20pct_s=").Append(t4c.ToString("0.00")).Append(';');
            // 5: five independent teams (solo) work with the same model
            var m5 = new CapturePointModel { CaptureSeconds = 6f };
            var c5 = new int[5]; c5[4] = 1; guard = 0;
            while (m5.Owner != 4 && guard++ < 10000) m5.Tick(0.02f, c5, out _);
            sb.Append("solo_5_teams_owner=").Append(m5.Owner);
            return sb.ToString();
        }

        // ------------------------------------------------------------------ play-mode flow

        static void Tick()
        {
            double now = Time.realtimeSinceStartupAsDouble;
            if (now - s_T0 > 150.0) { Finish("timeout in step " + s_Step); return; }
            for (int i = s_Due.Count - 1; i >= 0; i--)
                if (now >= s_Due[i].time) { Capture(s_Due[i].name); s_Due.RemoveAt(i); }

            switch (s_Step)
            {
                case 0: // set up an overview camera and restart the match with bots only
                {
                    if (Time.frameCount - s_StartFrame < 30) return;
                    s_Match = Object.FindFirstObjectByType<MatchManager>();
                    if (s_Match == null) { Finish("MatchManager not found"); return; }
                    s_Rig = s_Match.cameraRig;
                    s_Anchor = new GameObject("OverviewAnchor");
                    s_Anchor.transform.position = new Vector3(0f, 0f, 0f);
                    s_Rig.offset = new Vector3(0f, 95f, -55f);
                    s_Match.allBots = true;
                    s_Match.matchSeconds = MatchSeconds;
                    s_Match.StartMatch(MatchConfig.Preset(MatchMode.TwoTeams2v2));
                    s_Rig.target = s_Anchor.transform;
                    s_StepT = now;
                    Schedule(10, "a_overview_10s"); Schedule(35, "b_overview_35s"); Schedule(65, "c_overview_65s");
                    s_Step = s_HudOnly ? 41 : 1;
                    s_Phase = 0;
                    break;
                }
                case 1: // let the match run to its end
                {
                    s_Frames++;
                    s_MaxPhase = Mathf.Max(s_MaxPhase, s_Match.PhaseIndex);
                    foreach (ControlPoint cp in s_Match.Layout.controlPoints)
                    {
                        if (cp.Contested) { s_ContestedFrames++; break; }
                    }
                    for (int t = 0; t < s_Match.CurrentShare.Length; t++) if (s_Match.CurrentShare[t] > s_MaxShare) s_MaxShare = s_Match.CurrentShare[t];
                    if (s_FirstCaptureAt < 0f)
                        foreach (ControlPoint cp in s_Match.Layout.controlPoints) if (cp.Owner >= 0) s_FirstCaptureAt = s_Match.Elapsed;
                    if (s_Match.State == MatchManager.MatchState.Ended)
                    {
                        s_CoresMin = int.MaxValue;
                        foreach (PlayerStats ps in s_Match.Players) s_CoresMin = Mathf.Min(s_CoresMin, ps.cores);
                        int bestTeam = 0;
                        for (int t = 1; t < s_Match.AverageShare.Length; t++) if (s_Match.TeamScore(t) > s_Match.TeamScore(bestTeam)) bestTeam = t;
                        s_WinnerTopScore = s_Match.IsDraw || s_Match.WinnerTeam == bestTeam || s_Match.TeamScore(s_Match.WinnerTeam) == s_Match.TeamScore(bestTeam);
                        s_FinalMultiplier = Mathf.Approximately(s_Match.phases[s_Match.phases.Length - 1].scoreMultiplier, 2f);
                        s_IncomeTotal = 0f; foreach (float inc in s_Match.TeamIncome) s_IncomeTotal += inc;
                        WriteMain();
                        s_Step = 30;
                    }
                    break;
                }
                case 40: // touch controls with simulated multi-touch: left stick drives, right stick aims and fires, dash button
                {
                    if (s_Phase == 0)
                    {
                        InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
                        s_Touch = InputSystem.AddDevice<Touchscreen>();
                        s_Match.allBots = false; s_Match.coresEnabled = false; s_Match.forceMobileControls = true;
                        s_Match.StartMatch(MatchConfig.Preset(MatchMode.TwoTeams2v2));
                        s_Rig.target = s_Anchor.transform;
                        s_Tank = s_Match.Tanks[s_Match.LocalSlot];
                        s_Tank.Respawn(new Vector3(-40f, 0f, -52f), Quaternion.identity);
                        s_ShotsFired = 0; s_Tank.Fired += u => s_TouchShots++;
                        s_TouchShots = 0;
                        s_Pos0 = s_Tank.transform.position;
                        var m = s_Match.mobileInput;
                        s_TouchSticksSeen = m != null && m.enabled;
                        float y = Screen.height * 0.3f, R = m.StickRadius;
                        Touch(1, new Vector2(Screen.width * 0.2f, y), UnityEngine.InputSystem.TouchPhase.Began);
                        Touch(2, new Vector2(Screen.width * 0.8f, y), UnityEngine.InputSystem.TouchPhase.Began);
                        s_PhaseT = now; s_Phase = 1;
                    }
                    else if (s_Phase == 1 && now - s_PhaseT > 0.2)
                    {
                        float y = Screen.height * 0.3f, R = s_Match.mobileInput.StickRadius;
                        Touch(1, new Vector2(Screen.width * 0.2f + R, y), UnityEngine.InputSystem.TouchPhase.Moved);          // push the move stick right
                        Touch(2, new Vector2(Screen.width * 0.8f, y + R), UnityEngine.InputSystem.TouchPhase.Moved);          // push the aim stick up
                        Schedule(0.6, "hud_play_mobile");
                        s_PhaseT = now; s_Phase = 2;
                    }
                    else if (s_Phase == 2 && now - s_PhaseT > 1.0)
                    {
                        s_TouchCountSeen = s_Match.mobileInput.ActiveTouchCount; s_TouchDevices = InputSystem.devices.Count;
                        s_TouchMoved = s_Tank.transform.position.x - s_Pos0.x;
                        Vector3 aim = s_Tank.Command.AimPoint - s_Tank.transform.position; aim.y = 0f;
                        s_TouchAimDot = Vector3.Dot(aim.normalized, Vector3.forward);
                        s_TouchFired = s_TouchShots > 0;
                        // release both, then tap the dash button
                        float y = Screen.height * 0.3f, R = s_Match.mobileInput.StickRadius;
                        Touch(1, new Vector2(Screen.width * 0.2f + R, y), UnityEngine.InputSystem.TouchPhase.Ended);
                        Touch(2, new Vector2(Screen.width * 0.8f, y + R), UnityEngine.InputSystem.TouchPhase.Ended);
                        s_PhaseT = now; s_Phase = 3;
                    }
                    else if (s_Phase == 3 && now - s_PhaseT > 0.4)
                    {
                        s_TouchIdle = s_Tank.Command.Move == Vector2.zero && !s_Tank.Command.Fire;
                        Touch(3, s_Match.mobileInput.DashCenter, UnityEngine.InputSystem.TouchPhase.Began);
                        s_PhaseT = now; s_Phase = 4;
                    }
                    else if (s_Phase == 4 && now - s_PhaseT > 0.3)
                    {
                        s_TouchDashCd = s_Tank.Skill.CooldownRemaining;
                        Touch(3, s_Match.mobileInput.DashCenter, UnityEngine.InputSystem.TouchPhase.Ended);
                        InputSystem.RemoveDevice(s_Touch);
                        s_Match.forceMobileControls = false; s_Match.allBots = true;
                        s_Phase = 0; s_Step = 41;
                    }
                    break;
                }
                case 41: // HUD screenshots: core pick and end screen on the phone layout
                {
                    if (s_Phase == 0)
                    {
                        s_Match.allBots = false; s_Match.coresEnabled = true; s_Match.forceMobileControls = true;
                        s_Match.StartMatch(MatchConfig.Preset(MatchMode.TwoTeams2v2));
                        Schedule(0.3, "hud_pick_mobile");
                        s_PhaseT = now; s_Phase = 1;
                    }
                    else if (s_Phase == 1 && now - s_PhaseT > 0.7) { s_Match.PickCore(0); s_PhaseT = now; s_Phase = 2; }
                    else if (s_Phase == 2 && now - s_PhaseT > 1.5) { s_Match.EndNow(); Schedule(0.3, "hud_end_mobile"); s_PhaseT = now; s_Phase = 3; }
                    else if (s_Phase == 3 && now - s_PhaseT > 0.8)
                    {
                        s_Match.forceMobileControls = false; s_Match.allBots = true; s_Match.coresEnabled = false;
                        if (s_HudOnly) { Finish(null); return; }
                        s_Phase = 0; s_Step = 10;
                    }
                    break;
                }
                case 30: // same seed => same core offers and same item roll; different seed => different
                {
                    s_Match.allBots = true; s_Match.coresEnabled = true; s_Match.seed = 1234;
                    s_Match.StartMatch(MatchConfig.Preset(MatchMode.TwoTeams2v2)); s_OffersA = Offers(); s_ItemsA = Items();
                    s_Match.StartMatch(MatchConfig.Preset(MatchMode.TwoTeams2v2)); s_OffersB = Offers(); s_ItemsB = Items();
                    var kinds = new System.Collections.Generic.HashSet<string>();
                    foreach (Pickup pk in s_Match.Layout.pickups) if (pk.Available) kinds.Add(pk.label);
                    s_ItemKinds = kinds.Count;
                    s_Match.seed = 0;
                    s_Step = 31;
                    break;
                }
                case 31: // a human player: the match freezes for the pick and resumes right after it
                {
                    s_Match.allBots = false; s_Match.coresEnabled = true;
                    s_Match.StartMatch(MatchConfig.Preset(MatchMode.TwoTeams2v2));
                    s_PickState = (int)s_Match.State;                         // expect Picking (0)
                    s_PickScale = Mathf.RoundToInt(Time.timeScale * 10f);       // expect 0
                    s_Match.PickCore(1);
                    s_PickCores = s_Match.Tanks[s_Match.LocalSlot].Cores.Count;   // expect 1
                    s_Match.allBots = true; s_Match.coresEnabled = false;
                    s_Phase = 0; s_Step = 40;
                    break;
                }
                case 10: // mechanics: dash, magazine/reload, repair pickup on a bot-free 2v2
                {
                    s_Match.coresEnabled = false;
                    s_Match.StartMatch(MatchConfig.Preset(MatchMode.TwoTeams2v2));
                    s_Rig.target = s_Anchor.transform;
                    foreach (BotBrain b in s_Match.Bots) if (b != null) b.enabled = false;
                    s_Tank = s_Match.Tanks[0];
                    s_ShotsFired = 0;
                    s_Tank.Fired += u => s_ShotsFired++;
                    s_Pos0 = s_Tank.transform.position;
                    s_Tank.Command = new TankCommand { Move = new Vector2(1f, 0f), AimPoint = s_Pos0 + Vector3.forward * 30f, Skill = true };
                    s_StepT = now;
                    s_Step = 11;
                    break;
                }
                case 11:
                {
                    if (now - s_StepT < 0.4) return;
                    s_DashDistance = Vector3.Distance(s_Tank.transform.position, s_Pos0);
                    s_DashCooldown = s_Tank.Skill.CooldownRemaining;
                    s_Tank.Command = new TankCommand { AimPoint = s_Tank.transform.position + Vector3.forward * 30f, Fire = true };
                    s_StepT = now;
                    s_Step = 12;
                    break;
                }
                case 12:
                {
                    if (!s_ReloadSeen && s_Tank.IsReloading) { s_ReloadSeen = true; s_ShotsBeforeReload = s_ShotsFired; s_AmmoAtReload = s_Tank.Ammo; s_ReloadStart = now; s_Tank.Command = new TankCommand { AimPoint = s_Tank.Command.AimPoint }; }
                    if (s_ReloadSeen && !s_Tank.IsReloading) { s_ReloadSeconds = (float)(now - s_ReloadStart); s_AmmoAfterReload = s_Tank.Ammo; s_StepT = now; s_Step = 13; }
                    else if (now - s_StepT > 10.0) { s_StepT = now; s_Step = 13; }
                    break;
                }
                case 13: // damage the tank and put it on a repair pickup
                {
                    Pickup pk = s_Match.Layout.pickups[0];
                    pk.Force(PickupKind.Repair);                                  // items are random: make this slot a repair kit
                    s_Tank.Command = default;
                    s_Tank.TakeDamage(60, s_Tank.transform.position, Vector3.forward, null);
                    s_HpBefore = s_Tank.Hp;
                    s_Tank.Body.position = pk.transform.position;
                    s_Tank.transform.position = pk.transform.position;
                    s_StepT = now;
                    s_Step = 14;
                    break;
                }
                case 14:
                {
                    if (now - s_StepT < 0.7) return;
                    s_HpAfter = s_Tank.Hp;
                    s_PickupConsumed = !s_Match.Layout.pickups[0].Available;
                    s_Step = 20; s_Phase = 0;
                    break;
                }
                case 20: // driving feel: top speed, speed kept through a 90 degree turn, sideways slip, stopping time
                {
                    if (s_Phase == 0)
                    {
                        s_Tank.Respawn(new Vector3(-40f, 0f, -52f), Quaternion.Euler(0f, 90f, 0f));   // open lane along the south wall
                        s_Tank.Command = new TankCommand { Move = new Vector2(1f, 0f), AimPoint = new Vector3(0f, 0.8f, 0f) };
                        s_PhaseT = now; s_Phase = 1; s_TurnMinSpeed = 999f; s_TurnMaxLateral = 0f;
                    }
                    else if (s_Phase == 1 && now - s_PhaseT > 1.0)
                    {
                        s_TopSpeed = s_Tank.Body.linearVelocity.magnitude;
                        s_Tank.Command = new TankCommand { Move = new Vector2(0f, 1f), AimPoint = new Vector3(0f, 0.8f, 0f) };   // 90 degree turn at full speed
                        s_PhaseT = now; s_Phase = 2;
                    }
                    else if (s_Phase == 2)
                    {
                        Vector3 v = s_Tank.Body.linearVelocity;
                        s_TurnMinSpeed = Mathf.Min(s_TurnMinSpeed, v.magnitude);
                        s_TurnMaxLateral = Mathf.Max(s_TurnMaxLateral, Mathf.Abs(Vector3.Dot(v, s_Tank.transform.right)));
                        if (now - s_PhaseT > 0.5) { s_Tank.Command = new TankCommand { AimPoint = new Vector3(0f, 0.8f, 0f) }; s_PhaseT = now; s_Phase = 3; }
                    }
                    else if (s_Phase == 3)
                    {
                        if (s_Tank.Body.linearVelocity.magnitude < 0.5f || now - s_PhaseT > 1.5) { s_StopSeconds = (float)(now - s_PhaseT); s_Step = 21; s_Phase = 0; }
                    }
                    break;
                }
                case 21: // special weapons: machine gun burst, rocket magazine runs dry and reverts to the cannon
                {
                    if (s_Phase == 0)
                    {
                        var mg = AssetDatabase.LoadAssetAtPath<WeaponDef>("Assets/Prototype/Data/Weapon_Machine Gun.asset");
                        s_Tank.GrantWeapon(mg);
                        s_ShotsFired = 0; s_MgAmmoUsed = s_Tank.Ammo;
                        s_Tank.Command = new TankCommand { AimPoint = s_Tank.transform.position + Vector3.forward * 30f, Fire = true };
                        s_PhaseT = now; s_Phase = 1;
                    }
                    else if (s_Phase == 1 && now - s_PhaseT > 0.8)
                    {
                        s_MgShots = s_ShotsFired; s_MgAmmoUsed -= s_Tank.Ammo;
                        s_Tank.Command = default;
                        var rk = AssetDatabase.LoadAssetAtPath<WeaponDef>("Assets/Prototype/Data/Weapon_Rocket.asset");
                        s_Tank.GrantWeapon(rk);
                        s_ShotsFired = 0;
                        s_Tank.Command = new TankCommand { AimPoint = s_Tank.transform.position + Vector3.forward * 30f, Fire = true };
                        s_PhaseT = now; s_Phase = 2;
                    }
                    else if (s_Phase == 2 && (!s_Tank.HasSpecialWeapon || now - s_PhaseT > 8.0))
                    {
                        s_RocketShots = s_ShotsFired;
                        s_RevertedToPrimary = !s_Tank.HasSpecialWeapon;
                        s_PrimaryRestored = s_Tank.Ammo;
                        s_Tank.Command = default;
                        s_Step = 22;
                    }
                    break;
                }
                case 22: // shield absorbs damage first; boosts report their multipliers
                {
                    s_Tank.Respawn(new Vector3(0f, 0f, -48f), Quaternion.identity);
                    s_Tank.GiveShield(50, 10f, Color.cyan);
                    s_Tank.TakeDamage(30, s_Tank.transform.position, Vector3.forward, null);
                    s_ShieldHpAfter1 = s_Tank.ShieldHp;
                    s_Tank.TakeDamage(30, s_Tank.transform.position, Vector3.forward, null);
                    s_HpAfterShield2 = s_Tank.Hp;                         // 100 - (30 - 20 left in the shield) = 90
                    s_Tank.GiveSpeed(1.4f, 8f, Color.yellow); s_SpeedMult = s_Tank.SpeedMultiplier;
                    s_Tank.GiveDamage(1.5f, 10f, Color.red); s_DamageMult = s_Tank.DamageMultiplier;
                    // an owned flag with nobody inside loses its hold and falls back to neutral
                    ControlPoint far = s_Match.Layout.controlPoints[4];   // E, far from every tank
                    far.Model.Owner = 0; far.Model.OwnerHold = 1f; far.Model.DecaySeconds = 4f;
                    s_PhaseT = now; s_Step = 23;
                    break;
                }
                case 23:
                {
                    ControlPoint far = s_Match.Layout.controlPoints[4];
                    if (far.Owner < 0) { s_FlagDecayed = true; s_FlagDecayAt = (float)(now - s_PhaseT); }
                    if (far.Owner < 0 || now - s_PhaseT > 8.0)
                    {
                        s_PresetIndex = 0;
                        StartPreset(now);
                        s_Step = 2;
                    }
                    break;
                }
                case 2: // other team configurations: start cleanly and run a few seconds
                {
                    if (now - s_StepT < 6.0) return;
                    s_Presets.Add(DescribeCurrent());
                    s_PresetIndex++;
                    if (s_PresetIndex >= s_Modes.Length) { Finish(null); return; }
                    StartPreset(now);
                    break;
                }
            }
        }

        static void Touch(int id, Vector2 pos, UnityEngine.InputSystem.TouchPhase phase)
        {
            InputSystem.QueueStateEvent(s_Touch, new TouchState
            {
                touchId = id, phase = phase, position = pos, startPosition = pos, pressure = phase == UnityEngine.InputSystem.TouchPhase.Ended ? 0f : 1f, startTime = Time.timeAsDouble,
            });
            InputSystem.Update();
        }

        static string Offers()
        {
            var sb = new StringBuilder();
            foreach (CoreDef[] o in s_Match.Offers) { foreach (CoreDef c in o) sb.Append(c.id).Append(','); sb.Append('|'); }
            return sb.ToString();
        }

        static string Items()
        {
            var sb = new StringBuilder();
            foreach (Pickup p in s_Match.Layout.pickups) sb.Append(p.Available ? p.label : "-").Append(',');
            return sb.ToString();
        }

        static void StartPreset(double now)
        {
            s_Match.matchSeconds = 120f;
            s_Match.coresEnabled = true;
            s_Match.StartMatch(MatchConfig.Preset(s_Modes[s_PresetIndex]));
            s_Rig.target = s_Anchor.transform;
            s_StepT = now;
        }

        static string DescribeCurrent()
        {
            var tanks = s_Match.Tanks;
            float minD = float.MaxValue;
            for (int i = 0; i < tanks.Length; i++)
                for (int j = i + 1; j < tanks.Length; j++)
                    minD = Mathf.Min(minD, Vector3.Distance(tanks[i].transform.position, tanks[j].transform.position));
            bool inBounds = true;
            foreach (TankUnit t in tanks)
                if (Mathf.Abs(t.transform.position.x) > s_Match.Layout.arenaHalfSize + 1f || Mathf.Abs(t.transform.position.z) > s_Match.Layout.arenaHalfSize + 1f) inBounds = false;
            return "{\"mode\":\"" + s_Modes[s_PresetIndex] + "\",\"teams\":" + s_Match.Config.Teams.Count + ",\"players\":" + s_Match.Players.Length +
                   ",\"solo\":" + (s_Match.Config.IsSolo ? "true" : "false") + ",\"tanks_alive\":" + Alive() + ",\"min_tank_distance_m\":" + minD.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) +
                   ",\"all_in_bounds\":" + (inBounds ? "true" : "false") + ",\"share_sum_le_1\":" + (Sum(s_Match.CurrentShare) <= 1.0001f ? "true" : "false") + "}";
        }

        static int Alive() { int n = 0; foreach (TankUnit t in s_Match.Tanks) if (!t.IsDead) n++; return n; }
        static float Sum(float[] a) { float s = 0f; foreach (float v in a) s += v; return s; }

        static string F(float v) { return v.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture); }

        static void WriteMain()
        {
            var sb = new StringBuilder();
            sb.Append("{\n  \"winner_team\": ").Append(s_Match.WinnerTeam).Append(",\n  \"draw\": ").Append(s_Match.IsDraw ? "true" : "false")
              .Append(",\n  \"end_reason\": \"").Append(s_Match.EndReason).Append("\",\n  \"elapsed_s\": ").Append(F(s_Match.Elapsed))
              .Append(",\n  \"first_point_owned_at_s\": ").Append(F(s_FirstCaptureAt)).Append(",\n  \"contested_frames\": ").Append(s_ContestedFrames)
              .Append(",\n  \"max_current_share\": ").Append(F(s_MaxShare)).Append(",\n  \"teams\": [");
            for (int t = 0; t < s_Match.AverageShare.Length; t++)
                sb.Append(t > 0 ? "," : "").Append("{\"name\":\"").Append(s_Match.TeamName(t)).Append("\",\"avg_control\":").Append(F(s_Match.AverageShare[t]))
                  .Append(",\"final_now\":").Append(F(s_Match.CurrentShare[t])).Append(",\"score\":").Append(s_Match.TeamScore(t)).Append("}");
            sb.Append("],\n  \"avg_control_sum\": ").Append(F(Sum(s_Match.AverageShare))).Append(",\n  \"players\": [");
            for (int i = 0; i < s_Match.Players.Length; i++)
            {
                PlayerStats p = s_Match.Players[i];
                sb.Append(i > 0 ? "," : "").Append("{\"name\":\"").Append(p.name).Append("\",\"team\":").Append(p.team).Append(",\"K\":").Append(p.kills)
                  .Append(",\"D\":").Append(p.deaths).Append(",\"A\":").Append(p.assists).Append(",\"cap\":").Append(p.captures)
                  .Append(",\"def\":").Append(p.defenses).Append(",\"con\":").Append(p.contests).Append(",\"score\":").Append(p.score).Append("}");
            }
            sb.Append("]\n}\n");
            File.WriteAllText(Path.Combine(s_Dir, "main_match.json"), sb.ToString());
        }

        static void Schedule(double secondsFromNow, string name)
        {
            s_Due.Add(new Due { time = Time.realtimeSinceStartupAsDouble + secondsFromNow, name = name });
        }

        static void Finish(string failure)
        {
            EditorApplication.update -= Tick;
            var sb = new StringBuilder();
            sb.Append("{\n  \"failure\": ").Append(failure == null ? "null" : "\"" + failure + "\"").Append(",\n  \"capture_model\": \"").Append(s_ModelResult)
              .Append("\",\n  \"hud_layout_violations\": \"").Append(s_LayoutResult).Append("\",\n  \"phases\": {\"max_phase_index\":").Append(s_MaxPhase).Append(",\"cores_picked_min_per_player\":").Append(s_CoresMin)
              .Append(",\"winner_has_top_score\":").Append(s_WinnerTopScore ? "true" : "false").Append(",\"final_phase_x2\":").Append(s_FinalMultiplier ? "true" : "false")
              .Append(",\"flag_income_total\":").Append(F(s_IncomeTotal)).Append("},\n  \"randomness\": {\"same_seed_same_offers\":").Append(s_OffersA == s_OffersB ? "true" : "false")
              .Append(",\"same_seed_same_items\":").Append(s_ItemsA == s_ItemsB ? "true" : "false").Append(",\"item_types_present\":").Append(s_ItemKinds)
              .Append("},\n  \"human_pick\": {\"state_while_picking\":").Append(s_PickState).Append(",\"timescale_x10\":").Append(s_PickScale).Append(",\"cores_after_pick\":").Append(s_PickCores)
              .Append("},\n  \"touch\": {\"controls_enabled\":").Append(s_TouchSticksSeen ? "true" : "false").Append(",\"moved_right_m\":").Append(F(s_TouchMoved)).Append(",\"aim_up_dot\":").Append(F(s_TouchAimDot))
              .Append(",\"active_touches_seen\":").Append(s_TouchCountSeen).Append(",\"touchscreens\":").Append(s_TouchDevices).Append(",\"fired\":").Append(s_TouchFired ? "true" : "false").Append(",\"idle_after_release\":").Append(s_TouchIdle ? "true" : "false").Append(",\"dash_cooldown_after_tap\":").Append(F(s_TouchDashCd))
              .Append("},\n  \"mechanics\": {\"dash_distance_m\":").Append(F(s_DashDistance)).Append(",\"dash_cooldown_left_s\":").Append(F(s_DashCooldown))
              .Append(",\"shots_before_reload\":").Append(s_ShotsBeforeReload).Append(",\"ammo_when_reload_started\":").Append(s_AmmoAtReload).Append(",\"reload_seconds\":").Append(F(s_ReloadSeconds))
              .Append(",\"ammo_after_reload\":").Append(s_AmmoAfterReload).Append(",\"hp_before_pickup\":").Append(s_HpBefore).Append(",\"hp_after_pickup\":").Append(s_HpAfter)
              .Append(",\"pickup_consumed\":").Append(s_PickupConsumed ? "true" : "false").Append("},\n  \"drive\": {\"top_speed_mps\":").Append(F(s_TopSpeed))
              .Append(",\"min_speed_in_90deg_turn_mps\":").Append(F(s_TurnMinSpeed)).Append(",\"max_sideways_speed_in_turn_mps\":").Append(F(s_TurnMaxLateral))
              .Append(",\"stop_seconds\":").Append(F(s_StopSeconds)).Append("},\n  \"weapons\": {\"mg_shots_in_0_8s\":").Append(F(s_MgShots)).Append(",\"mg_ammo_used\":").Append(F(s_MgAmmoUsed))
              .Append(",\"rocket_shots_until_empty\":").Append(s_RocketShots).Append(",\"reverted_to_primary\":").Append(s_RevertedToPrimary ? "true" : "false")
              .Append(",\"primary_ammo_after_revert\":").Append(s_PrimaryRestored).Append("},\n  \"buffs\": {\"shield_left_after_30dmg\":").Append(s_ShieldHpAfter1)
              .Append(",\"hp_after_second_30dmg\":").Append(s_HpAfterShield2).Append(",\"speed_mult\":").Append(F(s_SpeedMult)).Append(",\"damage_mult\":").Append(F(s_DamageMult))
              .Append("},\n  \"flag_decay\": {\"decayed_to_neutral\":").Append(s_FlagDecayed ? "true" : "false").Append(",\"seconds\":").Append(F(s_FlagDecayAt)).Append("},\n  \"presets\": [").Append(string.Join(",", s_Presets)).Append("],\n  \"errors\": ").Append(s_Errors).Append(",\n  \"error_messages\": [");
            for (int i = 0; i < s_Msgs.Count; i++) sb.Append(i > 0 ? ", " : "").Append('"').Append(s_Msgs[i].Replace("\\", "/").Replace("\"", "'").Replace("\n", " ")).Append('"');
            sb.Append("],\n  \"timescale_restored\": ").Append(Mathf.Approximately(Time.timeScale, 1f) ? "true" : "false").Append("\n}\n");
            File.WriteAllText(Path.Combine(s_Dir, "result.json"), sb.ToString());
            if (s_Anchor != null) Object.Destroy(s_Anchor);
            EditorApplication.ExitPlaymode();
        }

        static void Capture(string name)
        {
            if (name.StartsWith("hud_")) { ScreenCapture.CaptureScreenshot(Path.Combine(s_Dir, name + ".png")); return; }
            Camera cam = Camera.main;
            if (cam == null) return;
            const int w = 1280, h = 720;
            RenderTexture rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var req = new RenderPipeline.StandardRequest { destination = rt };
            if (!RenderPipeline.SupportsRenderRequest(cam, req)) { RenderTexture.ReleaseTemporary(rt); return; }
            RenderPipeline.SubmitRenderRequest(cam, req);
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            File.WriteAllBytes(Path.Combine(s_Dir, name + ".png"), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }
    }
}
