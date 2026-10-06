using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Mirror;
using UnityEngine;

namespace TankGame.NetSpike
{
    /// <summary>Numbers for the spike: what a player would feel (corrections, hit confirmation delay), what the network costs (bytes, rtt).</summary>
    public static class SpikeMetrics
    {
        public static int ReconCount, ReconOver05, SnapshotsIn, Underruns, Shots, Hits, Kills, ServerStarved, ServerDropped, ServerTicks, ServerFires, ExpectedHits, ConfirmedOfExpected;
        public static float ReconMax;
        public static long BytesIn, BytesOut;
        public static readonly List<float> FireConfirmMs = new List<float>();
        public static readonly List<float> RttMs = new List<float>();
        public static readonly List<float> SnapGapMs = new List<float>();
        public static float SecondsRun;
        public static int DbgTicks, DbgMoving, DbgTargetSeen, DbgFireWanted, DbgBotOn;
        public static int DbgPush, DbgOverlaps, DbgStop;
        public static string DbgNames = "";
        public static Vector3 DbgPos;
        public static float DbgMinDist = 9999f, DbgMinAngle = 9999f, DbgLastDist, DbgLastAngle;
        static bool s_Hooked;

        public static void Hook()
        {
            if (s_Hooked) return;
            s_Hooked = true;
            NetworkDiagnostics.InMessageEvent += m => BytesIn += (long)m.bytes * m.count;
            NetworkDiagnostics.OutMessageEvent += m => BytesOut += (long)m.bytes * m.count;
        }

        static string Stats(List<float> v)
        {
            var c = CultureInfo.InvariantCulture;
            if (v.Count == 0) return "{\"n\":0}";
            var s = new List<float>(v); s.Sort();
            double sum = 0; foreach (float x in s) sum += x;
            return "{\"n\":" + s.Count + ",\"mean\":" + (sum / s.Count).ToString("0.##", c) + ",\"p50\":" + s[s.Count / 2].ToString("0.##", c) +
                   ",\"p95\":" + s[Mathf.Min(s.Count - 1, (int)(s.Count * 0.95f))].ToString("0.##", c) + ",\"max\":" + s[s.Count - 1].ToString("0.##", c) + "}";
        }

        public static void Write(string path, string role, float latencyOneWayMs, float lossPct)
        {
            var c = CultureInfo.InvariantCulture;
            float secs = Mathf.Max(0.001f, SecondsRun);
            var sb = new StringBuilder();
            sb.Append("{\n \"role\": \"").Append(role).Append("\",\n \"latency_one_way_ms\": ").Append(latencyOneWayMs.ToString("0.#", c)).Append(",\n \"loss_pct\": ").Append(lossPct.ToString("0.#", c))
              .Append(",\n \"seconds\": ").Append(secs.ToString("0.0", c))
              .Append(",\n \"corrections\": ").Append(ReconCount).Append(",\n \"corrections_per_min\": ").Append((ReconCount / secs * 60f).ToString("0.#", c))
              .Append(",\n \"corrections_over_0_5m\": ").Append(ReconOver05).Append(",\n \"correction_max_m\": ").Append(ReconMax.ToString("0.###", c))
              .Append(",\n \"snapshots_in\": ").Append(SnapshotsIn).Append(",\n \"interp_underruns\": ").Append(Underruns)
              .Append(",\n \"shots\": ").Append(Shots).Append(",\n \"hits_confirmed\": ").Append(Hits).Append(",\n \"kills\": ").Append(Kills)
              .Append(",\n \"fire_to_confirm_ms_minus_travel\": ").Append(Stats(FireConfirmMs))
              .Append(",\n \"rtt_ms\": ").Append(Stats(RttMs))
              .Append(",\n \"snapshot_gap_ms\": ").Append(Stats(SnapGapMs))
              .Append(",\n \"dbg\": {\"client_ticks\":").Append(DbgTicks).Append(",\"moving\":").Append(DbgMoving).Append(",\"bot_on\":").Append(DbgBotOn).Append(",\"target_seen\":").Append(DbgTargetSeen).Append(",\"fire_wanted\":").Append(DbgFireWanted).Append(",\"pushes\":").Append(DbgPush).Append(",\"overlaps\":").Append(DbgOverlaps).Append(",\"stops\":").Append(DbgStop).Append(",\"overlap_names\":\"").Append(DbgNames).Append("\",\"last_pos\":\"").Append(DbgPos.ToString("0.0")).Append("\"").Append(",\"min_dist\":").Append(DbgMinDist.ToString("0.#", c)).Append(",\"min_angle\":").Append(DbgMinAngle.ToString("0.#", c)).Append(",\"last_dist\":").Append(DbgLastDist.ToString("0.#", c)).Append(",\"last_angle\":").Append(DbgLastAngle.ToString("0.#", c)).Append("}")
              .Append(",\n \"expected_hits_seen_on_screen\": ").Append(ExpectedHits).Append(",\n \"expected_hits_confirmed_by_server\": ").Append(ConfirmedOfExpected)
              .Append(",\n \"server_fires\": ").Append(ServerFires)
              .Append(",\n \"server_ticks\": ").Append(ServerTicks).Append(",\n \"server_starved_ticks\": ").Append(ServerStarved).Append(",\n \"server_dropped_cmds\": ").Append(ServerDropped)
              .Append(",\n \"bytes_in_per_s\": ").Append((BytesIn / secs).ToString("0", c)).Append(",\n \"bytes_out_per_s\": ").Append((BytesOut / secs).ToString("0", c))
              .Append("\n}\n");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            File.WriteAllText(path, sb.ToString());
        }
    }
}
