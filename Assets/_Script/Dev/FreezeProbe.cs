using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Threading;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

/// <summary>
/// 5 Oct (Mr.k): "the Editor freezes when the host presses Start in the lobby".
///
/// Writes breadcrumbs to a file that is flushed line by line, and a watchdog thread
/// notes when the main thread stops answering. After a freeze, close Unity and read:
///
///     Editor:    Pre-Thesis/Logs/FreezeProbe.log
///     Dev build: %USERPROFILE%/AppData/LocalLow/DefaultCompany/My project/FreezeProbe.log
///
/// Run 1 (5 Oct) showed: main thread stuck right after ServerChangeScene, still in
/// the same frame. So after Start, Watch() logs every PHASE of the next frames
/// (FixedUpdate, Update start/end, LateUpdate, render start/end, end of frame) to
/// show exactly which part never finishes.
///
/// Only in the Editor and Development builds. Calls vanish in a normal build.
/// </summary>
public static class FreezeProbe
{
    /// <summary>Write one breadcrumb ("host pressed Start", "loading Cinema"...).</summary>
    [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
    public static void Mark(string what)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        Runner.Write(what);
#endif
    }

    /// <summary>
    /// Adds a marker to every object under <paramref name="root"/> that logs when that
    /// object is disabled / destroyed (only while watching). Shows which part hangs.
    /// </summary>
    [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
    public static void TagForDestroy(GameObject root)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (root == null) return;
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            if (t.GetComponent<DestroyMarker>() == null) t.gameObject.AddComponent<DestroyMarker>();
#endif
    }

