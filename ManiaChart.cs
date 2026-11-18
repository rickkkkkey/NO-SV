using System.Globalization;
using System.Text;

namespace NoSvMania;

/// <param name="Start">Start time (ms).</param>
/// <param name="End">End time (ms); equals Start for tap notes.</param>
/// <param name="StartPos">Scroll position of the start under the processed SV.</param>
/// <param name="EndPos">Scroll position of the end under the processed SV.</param>
public readonly record struct Note(double Start, double End, bool IsHold, double StartPos, double EndPos);

/// <summary>
/// A 4K osu!mania chart with SV processed in memory (the .osu file is never modified).
/// Scroll position is the integral of the scroll multiplier over time, so drawing a note is just
/// (note position - current position) * pixels-per-unit.
/// </summary>
public sealed class ManiaChart
{
    public const int LaneCount = 4;

    public Note[][] Notes { get; }
    public double BaseBpm { get; }
    public int SvKept { get; }
    public int SvRemoved { get; }

    private readonly double[] _segTime;
    private readonly double[] _segMult;
    private readonly double[] _segPos;

    private ManiaChart(Note[][] notes, double baseBpm, int kept, int removed,
        double[] segTime, double[] segMult, double[] segPos)
    {
        Notes = notes;
        BaseBpm = baseBpm;
        SvKept = kept;
        SvRemoved = removed;
        _segTime = segTime;
        _segMult = segMult;
        _segPos = segPos;
    }

