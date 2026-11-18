using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Xna.Framework.Input;

namespace NoSvMania;

/// <summary>User settings, stored next to the exe as settings.json.</summary>
public sealed class Settings
{
    /// <summary>Keys for the 4 lanes, left to right. Read globally, so they work while osu! is focused.</summary>
    public Keys[] KeyBinds { get; set; } = { Keys.D, Keys.F, Keys.J, Keys.K };

    /// <summary>Scroll speed on a 1-40 scale (higher = faster). Tune until it feels like your in-game speed.</summary>
    public int ScrollSpeed { get; set; } = 24;

    /// <summary>true = one constant scroll speed for the whole map. false = strip SV but keep BPM compensation.</summary>
    public bool Normalize { get; set; }

    /// <summary>Assume osu!stable scales scroll speed with BPM relative to the map's main BPM.</summary>
    public bool BpmScalesScroll { get; set; } = true;

    /// <summary>How close (fraction) a green line must be to the BPM-compensating value to be kept.</summary>
    public double CompensationTolerance { get; set; } = 0.03;

    /// <summary>Shift the companion's clock (ms) if it looks early/late compared to the game.</summary>
    public int OffsetMs { get; set; }

    public string TosuUrl { get; set; } = "ws://127.0.0.1:24050/websocket/v2";

    /// <summary>Optional: full path to a skin folder to use instead of the one tosu reports.</summary>
    public string SkinPathOverride { get; set; } = "";

    public int WindowWidth { get; set; } = 420;
    public int WindowHeight { get; set; } = 720;

    static string FilePath => Path.Combine(AppContext.BaseDirectory, "settings.json");

    static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static Settings Load()
    {
        Settings s;
        try
        {
            s = File.Exists(FilePath)
                ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath), Options) ?? new Settings()
                : new Settings();
        }
        catch
        {
            s = new Settings();
        }

        if (s.KeyBinds == null || s.KeyBinds.Length != 4)
            s.KeyBinds = new[] { Keys.D, Keys.F, Keys.J, Keys.K };
        s.ScrollSpeed = Math.Clamp(s.ScrollSpeed, 1, 40);
        s.WindowWidth = Math.Max(200, s.WindowWidth);
        s.WindowHeight = Math.Max(300, s.WindowHeight);
        s.Save();
        return s;
    }

    public void Save()
    {
        try { File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Options)); }
        catch { /* read-only folder etc. - not fatal */ }
    }
}
