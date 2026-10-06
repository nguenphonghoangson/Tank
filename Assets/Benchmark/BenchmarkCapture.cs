// Development-player capture for the rendering benchmark scene. Compiled only into Development
// Builds (DEVELOPMENT_BUILD), and only active when the loaded scene is Benchmark_Rendering, so it
// has no effect on Bootstrap or on release builds. No scene object, no gameplay dependency.
//
// Flow: fixed warmup frames, then fixed measurement frames, one CSV row per frame, then a JSON
// description of the run, then Application.Quit. All per-frame buffers are allocated once in Awake;
// nothing is allocated while measuring (file output happens after the last measured frame).
//
// Optional command line: -benchOut <dir>  -benchWarmup <frames>  -benchFrames <frames>  -benchTag <text>
#if DEVELOPMENT_BUILD
using System;
using System.Globalization;
using System.IO;
using System.Text;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Tank.Benchmark
{
    public sealed class BenchmarkCapture : MonoBehaviour
    {
        public const string SceneName = "Benchmark_Rendering";
        const int DefaultWarmupFrames = 300;
        const int DefaultMeasureFrames = 1800;

        [Serializable]
        class RunInfo
        {
            public int schema = 1;
            public string status;
            public string tag;
            public string unity;
            public string platform;
            public string device;
            public string cpu;
            public string graphics_api;
            public string gpu;
            public string render_pipeline;
            public string quality_level;
            public int vsync_count;
            public int target_frame_rate;
            public double display_refresh_hz;
            public int screen_width;
            public int screen_height;
            public bool full_screen;
            public bool development_build;
            public bool frame_timing_enabled;
            public int warmup_frames;
            public int measure_frames;
            public double warmup_seconds;
            public double measure_seconds;
            public string[] missing_counters;
            public string csv;
        }

        struct Counter
        {
            public string column;
            public ProfilerRecorder recorder;
            public double scale;
            public float[] values;
        }

        int m_Warmup, m_Frames, m_Index, m_Seen;
        string m_OutDir, m_Tag;
        double m_WarmupStart, m_MeasureStart;
        bool m_Done;

        float[] m_Wall, m_FtCpu, m_FtMain, m_FtRender, m_FtGpu;
        Counter[] m_Counters;
        FrameTiming[] m_Timing;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (SceneManager.GetActiveScene().name != SceneName) return;
            var go = new GameObject("BenchmarkCapture") { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(go);
            go.AddComponent<BenchmarkCapture>();
        }

        static string Arg(string name)
        {
            var a = Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
            return null;
        }

        static int IntArg(string name, int fallback)
        {
            var s = Arg(name);
            return s != null && int.TryParse(s, out var v) && v > 0 ? v : fallback;
        }

        void Awake()
        {
            m_Warmup = IntArg("-benchWarmup", DefaultWarmupFrames);
            m_Frames = IntArg("-benchFrames", DefaultMeasureFrames);
            m_OutDir = Arg("-benchOut") ?? Path.Combine(Application.persistentDataPath, "benchmark");
            m_Tag = Arg("-benchTag") ?? "run";

            m_Wall = new float[m_Frames]; m_FtCpu = new float[m_Frames]; m_FtMain = new float[m_Frames];
            m_FtRender = new float[m_Frames]; m_FtGpu = new float[m_Frames];
            m_Timing = new FrameTiming[1];

            m_Counters = new[]
            {
                Make("rec_main_thread_ms", ProfilerCategory.Internal, "Main Thread", 1e-6),
                Make("rec_render_thread_ms", ProfilerCategory.Internal, "Render Thread", 1e-6),
                Make("gc_alloc_bytes", ProfilerCategory.Memory, "GC Allocated In Frame", 1),
                Make("gc_reserved_mb", ProfilerCategory.Memory, "GC Reserved Memory", 1.0 / (1024 * 1024)),
                Make("gc_used_mb", ProfilerCategory.Memory, "GC Used Memory", 1.0 / (1024 * 1024)),
                Make("system_used_mb", ProfilerCategory.Memory, "System Used Memory", 1.0 / (1024 * 1024)),
                Make("draw_calls", ProfilerCategory.Render, "Draw Calls Count", 1),
                Make("batches", ProfilerCategory.Render, "Batches Count", 1),
                Make("setpass_calls", ProfilerCategory.Render, "SetPass Calls Count", 1),
                Make("triangles", ProfilerCategory.Render, "Triangles Count", 1),
                Make("vertices", ProfilerCategory.Render, "Vertices Count", 1),
            };
            m_WarmupStart = Time.realtimeSinceStartupAsDouble;
        }

        Counter Make(string column, ProfilerCategory cat, string name, double scale)
        {
            return new Counter { column = column, recorder = ProfilerRecorder.StartNew(cat, name), scale = scale, values = new float[m_Frames] };
        }

        void Update()
        {
            if (m_Done) return;
            m_Seen++;
            if (m_Seen <= m_Warmup) return;
            if (m_Index == 0) m_MeasureStart = Time.realtimeSinceStartupAsDouble;

            if (m_Index < m_Frames)
            {
                int i = m_Index++;
                m_Wall[i] = Time.unscaledDeltaTime * 1000f;
                FrameTimingManager.CaptureFrameTimings();
                if (FrameTimingManager.GetLatestTimings(1, m_Timing) > 0)
                {
                    m_FtCpu[i] = (float)m_Timing[0].cpuFrameTime;
                    m_FtMain[i] = (float)m_Timing[0].cpuMainThreadFrameTime;
                    m_FtRender[i] = (float)m_Timing[0].cpuRenderThreadFrameTime;
                    m_FtGpu[i] = (float)m_Timing[0].gpuFrameTime;
                }
                for (int c = 0; c < m_Counters.Length; c++)
                    m_Counters[c].values[i] = m_Counters[c].recorder.Valid ? (float)(m_Counters[c].recorder.LastValue * m_Counters[c].scale) : -1f;
            }

            if (m_Index >= m_Frames) Finish();
        }

        void Finish()
        {
            m_Done = true;
            double now = Time.realtimeSinceStartupAsDouble;
            Directory.CreateDirectory(m_OutDir);
            string stem = "benchmark_" + m_Tag;
            string csvPath = Path.Combine(m_OutDir, stem + ".csv");

            var sb = new StringBuilder(m_Frames * 160);
            sb.Append("frame,wall_ms,ft_cpu_ms,ft_main_ms,ft_render_ms,ft_gpu_ms");
            for (int c = 0; c < m_Counters.Length; c++) sb.Append(',').Append(m_Counters[c].column);
            sb.Append('\n');
            var inv = CultureInfo.InvariantCulture;
            for (int i = 0; i < m_Frames; i++)
            {
                sb.Append(i.ToString(inv)).Append(',').Append(m_Wall[i].ToString("0.####", inv)).Append(',')
                  .Append(m_FtCpu[i].ToString("0.####", inv)).Append(',').Append(m_FtMain[i].ToString("0.####", inv)).Append(',')
                  .Append(m_FtRender[i].ToString("0.####", inv)).Append(',').Append(m_FtGpu[i].ToString("0.####", inv));
                for (int c = 0; c < m_Counters.Length; c++) sb.Append(',').Append(m_Counters[c].values[i].ToString("0.####", inv));
                sb.Append('\n');
            }
            File.WriteAllText(csvPath, sb.ToString());

            var missing = new System.Collections.Generic.List<string>();
            for (int c = 0; c < m_Counters.Length; c++) if (!m_Counters[c].recorder.Valid) missing.Add(m_Counters[c].column);

            var info = new RunInfo
            {
                status = "completed", tag = m_Tag, unity = Application.unityVersion, platform = Application.platform.ToString(),
                device = SystemInfo.deviceModel, cpu = SystemInfo.processorType,
                graphics_api = SystemInfo.graphicsDeviceType.ToString(), gpu = SystemInfo.graphicsDeviceName,
                render_pipeline = GraphicsSettings.currentRenderPipeline != null ? GraphicsSettings.currentRenderPipeline.name : "Built-in",
                quality_level = QualitySettings.names[QualitySettings.GetQualityLevel()], vsync_count = QualitySettings.vSyncCount,
                target_frame_rate = Application.targetFrameRate, display_refresh_hz = Screen.currentResolution.refreshRateRatio.value,
                screen_width = Screen.width, screen_height = Screen.height, full_screen = Screen.fullScreen,
                development_build = Debug.isDebugBuild, frame_timing_enabled = FrameTimingManager.IsFeatureEnabled(),
                warmup_frames = m_Warmup, measure_frames = m_Frames,
                warmup_seconds = m_MeasureStart - m_WarmupStart, measure_seconds = now - m_MeasureStart,
                missing_counters = missing.ToArray(), csv = csvPath,
            };
            File.WriteAllText(Path.Combine(m_OutDir, stem + ".json"), JsonUtility.ToJson(info, true));

            for (int c = 0; c < m_Counters.Length; c++) m_Counters[c].recorder.Dispose();
            Application.Quit(0);
        }
    }
}
#endif
