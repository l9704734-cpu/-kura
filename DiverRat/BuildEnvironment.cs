using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;

namespace Diver_RaT
{
    public readonly record struct ProgressInfo(double Percent, string Message);

    public sealed record ToolStatus(bool Found, string Path, string Label);

    public static class BuildEnvironment
    {
        public const string GradleVersion = "8.14";
        public const string BuildToolsVersion = "35.0.0";
        public const string CompileSdkPlatform = "android-34";

        public const string JdkZipUrl =
            "https://github.com/adoptium/temurin17-binaries/releases/download/jdk-17.0.20%2B8/OpenJDK17U-jdk_x64_windows_hotspot_17.0.20_8.zip";
        public const string CmdlineToolsZipUrl =
            "https://dl.google.com/android/repository/commandlinetools-win-11076708_latest.zip";
        public const string GradleZipUrl =
            "https://services.gradle.org/distributions/gradle-8.14-bin.zip";

        public static readonly string[] SdkComponents =
            { "platform-tools", $"build-tools;{BuildToolsVersion}", $"platforms;{CompileSdkPlatform}" };

        public static string Root =>
            AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        public static string ToolsDir => Path.Combine(Root, "tools");
        public static string SdkDir => Path.Combine(ToolsDir, "android-sdk");
        public static string GradleHomeDir => Path.Combine(ToolsDir, "gradle");
        public static string GradleUserHome => Path.Combine(ToolsDir, "gradle-cache");
        public static string JdkHomeDir => Path.Combine(ToolsDir, "jdk");

        public static bool IsValidSdk(string dir) =>
            !string.IsNullOrEmpty(dir) &&
            File.Exists(Path.Combine(dir, "platform-tools", "adb.exe")) &&
            Directory.Exists(Path.Combine(dir, "build-tools", BuildToolsVersion)) &&
            Directory.Exists(Path.Combine(dir, "platforms", CompileSdkPlatform));

        public static string GetAndroidSdk()
        {
            if (IsValidSdk(SdkDir)) return SdkDir;

            var env = Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT")
                   ?? Environment.GetEnvironmentVariable("ANDROID_HOME");
            if (!string.IsNullOrEmpty(env) && Directory.Exists(env)) return env;

            var candidates = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Android", "Sdk"),
                @"C:\Android\Sdk",
                @"C:\android-sdk",
            };
            foreach (var c in candidates)
                if (Directory.Exists(c)) return c;

            return @"";
        }

        public static string? GetJavaHome()
        {
            if (Directory.Exists(JdkHomeDir))
            {
                var javaExe = Directory.EnumerateFiles(JdkHomeDir, "java.exe", SearchOption.AllDirectories)
                    .FirstOrDefault(f => string.Equals(Path.GetFileName(Path.GetDirectoryName(f)), "bin",
                        StringComparison.OrdinalIgnoreCase));
                if (javaExe != null)
                    return Path.GetDirectoryName(Path.GetDirectoryName(javaExe));
            }

            var env = Environment.GetEnvironmentVariable("JAVA_HOME");
            if (!string.IsNullOrEmpty(env) && File.Exists(Path.Combine(env, "bin", "java.exe"))) return env;

            var paths = new[]
            {
                @"C:\Program Files\Eclipse Adoptium",
                @"C:\Program Files\Android\Android Studio\jbr",
                @"C:\Program Files\Java",
            };
            foreach (var p in paths)
            {
                if (!Directory.Exists(p)) continue;
                return Directory.GetDirectories(p)
                    .FirstOrDefault(d => File.Exists(Path.Combine(d, "bin", "java.exe")));
            }

            try
            {
                var psi = new ProcessStartInfo("java", "-version")
                {
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var p = Process.Start(psi);
                if (p != null && p.WaitForExit(2000)) return null;
            }
            catch { }
            return null;
        }

        public static string? GetGradleBat()
        {
            if (Directory.Exists(GradleHomeDir))
            {
                var bat = Directory.GetFiles(GradleHomeDir, "gradle.bat", SearchOption.AllDirectories)
                    .FirstOrDefault(f => string.Equals(Path.GetFileName(Path.GetDirectoryName(f)), "bin",
                        StringComparison.OrdinalIgnoreCase));
                if (bat != null) return bat;
            }

            var dists = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                                      ".gradle", "wrapper", "dists");
            if (Directory.Exists(dists))
            {
                foreach (var d in Directory.GetDirectories(dists, "gradle-*", SearchOption.TopDirectoryOnly))
                {
                    var bat = Directory.GetFiles(d, "gradle.bat", SearchOption.AllDirectories).FirstOrDefault();
                    if (bat != null) return bat;
                }
            }

            try
            {
                var psi = new ProcessStartInfo("where", "gradle.bat")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var p = Process.Start(psi);
                if (p != null)
                {
                    var output = p.StandardOutput.ReadToEnd();
                    p.WaitForExit();
                    if (p.ExitCode == 0)
                    {
                        var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                        if (lines.Length > 0 && File.Exists(lines[0])) return lines[0];
                    }
                }
            }
            catch { }
            return null;
        }

