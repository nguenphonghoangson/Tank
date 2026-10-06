using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.Profiling;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace TankGame.Prototype.Editor
{
    /// <summary>
    /// Scripted Play-mode check of the combat loop (no human input), in phases:
    /// 1 move + independent turret, 2 idle GC baseline, 3 player kills a parked enemy (frames saved around
    /// muzzle flash / impact / explosion), 4 enemy respawn, 5 enemy brain shoots the player.
    /// Survives the Play-mode domain reload through SessionState.
    /// Run PrototypeSmokeTest.Begin(); the result lands in Library/AgentKit/prototype_smoke/.
    /// </summary>
    [InitializeOnLoad]
    public static class PrototypeSmokeTest
    {
        const string KActive = "TankProto.Smoke.Active";
        const string KPrevScene = "TankProto.Smoke.PrevScene";
        const string ScenePath = "Assets/Scenes/Prototype_Arena.unity";

        struct Due { public double time; public string name; }

        static readonly List<Due> s_Due = new List<Due>();
        static readonly List<string> s_Msgs = new List<string>();
        static int s_Step, s_StartFrame, s_Shots, s_DamageEvents, s_DamageTotal, s_Kills, s_Errors, s_EnemyShots, s_PlayerHits;
        static double s_T0, s_StepT, s_FirstFireT = -1, s_FirstHitT = -1, s_DiedT = -1;
        static bool s_Respawned;
        static TankUnit s_Player, s_Enemy;
        static CameraRig s_Rig;
        static ProfilerRecorder s_Gc;
        static string s_Dir;
        static float s_IdleSum, s_FightSum, s_FightMax, s_IdleMax;
        static int s_IdleN, s_FightN;
        static float s_MoveSpeed, s_MoveHullYaw, s_MoveTurretYaw, s_MoveTurretErr, s_MinEnemyDist = 999f;

        static PrototypeSmokeTest()
        {
            EditorApplication.playModeStateChanged += OnState;
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
            if (change == PlayModeStateChange.EnteredPlayMode) Hook();
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
            s_Dir = Path.Combine(Directory.GetCurrentDirectory(), "Library", "AgentKit", "prototype_smoke");
            Directory.CreateDirectory(s_Dir);
            s_Step = 0;
            s_T0 = Time.realtimeSinceStartupAsDouble;
            s_StartFrame = Time.frameCount;
            s_Due.Clear(); s_Msgs.Clear();
            s_Shots = s_DamageEvents = s_DamageTotal = s_Kills = s_Errors = s_EnemyShots = s_PlayerHits = 0;
            s_FirstFireT = s_FirstHitT = s_DiedT = -1;
            s_IdleSum = s_FightSum = s_FightMax = s_IdleMax = 0f;
            s_IdleN = s_FightN = 0;
            s_Respawned = false;
            Application.logMessageReceived += OnLog;
            EditorApplication.update += Tick;
        }

        static void Unhook()
        {
            EditorApplication.update -= Tick;
            Application.logMessageReceived -= OnLog;
            if (s_Gc.Valid) s_Gc.Dispose();
        }

        static void OnLog(string condition, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            {
                s_Errors++;
                if (s_Msgs.Count < 8) s_Msgs.Add(type + ": " + condition);
            }
        }

        static float SampleGc() { return s_Gc.Valid ? (float)s_Gc.LastValue : 0f; }

        static void Tick()
        {
            double now = Time.realtimeSinceStartupAsDouble;
            if (now - s_T0 > 60.0) { Finish("timeout in step " + s_Step); return; }

            for (int i = s_Due.Count - 1; i >= 0; i--)
                if (now >= s_Due[i].time) { Capture(s_Due[i].name); s_Due.RemoveAt(i); }

            switch (s_Step)
            {
                case 0: // setup once the scene has had a few frames
                {
                    if (Time.frameCount - s_StartFrame < 30) return;
                    var game = Object.FindFirstObjectByType<PrototypeGame>();
                    if (game == null) { Finish("PrototypeGame not found"); return; }
                    s_Player = game.player;
                    s_Enemy = game.enemy;
                    s_Rig = Camera.main.GetComponent<CameraRig>();
                    s_Player.GetComponent<PlayerTankInput>().enabled = false;
                    s_Enemy.GetComponent<EnemyTankBrain>().enabled = false;
                    s_Enemy.Command = default;
                    s_Enemy.Respawn(new Vector3(0f, 0f, 6f), Quaternion.Euler(0f, 180f, 0f));
                    s_Player.Fired += u => { s_Shots++; if (s_FirstFireT < 0) { s_FirstFireT = Time.realtimeSinceStartupAsDouble; Schedule(0.004, "1_muzzle_flash"); Schedule(0.05, "2_projectile_in_flight"); } };
                    s_Enemy.Fired += u => s_EnemyShots++;
                    s_Enemy.Damaged += (u, dmg, p, d) => { s_DamageEvents++; s_DamageTotal += dmg; if (s_FirstHitT < 0) { s_FirstHitT = Time.realtimeSinceStartupAsDouble; Schedule(0.012, "3_impact"); } };
                    s_Enemy.Died += u => { s_Kills++; s_DiedT = Time.realtimeSinceStartupAsDouble; Schedule(0.05, "4_explosion_early"); Schedule(0.25, "5_explosion_late"); };
                    s_Player.Damaged += (u, dmg, p, d) => s_PlayerHits++;
                    s_Gc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
                    s_StepT = now;
                    s_Step = 1;
                    break;
                }
                case 1: // move right while the turret keeps aiming at a point behind-left: hull and turret are independent
                {
                    Vector3 aim = new Vector3(-10f, 0.8f, -20f);
                    s_Player.Command = new TankCommand { Move = new Vector2(1f, 0f), AimPoint = aim };
                    if (now - s_StepT > 1.2)
                    {
                        s_MoveSpeed = s_Player.Body.linearVelocity.magnitude;
                        s_MoveHullYaw = s_Player.transform.eulerAngles.y;
                        s_MoveTurretYaw = s_Player.turretPivot.eulerAngles.y;
                        Vector3 to = aim - s_Player.turretPivot.position; to.y = 0f;
                        s_MoveTurretErr = Vector3.Angle(s_Player.turretPivot.forward, to);
                        s_Player.Command = default;
                        s_Player.Respawn(new Vector3(0f, 0f, -20f), Quaternion.identity);
                        s_StepT = now;
                        s_Step = 2;
                    }
                    break;
                }
                case 2: // idle GC baseline (Editor overhead only)
                {
                    if (now - s_StepT < 0.3) return;
                    float v = SampleGc(); s_IdleSum += v; s_IdleN++; if (v > s_IdleMax) s_IdleMax = v;
                    if (now - s_StepT > 1.3)
                    {
                        s_Rig.target = s_Enemy.transform;    // frame the target so captures show the hit and the explosion
                        s_StepT = now;
                        s_Step = 3;
                    }
                    break;
                }
                case 3: // fire at the parked enemy until it is destroyed
                {
                    s_Player.Command = new TankCommand { AimPoint = s_Enemy.transform.position + Vector3.up * 0.8f, Fire = true };
                    if (now - s_StepT > 0.3)
                    {
                        float v = SampleGc(); s_FightSum += v; s_FightN++; if (v > s_FightMax) s_FightMax = v;
                    }
                    if (s_Enemy.IsDead && now - s_DiedT > 0.4)
                    {
                        s_Player.Command = default;
                        s_StepT = now;
                        s_Step = 4;
                    }
                    break;
                }
                case 4: // the prototype loop should respawn the enemy
                {
                    if (!s_Enemy.IsDead) { s_Respawned = true; s_Step = 5; s_StepT = now; PrepareBrainPhase(); }
                    else if (now - s_StepT > 6.0) Finish("enemy did not respawn");
                    break;
                }
                case 5: // enemy brain on: it should close in, aim and hit the idle player
                {
                    float d = Vector3.Distance(s_Enemy.transform.position, s_Player.transform.position);
                    if (d < s_MinEnemyDist) s_MinEnemyDist = d;
                    if (now - s_StepT > 12.0) Finish(null);
                    break;
                }
            }
        }

        static void PrepareBrainPhase()
        {
            s_Player.Command = default;
            s_Rig.target = s_Player.transform;
            s_Enemy.GetComponent<EnemyTankBrain>().enabled = true;
        }

        static void Schedule(double secondsFromNow, string name)
        {
            s_Due.Add(new Due { time = Time.realtimeSinceStartupAsDouble + secondsFromNow, name = name });
        }

        static string F(float v) { return v.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture); }

        static void Finish(string failure)
        {
            EditorApplication.update -= Tick;
            var sb = new StringBuilder();
            sb.Append("{\n");
            sb.Append("  \"failure\": ").Append(failure == null ? "null" : "\"" + failure.Replace("\"", "'") + "\"").Append(",\n");
            sb.Append("  \"move_speed_mps\": ").Append(F(s_MoveSpeed)).Append(",\n");
            sb.Append("  \"move_hull_yaw_deg\": ").Append(F(s_MoveHullYaw)).Append(",\n");
            sb.Append("  \"move_turret_yaw_deg\": ").Append(F(s_MoveTurretYaw)).Append(",\n");
            sb.Append("  \"move_turret_aim_error_deg\": ").Append(F(s_MoveTurretErr)).Append(",\n");
            sb.Append("  \"player_shots_fired\": ").Append(s_Shots).Append(",\n");
            sb.Append("  \"enemy_damage_events\": ").Append(s_DamageEvents).Append(",\n");
            sb.Append("  \"enemy_damage_total\": ").Append(s_DamageTotal).Append(",\n");
            sb.Append("  \"enemy_kills\": ").Append(s_Kills).Append(",\n");
            sb.Append("  \"enemy_respawned\": ").Append(s_Respawned ? "true" : "false").Append(",\n");
            sb.Append("  \"fire_to_first_hit_s\": ").Append(s_FirstHitT > 0 ? F((float)(s_FirstHitT - s_FirstFireT)) : "null").Append(",\n");
            sb.Append("  \"first_hit_to_kill_s\": ").Append(s_DiedT > 0 ? F((float)(s_DiedT - s_FirstHitT)) : "null").Append(",\n");
            sb.Append("  \"enemy_shots_in_brain_phase\": ").Append(s_EnemyShots).Append(",\n");
            sb.Append("  \"player_hits_taken\": ").Append(s_PlayerHits).Append(",\n");
            sb.Append("  \"enemy_min_distance_m\": ").Append(F(s_MinEnemyDist)).Append(",\n");
            sb.Append("  \"gc_idle_mean_bytes_per_frame\": ").Append(F(s_IdleN > 0 ? s_IdleSum / s_IdleN : 0f)).Append(",\n");
            sb.Append("  \"gc_idle_max_bytes\": ").Append(F(s_IdleMax)).Append(",\n");
            sb.Append("  \"gc_fight_mean_bytes_per_frame\": ").Append(F(s_FightN > 0 ? s_FightSum / s_FightN : 0f)).Append(",\n");
            sb.Append("  \"gc_fight_max_bytes\": ").Append(F(s_FightMax)).Append(",\n");
            sb.Append("  \"timescale_restored\": ").Append(Mathf.Approximately(Time.timeScale, 1f) ? "true" : "false").Append(",\n");
            sb.Append("  \"errors\": ").Append(s_Errors).Append(",\n");
            sb.Append("  \"error_messages\": [");
            for (int i = 0; i < s_Msgs.Count; i++) sb.Append(i > 0 ? ", " : "").Append('"').Append(s_Msgs[i].Replace("\\", "/").Replace("\"", "'").Replace("\n", " ")).Append('"');
            sb.Append("]\n}\n");
            File.WriteAllText(Path.Combine(s_Dir, "result.json"), sb.ToString());
            EditorApplication.ExitPlaymode();
        }

        static void Capture(string name)
        {
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
