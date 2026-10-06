// Development-player check of the objective match, independent of the Unity Editor: a bot-only match, then
// driving / weapons / buffs / flag decay mechanics, then startup of the other team configurations.
// Compiled only into Development Builds and active only with -matchProbe in the Match_Prototype scene.
// Throwaway tooling, not part of the game.
#if DEVELOPMENT_BUILD
using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TankGame.Prototype
{
    public sealed class MatchProbe : MonoBehaviour
    {
        MatchManager m_Match;
        TankUnit m_Tank;
        int m_Step, m_Phase, m_Shots, m_PresetIndex, m_Errors;
        float m_T;
        readonly StringBuilder m_Out = new StringBuilder();
        readonly StringBuilder m_Msgs = new StringBuilder();
        static readonly MatchMode[] Modes = { MatchMode.TwoTeams3v2, MatchMode.ThreeTeams221, MatchMode.Solo5 };

        // measurements
        float m_Top, m_TurnMin = 999f, m_TurnLat, m_Stop, m_Dash0, m_DashDist, m_DashCd, m_ReloadStart, m_ReloadSec;
        int m_MgShots, m_RocketShots, m_PrimaryAmmo, m_ShieldLeft, m_HpAfter, m_HpBefore, m_ShotsBeforeReload, m_AmmoAtReload, m_AmmoAfterReload;
        bool m_Reverted, m_ReloadSeen, m_PickupConsumed, m_FlagDecayed;
        float m_SpeedMult, m_DamageMult, m_FlagDecayAt = -1f;
        Vector3 m_Pos0;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-matchProbe") < 0) return;
            if (SceneManager.GetActiveScene().name != "Match_Prototype") return;
            new GameObject("MatchProbe").AddComponent<MatchProbe>();
        }

        static string Arg(string name)
        {
            var a = Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
            return null;
        }

        static string F(float v) { return v.ToString("0.###", CultureInfo.InvariantCulture); }

        void OnEnable() { Application.logMessageReceived += OnLog; }
        void OnDisable() { Application.logMessageReceived -= OnLog; }

        void OnLog(string c, string st, LogType t)
        {
            if (t == LogType.Error || t == LogType.Exception || t == LogType.Assert) { m_Errors++; if (m_Msgs.Length < 600) m_Msgs.Append(c.Replace("\"", "'").Replace("\n", " ")).Append(" | "); }
        }

        WeaponDef FindWeapon(string name)
        {
            foreach (Pickup p in m_Match.Layout.pickups) if (p.weapon != null && p.weapon.displayName == name) return p.weapon;
            return null;
        }

        void Update()
        {
            float now = Time.time;
            if (m_Match == null)
            {
                m_Match = FindFirstObjectByType<MatchManager>();
                if (m_Match == null || m_Match.Tanks == null) return;
                m_Match.allBots = true;
                m_Match.matchSeconds = 60f;
                m_Match.StartMatch(MatchConfig.Preset(MatchMode.TwoTeams2v2));
                m_Step = 1;
                return;
            }
            if (now > 400f) { Write("timeout in step " + m_Step); return; }

            switch (m_Step)
            {
                case 1: // full bot match
                    if (m_Match.State == MatchManager.MatchState.Ended)
                    {
                        var sb = m_Out;
                        sb.Append(" \"match\": {\"winner\":\"").Append(m_Match.IsDraw ? "draw" : m_Match.TeamName(m_Match.WinnerTeam)).Append("\",\"reason\":\"").Append(m_Match.EndReason)
                          .Append("\",\"elapsed\":").Append(F(m_Match.Elapsed)).Append(",\"points\":").Append(m_Match.Layout.controlPoints.Length).Append(",\"teams\":[");
                        for (int t = 0; t < m_Match.AverageShare.Length; t++)
                            sb.Append(t > 0 ? "," : "").Append("{\"name\":\"").Append(m_Match.TeamName(t)).Append("\",\"avg\":").Append(F(m_Match.AverageShare[t])).Append(",\"score\":").Append(m_Match.TeamScore(t)).Append("}");
                        sb.Append("],\"players\":[");
                        for (int i = 0; i < m_Match.Players.Length; i++)
                        {
                            PlayerStats p = m_Match.Players[i];
                            sb.Append(i > 0 ? "," : "").Append("{\"n\":\"").Append(p.name).Append("\",\"K\":").Append(p.kills).Append(",\"D\":").Append(p.deaths).Append(",\"A\":").Append(p.assists)
                              .Append(",\"cap\":").Append(p.captures).Append(",\"def\":").Append(p.defenses).Append(",\"con\":").Append(p.contests).Append(",\"score\":").Append(p.score).Append("}");
                        }
                        sb.Append("]},\n");
                        m_Step = 10;
                    }
                    break;

                case 10: // mechanics on an idle 2v2
                    m_Match.StartMatch(MatchConfig.Preset(MatchMode.TwoTeams2v2));
                    foreach (BotBrain b in m_Match.Bots) if (b != null) b.enabled = false;
                    m_Tank = m_Match.Tanks[0];
                    m_Tank.Fired += u => m_Shots++;
                    m_Tank.Respawn(new Vector3(0f, 0f, -48f), Quaternion.Euler(0f, 90f, 0f));
                    m_Phase = 0; m_Step = 11;
                    break;

                case 11: // driving: top speed, speed kept through a 90 degree turn, sideways slip, stopping time
                    if (m_Phase == 0) { m_Tank.Command = new TankCommand { Move = new Vector2(1f, 0f), AimPoint = new Vector3(0f, 0.8f, 0f) }; m_T = now; m_Phase = 1; }
                    else if (m_Phase == 1 && now - m_T > 1.0f) { m_Top = m_Tank.Body.linearVelocity.magnitude; m_Tank.Command = new TankCommand { Move = new Vector2(0f, 1f), AimPoint = new Vector3(0f, 0.8f, 0f) }; m_T = now; m_Phase = 2; }
                    else if (m_Phase == 2)
                    {
                        Vector3 v = m_Tank.Body.linearVelocity;
                        m_TurnMin = Mathf.Min(m_TurnMin, v.magnitude);
                        m_TurnLat = Mathf.Max(m_TurnLat, Mathf.Abs(Vector3.Dot(v, m_Tank.transform.right)));
                        if (now - m_T > 0.5f) { m_Tank.Command = new TankCommand { AimPoint = new Vector3(0f, 0.8f, 0f) }; m_T = now; m_Phase = 3; }
                    }
                    else if (m_Phase == 3 && (m_Tank.Body.linearVelocity.magnitude < 0.5f || now - m_T > 1.5f)) { m_Stop = now - m_T; m_Phase = 0; m_Step = 12; }
                    break;

                case 12: // dash
                    if (m_Phase == 0)
                    {
                        m_Tank.Respawn(new Vector3(0f, 0f, -48f), Quaternion.Euler(0f, 90f, 0f));
                        m_Pos0 = m_Tank.transform.position;
                        m_Tank.Command = new TankCommand { Move = new Vector2(1f, 0f), AimPoint = m_Pos0 + Vector3.forward * 30f, Skill = true };
                        m_T = now; m_Phase = 1;
                    }
                    else if (m_Phase == 1 && now - m_T > 0.4f)
                    {
                        m_DashDist = Vector3.Distance(m_Tank.transform.position, m_Pos0);
                        m_DashCd = m_Tank.Skill.CooldownRemaining;
                        m_Tank.Command = new TankCommand { AimPoint = m_Tank.transform.position + Vector3.forward * 30f, Fire = true };
                        m_Shots = 0; m_T = now; m_Phase = 0; m_Step = 13;
                    }
                    break;

                case 13: // cannon magazine and reload
                    if (!m_ReloadSeen && m_Tank.IsReloading) { m_ReloadSeen = true; m_ShotsBeforeReload = m_Shots; m_AmmoAtReload = m_Tank.Ammo; m_ReloadStart = now; m_Tank.Command = new TankCommand { AimPoint = m_Tank.Command.AimPoint }; }
                    if (m_ReloadSeen && !m_Tank.IsReloading) { m_ReloadSec = now - m_ReloadStart; m_AmmoAfterReload = m_Tank.Ammo; m_Phase = 0; m_Step = 14; }
                    else if (now - m_T > 10f) { m_Phase = 0; m_Step = 14; }
                    break;

                case 14: // machine gun burst, then rocket magazine to empty and back to the cannon
                    if (m_Phase == 0)
                    {
                        m_Tank.GrantWeapon(FindWeapon("Machine Gun"));
                        m_Shots = 0; m_MgShots = m_Tank.Ammo;
                        m_Tank.Command = new TankCommand { AimPoint = m_Tank.transform.position + Vector3.forward * 30f, Fire = true };
                        m_T = now; m_Phase = 1;
                    }
                    else if (m_Phase == 1 && now - m_T > 0.8f)
                    {
                        m_MgShots = m_Shots; // shots fired in 0.8 s
                        m_Tank.Command = default;
                        m_Tank.GrantWeapon(FindWeapon("Rocket"));
                        m_Shots = 0;
                        m_Tank.Command = new TankCommand { AimPoint = m_Tank.transform.position + Vector3.forward * 30f, Fire = true };
                        m_T = now; m_Phase = 2;
                    }
                    else if (m_Phase == 2 && (!m_Tank.HasSpecialWeapon || now - m_T > 8f))
                    {
                        m_RocketShots = m_Shots; m_Reverted = !m_Tank.HasSpecialWeapon; m_PrimaryAmmo = m_Tank.Ammo;
                        m_Tank.Command = default; m_Phase = 0; m_Step = 15;
                    }
                    break;

                case 15: // shield, boosts, repair pickup, undefended flag
                {
                    m_Tank.Respawn(new Vector3(0f, 0f, -48f), Quaternion.identity);
                    m_Tank.GiveShield(50, 10f, Color.cyan);
                    m_Tank.TakeDamage(30, m_Tank.transform.position, Vector3.forward, null);
                    m_ShieldLeft = m_Tank.ShieldHp;
                    m_Tank.TakeDamage(30, m_Tank.transform.position, Vector3.forward, null);
                    m_HpAfter = m_Tank.Hp;
                    m_Tank.GiveSpeed(1.4f, 8f, Color.yellow); m_SpeedMult = m_Tank.SpeedMultiplier;
                    m_Tank.GiveDamage(1.5f, 10f, Color.red); m_DamageMult = m_Tank.DamageMultiplier;
                    Pickup repair = null;
                    foreach (Pickup p in m_Match.Layout.pickups) if (p.kind == PickupKind.Repair) { repair = p; break; }
                    m_Tank.TakeDamage(40, m_Tank.transform.position, Vector3.forward, null);
                    m_HpBefore = m_Tank.Hp;
                    m_Tank.Body.position = repair.transform.position;
                    m_Tank.transform.position = repair.transform.position;
                    ControlPoint far = m_Match.Layout.controlPoints[3];
                    far.Model.Owner = 0; far.Model.OwnerHold = 1f; far.Model.DecaySeconds = 4f;
                    m_T = now; m_Step = 16;
                    break;
                }
                case 16:
                {
                    ControlPoint far = m_Match.Layout.controlPoints[3];
                    if (far.Owner < 0 && !m_FlagDecayed) { m_FlagDecayed = true; m_FlagDecayAt = now - m_T; }
                    if (now - m_T > 1.0f && m_HpBefore > 0 && m_Tank.Hp > m_HpBefore) m_PickupConsumed = true;
                    if (m_FlagDecayed || now - m_T > 8f) { m_PresetIndex = 0; m_Step = 20; m_T = 0f; }
                    break;
                }
                case 20: // other configurations: start cleanly and look at the result
                    if (m_T == 0f)
                    {
                        m_Match.StartMatch(MatchConfig.Preset(Modes[m_PresetIndex]));
                        m_T = now;
                    }
                    else if (now - m_T > 3f)
                    {
                        float minD = float.MaxValue;
                        for (int i = 0; i < m_Match.Tanks.Length; i++)
                            for (int j = i + 1; j < m_Match.Tanks.Length; j++)
                                minD = Mathf.Min(minD, Vector3.Distance(m_Match.Tanks[i].transform.position, m_Match.Tanks[j].transform.position));
                        m_Out.Append(" \"preset_").Append(Modes[m_PresetIndex]).Append("\": {\"teams\":").Append(m_Match.Config.Teams.Count).Append(",\"players\":").Append(m_Match.Players.Length)
                             .Append(",\"solo\":").Append(m_Match.Config.IsSolo ? "true" : "false").Append(",\"min_tank_dist\":").Append(F(minD)).Append("},\n");
                        m_PresetIndex++; m_T = 0f;
                        if (m_PresetIndex >= Modes.Length) Write(null);
                    }
                    break;
            }
        }

        void Write(string failure)
        {
            string dir = Arg("-benchOut") ?? Application.persistentDataPath;
            Directory.CreateDirectory(dir);
            var sb = new StringBuilder();
            sb.Append("{\n \"failure\": ").Append(failure == null ? "null" : "\"" + failure + "\"").Append(",\n");
            sb.Append(m_Out);
            sb.Append(" \"drive\": {\"top_speed\":").Append(F(m_Top)).Append(",\"min_speed_in_90deg_turn\":").Append(F(m_TurnMin)).Append(",\"max_sideways_in_turn\":").Append(F(m_TurnLat)).Append(",\"stop_seconds\":").Append(F(m_Stop)).Append("},\n");
            sb.Append(" \"dash\": {\"distance\":").Append(F(m_DashDist)).Append(",\"cooldown_left\":").Append(F(m_DashCd)).Append("},\n");
            sb.Append(" \"cannon\": {\"shots_before_reload\":").Append(m_ShotsBeforeReload).Append(",\"ammo_at_reload\":").Append(m_AmmoAtReload).Append(",\"reload_seconds\":").Append(F(m_ReloadSec)).Append(",\"ammo_after\":").Append(m_AmmoAfterReload).Append("},\n");
            sb.Append(" \"special_weapons\": {\"mg_shots_in_0_8s\":").Append(m_MgShots).Append(",\"rocket_shots_until_empty\":").Append(m_RocketShots).Append(",\"reverted_to_cannon\":").Append(m_Reverted ? "true" : "false").Append(",\"cannon_ammo_after\":").Append(m_PrimaryAmmo).Append("},\n");
            sb.Append(" \"buffs\": {\"shield_left_after_30\":").Append(m_ShieldLeft).Append(",\"hp_after_second_30\":").Append(m_HpAfter).Append(",\"speed_mult\":").Append(F(m_SpeedMult)).Append(",\"damage_mult\":").Append(F(m_DamageMult))
              .Append(",\"hp_before_repair\":").Append(m_HpBefore).Append(",\"repair_worked\":").Append(m_PickupConsumed ? "true" : "false").Append("},\n");
            sb.Append(" \"flag_decay\": {\"fell_to_neutral\":").Append(m_FlagDecayed ? "true" : "false").Append(",\"seconds\":").Append(F(m_FlagDecayAt)).Append("},\n");
            sb.Append(" \"errors\": ").Append(m_Errors).Append(",\n \"error_text\": \"").Append(m_Msgs).Append("\"\n}\n");
            File.WriteAllText(Path.Combine(dir, "match_probe.json"), sb.ToString());
            enabled = false;
            Application.Quit(0);
        }
    }
}
#endif