    /// <summary>Log every phase of the next <paramref name="frames"/> frames.</summary>
    [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
    public static void Watch(int frames)
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        Runner.WatchFrames = Mathf.Max(Runner.WatchFrames, frames);
        Runner.Write($"watching the next {frames} frames phase by phase");
#endif
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    [DefaultExecutionOrder(-32000)] // first Update of the frame
    private sealed class Runner : MonoBehaviour
    {
        public static int WatchFrames;

        private const double StuckSeconds = 3.0;
        private static readonly object fileLock = new object();
        private static StreamWriter writer;
        private static Thread watchdog;
        private static volatile bool running;
        private static long lastBeatTicks;
        private static volatile string lastMark = "(none)";
        private static readonly Stopwatch clock = Stopwatch.StartNew();
        private static volatile int frame;
        private static int mainThreadId;

        private float loadWatchStarted = -1f;
        private float nextLoadNote;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            running = false;
            lastMark = "(none)";
            WatchFrames = 0;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Boot()
        {
            if (Application.isBatchMode) return;
            var go = new GameObject("[FreezeProbe]");
            go.hideFlags = HideFlags.HideInHierarchy;
            DontDestroyOnLoad(go);
            go.AddComponent<Runner>();
            go.AddComponent<LateRunner>();
            // 5 Oct: the engine-step markers (PlayerLoop) did their job (run 3 found the
            // hang inside Unity's delayed Destroy). They are no longer installed: changing
            // the PlayerLoop is too invasive to leave on.
        }

        private void Awake()
        {
            mainThreadId = Thread.CurrentThread.ManagedThreadId;
            string path = Application.isEditor
                ? Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", "Logs", "FreezeProbe.log")
                : Path.Combine(Application.persistentDataPath, "FreezeProbe.log");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                lock (fileLock)
                {
                    writer = new StreamWriter(path, false) { AutoFlush = true };
                    writer.WriteLine($"FreezeProbe started {DateTime.Now:yyyy-MM-dd HH:mm:ss}  Unity {Application.unityVersion}  editor={Application.isEditor}");
                }
            }
            catch (Exception e) { Debug.LogWarning("[FreezeProbe] cannot write log: " + e.Message); }

            Interlocked.Exchange(ref lastBeatTicks, clock.ElapsedTicks);
            running = true;
            watchdog = new Thread(Watch) { IsBackground = true, Name = "FreezeProbe" };
            watchdog.Start();

            SceneManager.sceneLoaded += OnSceneLoaded;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
            SceneManager.activeSceneChanged += OnActiveSceneChanged;
            Application.logMessageReceivedThreaded += OnLog;
            Application.onBeforeRender += OnBeforeRender;
            RenderPipelineManager.beginContextRendering += OnBeginRender;
            RenderPipelineManager.endContextRendering += OnEndRender;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.pauseStateChanged += OnPause;
#endif
            StartCoroutine(EndOfFrameLoop());
        }

        private static void OnSceneLoaded(Scene s, LoadSceneMode m) => Write($"scene LOADED '{s.name}' ({m})");
        private static void OnSceneUnloaded(Scene s) => Write($"scene unloaded '{s.name}'");
        private static void OnActiveSceneChanged(Scene a, Scene b) => Write($"active scene -> '{b.name}'");
#if UNITY_EDITOR
        private static void OnPause(UnityEditor.PauseState p) => Write("EDITOR " + p + " (Pause button or Console 'Error Pause')");
#endif

        private static void OnLog(string message, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                Write($"{type}: {Short(message)}");
        }

        // ---- Phase marks while watching -------------------------------------

        private static bool Watching => WatchFrames > 0;
        public static void Phase(string phase) { if (Watching) Write("  phase: " + phase); }

        private void FixedUpdate() => Phase("FixedUpdate");
        private static void OnBeforeRender() => Phase("before render");
        private static void OnBeginRender(ScriptableRenderContext c, System.Collections.Generic.List<Camera> cams) => Phase($"render start ({cams.Count} cameras)");
        private static void OnEndRender(ScriptableRenderContext c, System.Collections.Generic.List<Camera> cams) => Phase("render end");

        private IEnumerator EndOfFrameLoop()
        {
            var wait = new WaitForEndOfFrame();
            while (true)
            {
                yield return wait;
                if (!Watching) continue;
                Phase("END OF FRAME");
                WatchFrames--;
                if (WatchFrames == 0) Write("watching ended (frames run normally)");
            }
        }

        private void Update()
        {
            frame = Time.frameCount;
            Interlocked.Exchange(ref lastBeatTicks, clock.ElapsedTicks);
            Phase("Update start");
            WatchSceneLoad();
        }

        /// <summary>Mirror's map load: note it if it takes long (Unity alive, map not loading).</summary>
        private void WatchSceneLoad()
        {
            AsyncOperation load = Mirror.NetworkManager.loadingSceneAsync;
            if (load == null || load.isDone) { loadWatchStarted = -1f; return; }
            if (loadWatchStarted < 0f)
            {
                loadWatchStarted = Time.unscaledTime;
                nextLoadNote = loadWatchStarted + 5f;
                Write("Mirror scene load started");
                return;
            }
            if (Watching) Phase($"scene load progress={load.progress:F2}");
            if (Time.unscaledTime < nextLoadNote) return;
            nextLoadNote = Time.unscaledTime + 5f;
            Write($"scene load still running after {Time.unscaledTime - loadWatchStarted:F0}s, " +
                  $"progress={load.progress:F2} allowActivation={load.allowSceneActivation}");
        }

        public static void Write(string what) => WriteLine(what, true);

        private static void WriteLine(string what, bool remember)
        {
            if (remember) lastMark = what;
            int f = Thread.CurrentThread.ManagedThreadId == mainThreadId ? Time.frameCount : frame;
            lock (fileLock)
            {
                if (writer == null) return;
                try { writer.WriteLine($"{DateTime.Now:HH:mm:ss.fff} f{f}  {what}"); }
                catch (Exception) { /* never break the game for a log line */ }
            }
        }

        /// <summary>Background thread: says when the main thread stops ticking.</summary>
        private static void Watch()
        {
            double reported = 0;
            while (running)
            {
                Thread.Sleep(500);
                double stuck = (clock.ElapsedTicks - Interlocked.Read(ref lastBeatTicks)) / (double)Stopwatch.Frequency;
                if (stuck >= StuckSeconds && stuck >= reported * 2)
                {
                    reported = Math.Max(stuck, StuckSeconds);
                    // Not remembered: the "last breadcrumb" must stay the real one.
                    WriteLine($"!!! MAIN THREAD STUCK {stuck:F0}s. Last breadcrumb: {lastMark}", false);
                }
                else if (stuck < 1 && reported > 0)
                {
                    WriteLine($"main thread back (was stuck about {reported:F0}s+)", false);
                    reported = 0;
                }
            }
        }

        private void OnDestroy()
        {
            running = false;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            SceneManager.activeSceneChanged -= OnActiveSceneChanged;
            Application.logMessageReceivedThreaded -= OnLog;
            Application.onBeforeRender -= OnBeforeRender;
            RenderPipelineManager.beginContextRendering -= OnBeginRender;
            RenderPipelineManager.endContextRendering -= OnEndRender;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.pauseStateChanged -= OnPause;
#endif
            lock (fileLock)
            {
                try { writer?.WriteLine("FreezeProbe stopped (Play mode ended / game closed)"); writer?.Dispose(); }
                catch (Exception) { }
                writer = null;
            }
        }

        private static string Short(string s) => string.IsNullOrEmpty(s) ? "" : s.Length > 200 ? s.Substring(0, 200) + "..." : s.Replace('\n', ' ');
    }

    /// <summary>Logs when the object it sits on is disabled / destroyed (while watching).</summary>
    private sealed class DestroyMarker : MonoBehaviour
    {
        private void OnDisable() => Runner.Phase("disable: " + Path(transform));
        private void OnDestroy() => Runner.Phase("destroy: " + Path(transform));

        private static string Path(Transform t)
        {
            string p = t.name;
            for (Transform up = t.parent; up != null; up = up.parent) p = up.name + "/" + p;
            return p;
        }
    }

    /// <summary>Last in the frame: shows whether Update / LateUpdate finished.</summary>
    [DefaultExecutionOrder(32000)]
    private sealed class LateRunner : MonoBehaviour
    {
        private void Update() => Runner.Phase("Update end (all scripts' Update done)");
        private void LateUpdate() => Runner.Phase("LateUpdate end");
    }
#endif
}