    /// <summary>Scroll position at a given time (ms).</summary>
    public double Pos(double time)
    {
        int lo = 0, hi = _segTime.Length - 1;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) >> 1;
            if (_segTime[mid] <= time) lo = mid; else hi = mid - 1;
        }
        return _segPos[lo] + (time - _segTime[lo]) * _segMult[lo];
    }

    private readonly record struct TimingPoint(double Time, double BeatLength, bool Uninherited);
    private readonly record struct RawObject(int Lane, double Start, double End, bool Hold);

    /// <summary>
    /// Loads a chart. Returns null if the file is not a 4K osu!mania map.
    /// </summary>
    /// <param name="normalize">true = constant speed for the whole map.</param>
    /// <param name="bpmScalesScroll">Assume stable scales scroll speed with BPM relative to the main BPM.</param>
    /// <param name="tolerance">Fractional tolerance for treating a green line as BPM compensation.</param>
    public static ManiaChart? Load(string path, bool normalize, bool bpmScalesScroll, double tolerance)
    {
        var inv = CultureInfo.InvariantCulture;
        int mode = 0;
        double circleSize = 0;
        var timing = new List<TimingPoint>();
        var objects = new List<RawObject>();
        string section = "";

        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        using (var reader = new StreamReader(fs, Encoding.UTF8))
        {
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                line = line.Trim();
                if (line.Length == 0 || line.StartsWith("//")) continue;
                if (line[0] == '[' && line[^1] == ']')
                {
                    section = line[1..^1];
                    continue;
                }

                if (section == "General" || section == "Difficulty")
                {
                    int colon = line.IndexOf(':');
                    if (colon <= 0) continue;
                    string key = line[..colon].Trim();
                    string val = line[(colon + 1)..].Trim();
                    if (section == "General" && key == "Mode")
                        int.TryParse(val, NumberStyles.Integer, inv, out mode);
                    else if (section == "Difficulty" && key == "CircleSize")
                        double.TryParse(val, NumberStyles.Float, inv, out circleSize);
                }
                else if (section == "TimingPoints")
                {
                    var p = line.Split(',');
                    if (p.Length < 2) continue;
                    if (!double.TryParse(p[0], NumberStyles.Float, inv, out var time)) continue;
                    if (!double.TryParse(p[1], NumberStyles.Float, inv, out var beat)) continue;
                    bool uninherited = p.Length < 7 || p[6].Trim() == "1";
                    timing.Add(new TimingPoint(time, beat, uninherited));
                }
                else if (section == "HitObjects")
                {
                    var p = line.Split(',');
                    if (p.Length < 5) continue;
                    if (!int.TryParse(p[0], NumberStyles.Integer, inv, out var x)) continue;
                    if (!double.TryParse(p[2], NumberStyles.Float, inv, out var start)) continue;
                    if (!int.TryParse(p[3], NumberStyles.Integer, inv, out var type)) continue;

                    int lane = Math.Clamp(x * LaneCount / 512, 0, LaneCount - 1);
                    bool hold = (type & 128) != 0;
                    double end = start;
                    if (hold && p.Length > 5)
                    {
                        string endText = p[5].Split(':')[0];
                        if (double.TryParse(endText, NumberStyles.Float, inv, out var parsedEnd))
                            end = Math.Max(parsedEnd, start);
                    }
                    objects.Add(new RawObject(lane, start, end, hold));
                }
            }
        }

        if (mode != 3 || Math.Abs(circleSize - 4) > 0.01) return null;

        // Red lines before green lines at the same time, as osu! applies them.
        var points = timing
            .Select((tp, i) => (tp, i))
            .OrderBy(x => x.tp.Time)
            .ThenBy(x => x.tp.Uninherited ? 0 : 1)
            .ThenBy(x => x.i)
            .Select(x => x.tp)
            .ToList();

        var reds = points.Where(tp => tp.Uninherited && tp.BeatLength > 0).ToList();
        if (reds.Count == 0)
        {
            var fallback = new TimingPoint(0, 500, true);
            reds.Add(fallback);
            points.Insert(0, fallback);
        }

        double lastEnd = objects.Count > 0 ? objects.Max(o => o.End) : 0;

        // Base BPM = the BPM the map spends the most time at.
        var durationByBpm = new Dictionary<double, double>();
        for (int i = 0; i < reds.Count; i++)
        {
            double s = reds[i].Time;
            double e = i + 1 < reds.Count ? reds[i + 1].Time : Math.Max(lastEnd, s);
            double bpm = Math.Round(60000.0 / reds[i].BeatLength, 2);
            durationByBpm[bpm] = durationByBpm.GetValueOrDefault(bpm) + Math.Max(0, e - s);
        }
        double baseBpm = durationByBpm.OrderByDescending(kv => kv.Value).First().Key;

        // Net scroll multiplier for a given red-line beat length and green-line SV.
        (double Mult, bool Compensating) Evaluate(double beatLength, double sv)
        {
            if (normalize) return (1.0, false);
            double bpmFactor = bpmScalesScroll ? (60000.0 / beatLength) / baseBpm : 1.0;
            double needed = 1.0 / bpmFactor; // SV that would cancel the BPM-induced speed change
            bool compensating = Math.Abs(bpmFactor - 1.0) > 0.01
                                && Math.Abs(sv - needed) / needed <= tolerance;
            return (bpmFactor * (compensating ? sv : 1.0), compensating);
        }

        double curBeat = reds[0].BeatLength;
        int kept = 0, removed = 0;
        var times = new List<double> { -1e7 };
        var mults = new List<double> { Evaluate(curBeat, 1.0).Mult };

        foreach (var tp in points)
        {
            if (tp.Uninherited)
            {
                if (tp.BeatLength <= 0) continue;
                curBeat = tp.BeatLength;
                times.Add(tp.Time);
                mults.Add(Evaluate(curBeat, 1.0).Mult); // a red line resets SV to 1x
            }
            else
            {
                if (tp.BeatLength >= 0) continue;
                double sv = Math.Clamp(-100.0 / tp.BeatLength, 0.1, 10.0);
                var result = Evaluate(curBeat, sv);
                if (result.Compensating) kept++; else removed++;
                times.Add(tp.Time);
                mults.Add(result.Mult);
            }
        }

        var segTime = times.ToArray();
        var segMult = mults.ToArray();
        var segPos = new double[segTime.Length];
        for (int i = 1; i < segTime.Length; i++)
            segPos[i] = segPos[i - 1] + (segTime[i] - segTime[i - 1]) * segMult[i - 1];

        var notes = new Note[LaneCount][];
        var chart = new ManiaChart(notes, baseBpm, kept, removed, segTime, segMult, segPos);
        for (int lane = 0; lane < LaneCount; lane++)
        {
            notes[lane] = objects
                .Where(o => o.Lane == lane)
                .OrderBy(o => o.Start)
                .Select(o => new Note(o.Start, o.End, o.Hold, chart.Pos(o.Start), chart.Pos(o.End)))
                .ToArray();
        }
        return chart;
    }
}
