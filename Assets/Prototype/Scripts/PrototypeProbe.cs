// Development-player probe for the combat prototype: measures GC allocation and frame time while idle
// and while the player continuously fires at a parked enemy. Compiled only into Development Builds and
// active only with the command line flag -protoProbe in the Prototype_Arena scene. Buffers are allocated
// once; results are written after sampling. Throwaway tooling, not part of the game.
#if DEVELOPMENT_BUILD
using System;
using System.Globalization;
using System.IO;
using System.Text;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TankGame.Prototype
{
    public sealed class PrototypeProbe : MonoBehaviour
    {
        const int WarmupFrames = 180, IdleFrames = 300, FightFrames = 600;

        TankUnit m_Player, m_Enemy;
        ProfilerRecorder m_Gc;
        float[] m_IdleGc, m_IdleMs, m_FightGc, m_FightMs;
        int m_Frame, m_Kills, m_Shots;
        bool m_WasDead;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-protoProbe") < 0) return;
            if (SceneManager.GetActiveScene().name != "Prototype_Arena") return;
            new GameObject("PrototypeProbe").AddComponent<PrototypeProbe>();
        }

        static string Arg(string name)
        {
            var a = Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
            return null;
        }

        void Start()
        {
            var game = FindFirstObjectByType<PrototypeGame>();
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-noHud") >= 0) { var hud = game.GetComponent<PrototypeHud>(); if (hud != null) hud.enabled = false; }
            m_Player = game.player;
            m_Enemy = game.enemy;
            m_Player.GetComponent<PlayerTankInput>().enabled = false;
            m_Enemy.GetComponent<EnemyTankBrain>().enabled = false;
            m_Player.Fired += u => m_Shots++;
            m_Enemy.Died += u => m_Kills++;
            m_Gc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
            m_IdleGc = new float[IdleFrames]; m_IdleMs = new float[IdleFrames];
            m_FightGc = new float[FightFrames]; m_FightMs = new float[FightFrames];
            Park();
        }

        void Park() { m_Enemy.Respawn(new Vector3(0f, 0f, 6f), Quaternion.Euler(0f, 180f, 0f)); }

        void Update()
        {
            int f = m_Frame++;
            if (m_WasDead && !m_Enemy.IsDead) Park();      // game respawned it elsewhere: put it back on the firing line
            m_WasDead = m_Enemy.IsDead;

            if (f < WarmupFrames) return;
            int i = f - WarmupFrames;
            float gc = (float)m_Gc.LastValue, ms = Time.unscaledDeltaTime * 1000f;
            if (i < IdleFrames)
            {
                m_IdleGc[i] = gc; m_IdleMs[i] = ms;
                return;
            }
            i -= IdleFrames;
            if (i < FightFrames)
            {
                m_Player.Command = new TankCommand { AimPoint = m_Enemy.transform.position + Vector3.up * 0.8f, Fire = true };
                m_FightGc[i] = gc; m_FightMs[i] = ms;
                return;
            }
            Finish();
        }

        static string Stats(float[] v)
        {
            var s = (float[])v.Clone();
            Array.Sort(s);
            double sum = 0; int alloc = 0;
            for (int i = 0; i < v.Length; i++) { sum += v[i]; if (v[i] > 0f) alloc++; }
            var c = CultureInfo.InvariantCulture;
            return "{\"n\":" + v.Length + ",\"mean\":" + (sum / v.Length).ToString("0.###", c) + ",\"p50\":" + s[s.Length / 2].ToString("0.###", c) +
                   ",\"p95\":" + s[(int)(s.Length * 0.95f)].ToString("0.###", c) + ",\"max\":" + s[s.Length - 1].ToString("0.###", c) + ",\"frames_nonzero\":" + alloc + "}";
        }

        void Finish()
        {
            string dir = Arg("-benchOut") ?? Application.persistentDataPath;
            Directory.CreateDirectory(dir);
            var sb = new StringBuilder();
            sb.Append("{\n \"unity\": \"").Append(Application.unityVersion).Append("\",\n \"graphics\": \"").Append(SystemInfo.graphicsDeviceType)
              .Append("\",\n \"resolution\": \"").Append(Screen.width).Append('x').Append(Screen.height).Append("\",\n \"vsync\": ").Append(QualitySettings.vSyncCount)
              .Append(",\n \"shots\": ").Append(m_Shots).Append(",\n \"kills\": ").Append(m_Kills)
              .Append(",\n \"idle_gc_bytes_per_frame\": ").Append(Stats(m_IdleGc))
              .Append(",\n \"fight_gc_bytes_per_frame\": ").Append(Stats(m_FightGc))
              .Append(",\n \"idle_frame_ms\": ").Append(Stats(m_IdleMs))
              .Append(",\n \"fight_frame_ms\": ").Append(Stats(m_FightMs)).Append("\n}\n");
            File.WriteAllText(Path.Combine(dir, "prototype_probe.json"), sb.ToString());
            m_Gc.Dispose();
            Application.Quit(0);
        }
    }
}
#endif
