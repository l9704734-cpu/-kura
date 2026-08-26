using System.Threading;

namespace Diver_RaT
{
    public static class SetupOrchestrator
    {
        public enum Target { All, Jdk, Sdk, Gradle }

        private static readonly object _gate = new();
        private static CancellationTokenSource? _cts;
        private static Task? _task;
        private static SynchronizationContext? _uiContext;
        private static readonly List<string> _log = new();

        public static bool IsBusy { get; private set; }
        public static ProgressInfo Latest { get; private set; }

        public static IReadOnlyList<string> GetLogSnapshot()
        {
            lock (_gate) return _log.ToArray();
        }

        public static event Action<ProgressInfo>? ProgressChanged;
        public static event Action<bool, string>? Completed;

        public static bool Start(Target target)
        {
            SynchronizationContext? ui;
            CancellationToken token;
            lock (_gate)
            {
                if (IsBusy) return false;
                _cts = new CancellationTokenSource();
                token = _cts.Token;
                _uiContext = SynchronizationContext.Current;
                ui = _uiContext;
                IsBusy = true;
                _log.Clear();
                AddLogLocked("---- start ----");
                Latest = new ProgressInfo(0, "Starting...");
            }

            ProgressChanged?.Invoke(Latest);

            var prog = new Progress<ProgressInfo>(OnReport);
            _task = Task.Run(async () =>
            {
                bool ok = false; string msg = "Done";
                try
                {
                    switch (target)
                    {
                        case Target.All: await BuildEnvironment.InstallAllAsync(prog, token); break;
                        case Target.Jdk: await BuildEnvironment.InstallJdkAsync(prog, token); break;
                        case Target.Sdk: await BuildEnvironment.InstallSdkAsync(prog, token); break;
                        case Target.Gradle: await BuildEnvironment.InstallGradleAsync(prog, token); break;
                    }
                    ok = true;
                }
                catch (OperationCanceledException) { msg = "Cancelled"; }
                catch (Exception ex) { msg = "ERROR: " + ex.Message; AddLog(msg); }
                finally
                {
                    AddLog(ok ? "---- done ----" : (msg == "Cancelled" ? "---- cancelled ----" : "---- finished ----"));
                    lock (_gate) IsBusy = false;
                    RaiseCompleted(ui, ok, msg);
                }
            }, token);
            return true;
        }

        public static void Cancel()
        {
            CancellationTokenSource? cts;
            lock (_gate) cts = _cts;
            try { cts?.Cancel(); } catch { }
        }

        private static void OnReport(ProgressInfo info)
        {
            lock (_gate)
            {
                Latest = info;
                if (!string.IsNullOrEmpty(info.Message)) AddLogLocked(info.Message);
            }
            ProgressChanged?.Invoke(info);
        }

        private static void RaiseCompleted(SynchronizationContext? ui, bool ok, string msg)
        {
            if (ui == null) { try { Completed?.Invoke(ok, msg); } catch { } return; }
            try
            {
                ui.Post(_ =>
                {
                    try { Completed?.Invoke(ok, msg); } catch { }
                }, null);
            }
            catch { }
        }

        private static void AddLog(string line)
        {
            lock (_gate) AddLogLocked(line);
        }

        private static void AddLogLocked(string line)
        {
            _log.Add(line);
            if (_log.Count > 500) _log.RemoveRange(0, _log.Count - 500);
        }
    }
}