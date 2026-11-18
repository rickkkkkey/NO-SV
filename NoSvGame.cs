using System.Diagnostics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace NoSvMania;

public sealed class NoSvGame : Game
{
    private const long MirrorFlag = 1L << 30;
    private const long DoubleTimeFlags = 64 | 512; // DT | NC
    private const long HalfTimeFlag = 256;

    // Same constant lazer uses to turn a 1-40 scroll speed into "ms visible on screen".
    // Only an approximation of stable: adjust the speed setting until it feels the same.
    private const double MaxScrollTimeMs = 11485.0;

    private readonly GraphicsDeviceManager _gfx;
    private readonly Settings _s;
    private readonly TosuClient _tosu;

    private SpriteBatch _sb = null!;
    private Texture2D _px = null!;
    private TextCache _text = null!;

    private ManiaSkin? _skin;
    private string _skinPath = "\0";

    private ManiaChart? _chart;
    private string _chartKey = "";
    private Task<ManiaChart?>? _loading;
    private string _loadingKey = "";

    // clock
    private double _time;
    private double _anchorLive;
    private long _anchorTicks;
    private double _prevLive = double.NaN;
    private long _lastStamp;
    private int _sameCount;
    private bool _moving;

    // settings overlay
    private bool _settingsOpen;
    private int _rebind = -1;
    private KeyboardState _prevKb;