        public static bool IsJdkBundled() =>
            Directory.Exists(JdkHomeDir) &&
            Directory.EnumerateFiles(JdkHomeDir, "java.exe", SearchOption.AllDirectories)
                .Any(f => string.Equals(Path.GetFileName(Path.GetDirectoryName(f)), "bin",
                    StringComparison.OrdinalIgnoreCase));

        public static bool IsGradleBundled() =>
            Directory.Exists(GradleHomeDir) &&
            Directory.GetFiles(GradleHomeDir, "gradle.bat", SearchOption.AllDirectories)
                .Any(f => string.Equals(Path.GetFileName(Path.GetDirectoryName(f)), "bin",
                    StringComparison.OrdinalIgnoreCase));

        public static ToolStatus CheckJdk()
        {
            if (IsJdkBundled())
            {
                var jdk = GetJavaHome()!;
                return new ToolStatus(true, jdk, GetJavaVersion(jdk));
            }
            var sys = GetJavaHome();
            return new ToolStatus(false, sys ?? "",
                sys != null ? "System JDK detected - download to bundle" : "Not installed");
        }

        public static ToolStatus CheckAndroidSdk()
        {
            if (IsValidSdk(SdkDir))
                return new ToolStatus(true, SdkDir,
                    $"Bundled: platform {CompileSdkPlatform} + build-tools {BuildToolsVersion}");
            var sys = GetAndroidSdk();
            return new ToolStatus(false, sys,
                !string.IsNullOrEmpty(sys) ? "System SDK detected - download to bundle" : "Not installed");
        }

