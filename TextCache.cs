using System.Runtime.InteropServices;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SD = System.Drawing;

namespace NoSvMania;

/// <summary>
/// Renders strings to white-on-transparent textures with System.Drawing (Windows only) and
/// caches them, so the app needs no SpriteFont / content pipeline. Tint with the draw colour.
/// Only use for text that changes rarely (settings labels, status lines).
/// </summary>
public sealed class TextCache : IDisposable
{
    private readonly GraphicsDevice _gd;
    private readonly Dictionary<(string, int), Texture2D> _cache = new();

    public TextCache(GraphicsDevice gd) => _gd = gd;

    public Texture2D Get(string text, int pixelSize)
    {
        if (_cache.TryGetValue((text, pixelSize), out var cached)) return cached;

        if (_cache.Count > 300)
        {
            foreach (var t in _cache.Values) t.Dispose();
            _cache.Clear();
        }

        using var font = new SD.Font("Segoe UI", pixelSize, SD.FontStyle.Regular, SD.GraphicsUnit.Pixel);

        SD.SizeF size;
        using (var probe = new SD.Bitmap(1, 1))
        using (var g0 = SD.Graphics.FromImage(probe))
            size = g0.MeasureString(text, font);

        int w = Math.Max(1, (int)Math.Ceiling(size.Width) + 2);
        int h = Math.Max(1, (int)Math.Ceiling(size.Height) + 2);

        using var bmp = new SD.Bitmap(w, h, SD.Imaging.PixelFormat.Format32bppArgb);
        using (var g = SD.Graphics.FromImage(bmp))
        {
            g.Clear(SD.Color.Transparent);
            g.TextRenderingHint = SD.Text.TextRenderingHint.AntiAlias;
            g.DrawString(text, font, SD.Brushes.White, 0, 0);
        }

        var data = bmp.LockBits(new SD.Rectangle(0, 0, w, h), SD.Imaging.ImageLockMode.ReadOnly,
            SD.Imaging.PixelFormat.Format32bppArgb);
        var bytes = new byte[data.Stride * h];
        Marshal.Copy(data.Scan0, bytes, 0, bytes.Length);
        int stride = data.Stride;
        bmp.UnlockBits(data);

        var pixels = new Color[w * h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int alpha = bytes[y * stride + x * 4 + 3];
                pixels[y * w + x] = new Color(255, 255, 255, alpha);
            }
        }

        var tex = new Texture2D(_gd, w, h);
        tex.SetData(pixels);
        _cache[(text, pixelSize)] = tex;
        return tex;
    }

    public void Dispose()
    {
        foreach (var t in _cache.Values) t.Dispose();
        _cache.Clear();
    }
}
