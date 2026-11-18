using System.Diagnostics;
using System.Net.WebSockets;
using System.Text.Json;

namespace NoSvMania;

/// <param name="StateName">tosu game state, e.g. "play", "menu", "selectPlay".</param>
/// <param name="LiveTimeMs">Current audio position in map time (ms).</param>
/// <param name="BeatmapPath">Full path to the loaded .osu file, or "" if unknown.</param>
/// <param name="SkinPath">Full path to the active skin folder, or "" if unknown.</param>
/// <param name="Mods">Mod bitmask.</param>
/// <param name="StampTicks">Stopwatch timestamp when this message arrived.</param>
public sealed record TosuState(
    string StateName,
    double LiveTimeMs,
    string BeatmapPath,
    string SkinPath,
    long Mods,
    long StampTicks);

/// <summary>
/// Listens to tosu's v2 websocket (default ws://127.0.0.1:24050/websocket/v2) on a
/// background task and keeps the latest snapshot. Field names follow tosu's v2 schema;
/// if your tosu version differs, open http://127.0.0.1:24050/json/v2 and adjust Parse().
/// </summary>
public sealed class TosuClient : IDisposable
{
    private readonly string _url;
    private readonly CancellationTokenSource _cts = new();
    private TosuState? _latest;

    public bool Connected { get; private set; }
    public TosuState? Latest => Volatile.Read(ref _latest);

    public TosuClient(string url)
    {
        _url = url;
        Task.Run(Loop);
    }

    private async Task Loop()
    {
        var buffer = new byte[1 << 16];
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                using var ws = new ClientWebSocket();
                await ws.ConnectAsync(new Uri(_url), _cts.Token);
                Connected = true;

                using var ms = new MemoryStream();
                while (ws.State == WebSocketState.Open && !_cts.IsCancellationRequested)
                {
                    ms.SetLength(0);
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token);
                        if (result.MessageType == WebSocketMessageType.Close) break;
                        ms.Write(buffer, 0, result.Count);
                    } while (!result.EndOfMessage);

                    if (result.MessageType == WebSocketMessageType.Close) break;
                    TryParse(ms.ToArray());
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // tosu not running yet or connection dropped: retry below
            }

            Connected = false;
            try { await Task.Delay(1500, _cts.Token); }
            catch (OperationCanceledException) { break; }
        }
    }

    private void TryParse(byte[] data)
    {
        try
        {
            using var doc = JsonDocument.Parse(data);
            var root = doc.RootElement;

            string stateName = Str(root, "state", "name") ?? "";
            double live = Num(root, "beatmap", "time", "live") ?? 0;

            string game = Str(root, "folders", "game") ?? "";
            string songs = Str(root, "folders", "songs")
                           ?? Str(root, "settings", "folders", "songs")
                           ?? (game.Length > 0 ? Path.Combine(game, "Songs") : "");
            string folder = Str(root, "folders", "beatmap") ?? "";
            string file = Str(root, "files", "beatmap") ?? "";
            string? direct = Str(root, "directPath", "beatmapFile");

            string beatmapPath = "";
            if (!string.IsNullOrEmpty(direct) && File.Exists(direct))
            {
                beatmapPath = direct;
            }
            else if (file.Length > 0)
            {
                // Path.Combine restarts at any rooted segment, so this works whether
                // folders.beatmap is a bare folder name or a full path.
                string candidate = Path.Combine(songs, folder, file);
                if (File.Exists(candidate)) beatmapPath = candidate;
            }

            string skin = Str(root, "folders", "skin") ?? "";
            if (skin.Length > 0 && !Directory.Exists(skin) && game.Length > 0)
            {
                string alt = Path.Combine(game, "Skins", skin);
                skin = Directory.Exists(alt) ? alt : "";
            }

            double? mods = stateName == "play"
                ? Num(root, "play", "mods", "number") ?? Num(root, "menu", "mods", "number")
                : Num(root, "menu", "mods", "number") ?? Num(root, "play", "mods", "number");

            Volatile.Write(ref _latest, new TosuState(
                stateName,
                live,
                beatmapPath,
                skin,
                (long)(mods ?? 0),
                Stopwatch.GetTimestamp()));
        }
        catch
        {
            // ignore malformed or unexpected messages
        }
    }

    private static JsonElement? Dig(JsonElement element, params string[] path)
    {
        foreach (var key in path)
        {
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(key, out element))
                return null;
        }
        return element;
    }

    private static string? Str(JsonElement root, params string[] path)
    {
        var e = Dig(root, path);
        return e is { ValueKind: JsonValueKind.String } s ? s.GetString() : null;
    }

    private static double? Num(JsonElement root, params string[] path)
    {
        var e = Dig(root, path);
        return e is { ValueKind: JsonValueKind.Number } n ? n.GetDouble() : null;
    }

    public void Dispose() => _cts.Cancel();
}