    public NoSvGame()
    {
        _s = Settings.Load();
        _gfx = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = _s.WindowWidth,
            PreferredBackBufferHeight = _s.WindowHeight,
            SynchronizeWithVerticalRetrace = true,
        };
        Window.AllowUserResizing = true;
        Window.Title = "NoSvMania - companion";
        IsMouseVisible = true;
        IsFixedTimeStep = false;
        InactiveSleepTime = TimeSpan.Zero; // keep rendering while osu! has focus
        _tosu = new TosuClient(_s.TosuUrl);
    }

    protected override void LoadContent()
    {
        _sb = new SpriteBatch(GraphicsDevice);
        _px = new Texture2D(GraphicsDevice, 1, 1);
        _px.SetData(new[] { Color.White });
        _text = new TextCache(GraphicsDevice);
    }

    protected override void OnExiting(object sender, ExitingEventArgs args)
    {
        _s.WindowWidth = Window.ClientBounds.Width;
        _s.WindowHeight = Window.ClientBounds.Height;
        _s.Save();
        _tosu.Dispose();
        base.OnExiting(sender, args);
    }

    // ------------------------------------------------------------------ update

    protected override void Update(GameTime gameTime)
    {
        var kb = Keyboard.GetState();
        if (IsActive) HandleSettingsInput(kb);
        _prevKb = kb;

        var st = _tosu.Latest;
        if (st != null)
        {
            SyncSkin(st);
            SyncChart(st);
            AdvanceClock(st, gameTime.ElapsedGameTime.TotalMilliseconds);
        }

        base.Update(gameTime);
    }

    private void HandleSettingsInput(KeyboardState kb)
    {
        bool Pressed(Keys k) => kb.IsKeyDown(k) && _prevKb.IsKeyUp(k);

        if (_rebind >= 0)
        {
            foreach (var k in kb.GetPressedKeys())
            {
                if (_prevKb.IsKeyDown(k)) continue;
                if (k != Keys.Escape)
                {
                    _s.KeyBinds[_rebind] = k;
                    _s.Save();
                }
                _rebind = -1;
                break;
            }
            return;
        }

        if (Pressed(Keys.F1))
        {
            _settingsOpen = !_settingsOpen;
            if (!_settingsOpen) _s.Save();
            return;
        }
        if (!_settingsOpen) return;

        if (Pressed(Keys.Escape)) { _settingsOpen = false; _s.Save(); return; }

        if (Pressed(Keys.D1)) _rebind = 0;
        else if (Pressed(Keys.D2)) _rebind = 1;
        else if (Pressed(Keys.D3)) _rebind = 2;
        else if (Pressed(Keys.D4)) _rebind = 3;

        bool changed = false;
        if (Pressed(Keys.Up)) { _s.ScrollSpeed = Math.Min(40, _s.ScrollSpeed + 1); changed = true; }
        if (Pressed(Keys.Down)) { _s.ScrollSpeed = Math.Max(1, _s.ScrollSpeed - 1); changed = true; }
        if (Pressed(Keys.Right)) { _s.OffsetMs += 5; changed = true; }
        if (Pressed(Keys.Left)) { _s.OffsetMs -= 5; changed = true; }
        if (Pressed(Keys.N)) { _s.Normalize = !_s.Normalize; changed = true; }
        if (Pressed(Keys.B)) { _s.BpmScalesScroll = !_s.BpmScalesScroll; changed = true; }
        if (changed) _s.Save();
    }

    private void SyncSkin(TosuState st)
    {
        string wanted = !string.IsNullOrWhiteSpace(_s.SkinPathOverride) ? _s.SkinPathOverride : st.SkinPath;
        if (wanted == _skinPath) return;
        _skinPath = wanted;
        _skin?.Dispose();
        _skin = ManiaSkin.Load(GraphicsDevice, wanted);
    }

    private void SyncChart(TosuState st)
    {
        if (_loading is { IsCompleted: true })
        {
            _chart = _loading.Result;
            _chartKey = _loadingKey;
            _loading = null;
        }

        if (_loading != null || st.BeatmapPath.Length == 0) return;

        string wanted = $"{st.BeatmapPath}|{_s.Normalize}|{_s.BpmScalesScroll}|{_s.CompensationTolerance}";
        if (wanted == _chartKey) return;

        _loadingKey = wanted;
        string path = st.BeatmapPath;
        bool normalize = _s.Normalize;
        bool bpmScales = _s.BpmScalesScroll;
        double tol = _s.CompensationTolerance;
        _loading = Task.Run(() =>
        {
            try { return ManiaChart.Load(path, normalize, bpmScales, tol); }
            catch { return null; }
        });
    }

    private static double RateFor(long mods)
    {
        if ((mods & DoubleTimeFlags) != 0) return 1.5;
        if ((mods & HalfTimeFlag) != 0) return 0.75;
        return 1.0;
    }

    private void AdvanceClock(TosuState st, double dtMs)
    {
        if (st.StampTicks != _lastStamp)
        {
            _lastStamp = st.StampTicks;
            if (double.IsNaN(_prevLive) || Math.Abs(st.LiveTimeMs - _prevLive) > 0.01)
            {
                _sameCount = 0;
                _moving = true;
            }
            else if (++_sameCount >= 2)
            {
                _moving = false; // two identical readings in a row = paused
            }
            _prevLive = st.LiveTimeMs;
            _anchorLive = st.LiveTimeMs;
            _anchorTicks = st.StampTicks;
        }

        double rate = RateFor(st.Mods);
        if (!_moving)
        {
            _time = _anchorLive;
            return;
        }

        double sinceAnchorMs = (Stopwatch.GetTimestamp() - _anchorTicks) * 1000.0 / Stopwatch.Frequency;
        double target = _anchorLive + sinceAnchorMs * rate;

        _time += dtMs * rate;
        double error = target - _time;
        if (Math.Abs(error) > 120) _time = target;   // seek / retry / restart
        else _time += error * 0.1;                   // gentle drift correction
    }

    // -------------------------------------------------------------------- draw

    private void FillRect(float x, float y, float w, float h, Color c)
        => _sb.Draw(_px, new Rectangle((int)MathF.Round(x), (int)MathF.Round(y),
            Math.Max(1, (int)MathF.Round(w)), Math.Max(1, (int)MathF.Round(h))), c);

    private static float HeightFor(Texture2D? t, float width, float fallback)
        => t == null ? fallback : t.Height * width / t.Width;

    private void DrawImage(Texture2D t, float x, float yBottom, float w, bool flip = false)
    {
        float h = t.Height * w / t.Width;
        var rect = new Rectangle((int)MathF.Round(x), (int)MathF.Round(yBottom - h),
            Math.Max(1, (int)MathF.Round(w)), Math.Max(1, (int)MathF.Round(h)));
        _sb.Draw(t, rect, null, Color.White, 0f, Vector2.Zero,
            flip ? SpriteEffects.FlipVertically : SpriteEffects.None, 0f);
    }

    private void DrawText(string s, int size, float x, float y, Color c)
        => _sb.Draw(_text.Get(s, size), new Vector2(x, y), c);

    private static int FirstEndingAtOrAfter(Note[] notes, double pos)
    {
        int lo = 0, hi = notes.Length;
        while (lo < hi)
        {
            int mid = (lo + hi) >> 1;
            if (notes[mid].EndPos >= pos) hi = mid; else lo = mid + 1;
        }
        return lo;
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(new Color(10, 10, 14));

        int width = GraphicsDevice.Viewport.Width;
        int height = GraphicsDevice.Viewport.Height;
        float scale = height / 480f;

        var skin = _skin;
        float[] colWidths = skin?.ColumnWidth ?? new float[] { 30, 30, 30, 30 };
        var xs = new float[4];
        var ws = new float[4];
        float total = 0;
        for (int c = 0; c < 4; c++) { ws[c] = colWidths[c] * scale; total += ws[c]; }
        float left = (width - total) / 2f;
        float cursor = left;
        for (int c = 0; c < 4; c++) { xs[c] = cursor; cursor += ws[c]; }

        float hitY = (skin?.HitPosition ?? 402f) * scale;
        var st = _tosu.Latest;
        bool mirror = st != null && (st.Mods & MirrorFlag) != 0;
        var chart = _chart;

        _sb.Begin(blendState: BlendState.NonPremultiplied, samplerState: SamplerState.LinearClamp);

        // stage
        FillRect(left, 0, total, height, new Color(0, 0, 0, 170));
        for (int c = 0; c <= 4; c++)
        {
            float lx = c < 4 ? xs[c] : left + total;
            FillRect(lx - 0.5f, 0, 1, height, new Color(255, 255, 255, 40));
        }
        FillRect(left, hitY, total, 2, new Color(255, 255, 255, 70));

        // notes
        if (chart != null)
        {
            double curPos = chart.Pos(_time + _s.OffsetMs);
            double pxPerUnit = hitY / (MaxScrollTimeMs / _s.ScrollSpeed);
            double lookAhead = (hitY + 200f * scale) / pxPerUnit;

            for (int c = 0; c < 4; c++)
            {
                var lane = chart.Notes[mirror ? 3 - c : c];
                var noteTex = skin?.Note[c];
                var headTex = skin?.Head[c];
                var bodyTex = skin?.Body[c];
                var tailTex = skin?.Tail[c];
                Color fallback = (c == 0 || c == 3) ? new Color(235, 235, 235) : new Color(110, 170, 255);
                float fallbackH = 14f * scale;

                for (int i = FirstEndingAtOrAfter(lane, curPos); i < lane.Length; i++)
                {
                    var n = lane[i];
                    if (n.StartPos - curPos > lookAhead) break;

                    float yHead = hitY - (float)(Math.Max(0, n.StartPos - curPos) * pxPerUnit);

                    if (!n.IsHold)
                    {
                        if (noteTex != null) DrawImage(noteTex, xs[c], yHead, ws[c]);
                        else FillRect(xs[c] + 1, yHead - fallbackH, ws[c] - 2, fallbackH, fallback);
                        continue;
                    }

                    float yTail = hitY - (float)((n.EndPos - curPos) * pxPerUnit);
                    float headH = HeightFor(headTex, ws[c], fallbackH);
                    float tailH = HeightFor(tailTex ?? headTex, ws[c], fallbackH);

                    float bodyTop = yTail - tailH / 2f;
                    float bodyBottom = yHead - headH / 2f;
                    if (bodyBottom > bodyTop)
                    {
                        var rect = new Rectangle((int)MathF.Round(xs[c]), (int)MathF.Round(bodyTop),
                            Math.Max(1, (int)MathF.Round(ws[c])), Math.Max(1, (int)MathF.Round(bodyBottom - bodyTop)));
                        if (bodyTex != null) _sb.Draw(bodyTex, rect, Color.White);
                        else _sb.Draw(_px, rect, new Color(fallback.R, fallback.G, fallback.B, 150));
                    }

                    if (tailTex != null) DrawImage(tailTex, xs[c], yTail, ws[c]);
                    else if (headTex != null) DrawImage(headTex, xs[c], yTail, ws[c], flip: true);
                    else FillRect(xs[c] + 1, yTail - fallbackH, ws[c] - 2, fallbackH, fallback);

                    if (headTex != null) DrawImage(headTex, xs[c], yHead, ws[c]);
                    else FillRect(xs[c] + 1, yHead - fallbackH, ws[c] - 2, fallbackH, fallback);
                }
            }
        }

        // keys
        for (int c = 0; c < 4; c++)
        {
            bool down = NativeKeys.IsDown(_s.KeyBinds[c]);
            var tex = down ? skin?.KeyDown[c] : skin?.Key[c];
            if (tex != null)
            {
                DrawImage(tex, xs[c], height, ws[c]);
            }
            else
            {
                float keyH = 60f * scale;
                Color col = down ? new Color(255, 255, 255, 220) : new Color(255, 255, 255, 50);
                FillRect(xs[c] + 2, height - keyH, ws[c] - 4, keyH, col);
            }
        }

        // status / settings
        string status = !_tosu.Connected ? "waiting for tosu..."
            : chart != null ? $"{st?.StateName}  |  base {chart.BaseBpm:0.#} BPM  |  SV kept {chart.SvKept}, removed {chart.SvRemoved}"
            : _loading != null ? "loading chart..."
            : st != null && st.BeatmapPath.Length > 0 ? "not a 4K mania map"
            : "no map detected";
        DrawText(status, 13, 6, 4, new Color(255, 255, 255, 140));
        if (!_settingsOpen) DrawText("F1: settings", 13, 6, 22, new Color(255, 255, 255, 90));

        if (_settingsOpen) DrawSettings(width);

        _sb.End();
        base.Draw(gameTime);
    }

    private void DrawSettings(int width)
    {
        FillRect(0, 0, width, 215, new Color(0, 0, 0, 225));
        var white = new Color(255, 255, 255, 235);
        var dim = new Color(255, 255, 255, 150);

        string keys = string.Join("   ", _s.KeyBinds.Select((k, i) => $"{i + 1}: {k}"));
        DrawText("SETTINGS   (F1 or Esc to close)", 16, 10, 8, white);
        DrawText(_rebind >= 0 ? $"Press the new key for lane {_rebind + 1}  (Esc cancels)" : keys, 15, 10, 38,
            _rebind >= 0 ? new Color(255, 220, 120) : white);
        DrawText("press 1-4 to rebind a lane", 12, 10, 58, dim);
        DrawText($"Scroll speed: {_s.ScrollSpeed}      Up / Down", 15, 10, 82, white);
        DrawText($"Mode: {(_s.Normalize ? "Normalize (constant speed)" : "Strip SV (keep BPM compensation)")}      N", 15, 10, 108, white);
        DrawText($"BPM scales scroll: {(_s.BpmScalesScroll ? "on" : "off")}      B", 15, 10, 134, white);
        DrawText($"Clock offset: {_s.OffsetMs} ms      Left / Right", 15, 10, 160, white);
        DrawText("Mirror and DT are picked up automatically.", 12, 10, 190, dim);
    }
}
