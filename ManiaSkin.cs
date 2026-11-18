using System.Globalization;
using Microsoft.Xna.Framework.Graphics;

namespace NoSvMania;

/// <summary>
/// Loads the 4K [Mania] section of an osu!stable skin: column widths, hit position,
/// note / hold head / hold body / hold tail images and key images. Anything missing is left
/// null and the renderer falls back to plain rectangles.
/// Sizes are in osu! "480p pixels"; the renderer scales them by window height / 480.
/// </summary>
public sealed class ManiaSkin : IDisposable
{
    public float[] ColumnWidth { get; private set; } = { 30, 30, 30, 30 };
    public float HitPosition { get; private set; } = 402;

    public Texture2D?[] Note { get; } = new Texture2D?[4];
    public Texture2D?[] Head { get; } = new Texture2D?[4];
    public Texture2D?[] Body { get; } = new Texture2D?[4];
    public Texture2D?[] Tail { get; } = new Texture2D?[4];
    public Texture2D?[] Key { get; } = new Texture2D?[4];
    public Texture2D?[] KeyDown { get; } = new Texture2D?[4];

    private readonly List<Texture2D> _owned = new();

    public static ManiaSkin Load(GraphicsDevice gd, string dir)
    {
        var skin = new ManiaSkin();
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) return skin;

        var d = ReadManiaSection(Path.Combine(dir, "skin.ini"));

        if (d.TryGetValue("ColumnWidth", out var widths))
        {
            var parts = widths.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var arr = new float[4];
            float last = 30;
            for (int i = 0; i < 4; i++)
            {
                if (i < parts.Length && float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                    last = v;
                arr[i] = last;
            }
            skin.ColumnWidth = arr;
        }

        if (d.TryGetValue("HitPosition", out var hp)
            && float.TryParse(hp, NumberStyles.Float, CultureInfo.InvariantCulture, out var hitPos))
            skin.HitPosition = Math.Clamp(hitPos, 240, 480);

        for (int i = 0; i < 4; i++)
        {
            bool outer = i == 0 || i == 3; // default 4K pattern: note1, note2, note2, note1
            string noteDefault = outer ? "mania-note1" : "mania-note2";
            string keyDefault = outer ? "mania-key1" : "mania-key2";

            string note = Pick(d, $"NoteImage{i}", noteDefault);
            skin.Note[i] = skin.Tex(gd, dir, note);
            skin.Head[i] = skin.Tex(gd, dir, Pick(d, $"NoteImage{i}H", noteDefault + "H")) ?? skin.Note[i];
            skin.Body[i] = skin.Tex(gd, dir, Pick(d, $"NoteImage{i}L", noteDefault + "L"));
            skin.Tail[i] = skin.Tex(gd, dir, Pick(d, $"NoteImage{i}T", noteDefault + "T"));

            string key = Pick(d, $"KeyImage{i}", keyDefault);
            skin.Key[i] = skin.Tex(gd, dir, key);
            skin.KeyDown[i] = skin.Tex(gd, dir, Pick(d, $"KeyImage{i}D", keyDefault + "D")) ?? skin.Key[i];
        }

        return skin;
    }

    private static string Pick(Dictionary<string, string> d, string key, string fallback)
        => d.TryGetValue(key, out var v) && v.Length > 0 ? v : fallback;

    /// <summary>Returns the key/value pairs of the [Mania] section that has "Keys: 4".</summary>
    private static Dictionary<string, string> ReadManiaSection(string iniPath)
    {
        var sections = new List<Dictionary<string, string>>();
        if (!File.Exists(iniPath)) return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            Dictionary<string, string>? current = null;
            foreach (var raw in File.ReadLines(iniPath))
            {
                string line = raw.Trim();
                int comment = line.IndexOf("//", StringComparison.Ordinal);
                if (comment >= 0) line = line[..comment].Trim();
                if (line.Length == 0) continue;

                if (line[0] == '[' && line[^1] == ']')
                {
                    if (line.Equals("[Mania]", StringComparison.OrdinalIgnoreCase))
                    {
                        current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        sections.Add(current);
                    }
                    else
                    {
                        current = null;
                    }
                    continue;
                }

                if (current == null) continue;
                int colon = line.IndexOf(':');
                if (colon <= 0) continue;
                current[line[..colon].Trim()] = line[(colon + 1)..].Trim();
            }
        }
        catch
        {
            // unreadable skin.ini: use defaults
        }

        foreach (var s in sections)
            if (s.TryGetValue("Keys", out var k) && k == "4") return s;

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    private Texture2D? Tex(GraphicsDevice gd, string dir, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        if (name.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) name = name[..^4];

        foreach (var candidate in new[] { name + "@2x.png", name + ".png" })
        {
            string path = Path.Combine(dir, candidate);
            if (!File.Exists(path)) continue;
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                var tex = Texture2D.FromStream(gd, fs);
                _owned.Add(tex);
                return tex;
            }
            catch
            {
                // corrupt image: try the next candidate
            }
        }
        return null;
    }

    public void Dispose()
    {
        foreach (var t in _owned) t.Dispose();
        _owned.Clear();
    }
}