        public static ToolStatus CheckGradle()
        {
            if (IsGradleBundled())
            {
                var bat = GetGradleBat()!;
                var ver = Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(bat))) ?? "";
                return new ToolStatus(true, bat, string.IsNullOrEmpty(ver) ? "Gradle" : ver);
            }
            var sys = GetGradleBat();
            return new ToolStatus(false, sys ?? "",
                sys != null ? "System Gradle detected - download to bundle" : "Not installed");
        }

        public static string GetJavaVersion(string jdkHome)
        {
            try
            {
                var psi = new ProcessStartInfo(Path.Combine(jdkHome, "bin", "java.exe"), "-version")
                {
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var p = Process.Start(psi);
                if (p == null) return "JDK";
                var err = p.StandardError.ReadToEnd();
                if (!p.WaitForExit(5000)) return "JDK";
                var line = err.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                return string.IsNullOrWhiteSpace(line) ? "JDK" : line.Trim();
            }
            catch
            {
                return "JDK";
            }
        }

        public static async Task<string> InstallJdkAsync(IProgress<ProgressInfo> progress, CancellationToken ct)
        {
            Directory.CreateDirectory(ToolsDir);
            var zip = Path.Combine(ToolsDir, "jdk.zip");

            progress.Report(new ProgressInfo(0, "Downloading JDK 17 (Temurin)..."));
            await DownloadFileAsync(JdkZipUrl, zip, progress, ct);

            progress.Report(new ProgressInfo(95, "Extracting JDK..."));
            var extractTmp = Path.Combine(ToolsDir, "jdk-extract");
            if (Directory.Exists(extractTmp)) Directory.Delete(extractTmp, true);
            ZipFile.ExtractToDirectory(zip, extractTmp, overwriteFiles: true);
            try { File.Delete(zip); } catch { }

            var jdkRoot = Directory.GetDirectories(extractTmp)
                .FirstOrDefault(d => File.Exists(Path.Combine(d, "bin", "java.exe")));
            if (jdkRoot == null)
            {
                var inner = Directory.GetFiles(extractTmp, "java.exe", SearchOption.AllDirectories).FirstOrDefault();
                if (inner != null) jdkRoot = Path.GetDirectoryName(Path.GetDirectoryName(inner));
            }
            if (jdkRoot == null) throw new InvalidOperationException("Downloaded JDK zip did not contain a JDK home.");

            var name = new DirectoryInfo(jdkRoot).Name;
            var final = Path.Combine(JdkHomeDir, name);
            if (Directory.Exists(final)) Directory.Delete(final, true);
            Directory.CreateDirectory(JdkHomeDir);
            Directory.Move(jdkRoot, final);
            try { if (Directory.Exists(extractTmp)) Directory.Delete(extractTmp, true); } catch { }

            progress.Report(new ProgressInfo(100, "JDK ready: " + final));
            return final;
        }

        public static async Task InstallSdkAsync(IProgress<ProgressInfo> progress, CancellationToken ct)
        {
            Directory.CreateDirectory(SdkDir);
            var zip = Path.Combine(ToolsDir, "cmdline-tools.zip");

            progress.Report(new ProgressInfo(0, "Downloading Android command-line tools..."));
            await DownloadFileAsync(CmdlineToolsZipUrl, zip, progress, ct);

            progress.Report(new ProgressInfo(80, "Extracting command-line tools..."));
            var extractTmp = Path.Combine(ToolsDir, "clt-extract");
            if (Directory.Exists(extractTmp)) Directory.Delete(extractTmp, true);
            ZipFile.ExtractToDirectory(zip, extractTmp, overwriteFiles: true);
            try { File.Delete(zip); } catch { }

            var srcClt = Path.Combine(extractTmp, "cmdline-tools");
            if (!Directory.Exists(srcClt))
                srcClt = Directory.GetDirectories(extractTmp).FirstOrDefault();
            if (srcClt == null) throw new InvalidOperationException("commandline-tools zip layout unexpected.");

            var latest = Path.Combine(SdkDir, "cmdline-tools", "latest");
            Directory.CreateDirectory(Path.Combine(SdkDir, "cmdline-tools"));
            if (Directory.Exists(latest)) Directory.Delete(latest, true);
            Directory.Move(srcClt, latest);
            try { if (Directory.Exists(extractTmp)) Directory.Delete(extractTmp, true); } catch { }

            await InstallSdkComponentsAsync(progress, ct);
        }

        public static async Task InstallSdkComponentsAsync(IProgress<ProgressInfo> progress, CancellationToken ct)
        {
            var jdk = GetJavaHome() ?? throw new InvalidOperationException("Install the JDK before the SDK.");
            var mgr = Path.Combine(SdkDir, "cmdline-tools", "latest", "bin", "sdkmanager.bat");
            if (!File.Exists(mgr)) throw new InvalidOperationException("sdkmanager.bat not found after extraction.");

            progress.Report(new ProgressInfo(0, "Accepting SDK licenses..."));
            await RunProcessAsync(mgr, $"--sdk_root=\"{SdkDir}\" --licenses", jdk, ct, true,
                l => progress.Report(new ProgressInfo(-1, "  license: " + Trim(l))));

            progress.Report(new ProgressInfo(40, "Installing platform-tools, build-tools " + BuildToolsVersion +
                                                ", platforms " + CompileSdkPlatform + "..."));
            var components = string.Join(' ', SdkComponents.Select(c => $"\"{c}\""));
            await RunProcessAsync(mgr, $"--sdk_root=\"{SdkDir}\" {components}", jdk, ct, true,
                l => progress.Report(new ProgressInfo(-1, "  sdk: " + Trim(l))));

            progress.Report(new ProgressInfo(100, "Android SDK ready."));
        }

        public static async Task<string> InstallGradleAsync(IProgress<ProgressInfo> progress, CancellationToken ct)
        {
            Directory.CreateDirectory(ToolsDir);
            var zip = Path.Combine(ToolsDir, "gradle.zip");

            progress.Report(new ProgressInfo(0, "Downloading Gradle " + GradleVersion + "..."));
            await DownloadFileAsync(GradleZipUrl, zip, progress, ct);

            progress.Report(new ProgressInfo(90, "Extracting Gradle..."));
            if (Directory.Exists(GradleHomeDir))
            {
                foreach (var d in Directory.GetDirectories(GradleHomeDir, "gradle-*"))
                {
                    try { Directory.Delete(d, true); } catch { }
                }
            }
            Directory.CreateDirectory(GradleHomeDir);
            ZipFile.ExtractToDirectory(zip, GradleHomeDir, overwriteFiles: true);
            try { File.Delete(zip); } catch { }

            var bat = GetGradleBat();
            if (bat == null) throw new InvalidOperationException("Gradle extraction did not yield gradle.bat.");
            progress.Report(new ProgressInfo(100, "Gradle ready: " + bat));
            return bat;
        }

        public static async Task InstallAllAsync(IProgress<ProgressInfo> progress, CancellationToken ct)
        {
            Directory.CreateDirectory(ToolsDir);
            Directory.CreateDirectory(GradleUserHome);

            progress.Report(new ProgressInfo(0, "Step 1/3: JDK 17"));
            await InstallJdkAsync(progress, ct);

            progress.Report(new ProgressInfo(0, "Step 2/3: Android SDK"));
            await InstallSdkAsync(progress, ct);

            progress.Report(new ProgressInfo(0, "Step 3/3: Gradle " + GradleVersion));
            await InstallGradleAsync(progress, ct);

            progress.Report(new ProgressInfo(100, "Build environment fully configured and bundled with this app."));
        }

        public static void EnsureGradleCacheDir()
        {
            Directory.CreateDirectory(GradleUserHome);
            Directory.CreateDirectory(ToolsDir);
        }

        private static async Task DownloadFileAsync(string url, string destPath,
            IProgress<ProgressInfo> progress, CancellationToken ct)
        {
            using var handler = new HttpClientHandler { AllowAutoRedirect = true };
            using var http = new HttpClient(handler, true) { Timeout = TimeSpan.FromMinutes(30) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("DiverRaT-Setup/1.0");

            using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();

            var total = resp.Content.Headers.ContentLength ?? -1;
            var dir = Path.GetDirectoryName(destPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            await using var fs = File.Create(destPath);
            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            var buffer = new byte[81920];
            long read = 0;
            int n;
            var lastReport = DateTime.UtcNow;
            double lastPct = -1;
            while ((n = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
            {
                await fs.WriteAsync(buffer.AsMemory(0, n), ct);
                read += n;

                double pct = total > 0 ? (double)read / total * 100.0 : -1;
                bool isFinal = read == total;
                bool changed = pct >= 0 && (pct - lastPct) >= 2.0;
                bool elapsed = (DateTime.UtcNow - lastReport).TotalMilliseconds >= 250;
                if (isFinal || changed || elapsed)
                {
                    lastReport = DateTime.UtcNow;
                    lastPct = pct;
                    if (total > 0)
                        progress.Report(new ProgressInfo(pct, "Downloading... " +
                            (read / 1024 / 1024) + " / " + (total / 1024 / 1024) + " MB"));
                    else
                        progress.Report(new ProgressInfo(-1, "Downloading... " + (read / 1024 / 1024) + " MB"));
                }
            }
        }

        private static async Task<int> RunProcessAsync(string file, string arguments, string jdkHome,
            CancellationToken ct, bool yesInput, Action<string>? onLine)
        {
            var psi = new ProcessStartInfo(file, arguments)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = yesInput,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(file) ?? ""
            };
            if (!string.IsNullOrEmpty(jdkHome)) psi.EnvironmentVariables["JAVA_HOME"] = jdkHome;
            psi.EnvironmentVariables["ANDROID_HOME"] = SdkDir;
            psi.EnvironmentVariables["ANDROID_SDK_ROOT"] = SdkDir;

            using var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
            p.Start();

            var outTask = ConsumeAsync(p.StandardOutput, onLine, ct);
            var errTask = ConsumeAsync(p.StandardError, onLine, ct);

            if (yesInput)
            {
                var w = p.StandardInput;
                for (int i = 0; i < 40; i++) w.WriteLine("y");
                w.Close();
            }

            try
            {
                await p.WaitForExitAsync(ct);
            }
            catch (OperationCanceledException)
            {
                try { if (!p.HasExited) p.Kill(); } catch { }
                throw;
            }
            await outTask;
            await errTask;
            return p.ExitCode;
        }

        private static async Task ConsumeAsync(StreamReader reader, Action<string>? onLine, CancellationToken ct)
        {
            var last = DateTime.MinValue;
            string? line;
            while ((line = await reader.ReadLineAsync(ct)) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var now = DateTime.UtcNow;
                if ((now - last).TotalMilliseconds < 500) continue;
                last = now;
                onLine?.Invoke(line);
            }
        }

        private static string Trim(string s) =>
            string.IsNullOrWhiteSpace(s) ? "" : (s.Length > 200 ? s.Substring(0, 200) + "..." : s);
    }
}