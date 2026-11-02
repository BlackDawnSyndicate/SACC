using Sacc.Core.Model;

namespace Sacc.Core;

/// <summary>
/// One spot where a ground-checked spawn's climb pinned down the ground. The ground lies between
/// <see cref="Low"/> and <see cref="High"/> (world units, cm); <see cref="Z"/> is the middle.
/// </summary>
public sealed class GroundSample
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Low { get; set; }
    public double High { get; set; }
    /// <summary>How many climbs measured this spot. Repeat measurements narrow the bounds.</summary>
    public int Hits { get; set; } = 1;
    /// <summary>The GM action that measured it, e.g. "npcs" or "zombies".</summary>
    public string Source { get; set; } = "";
    public DateTime At { get; set; }

    public double Z => (Low + High) / 2;
}

/// <summary>
/// Ground heights learned from GM spawn climbs, so the more you spawn the better the next height guess.
/// A climb goes up from below the ground; the game refuses each step until the ground is within about
/// <see cref="AcceptAbove"/> above the spawn Z. So when step Z is accepted right after Z − step was refused,
/// the ground is between Z − step + AcceptAbove and Z + AcceptAbove: one step wide, wherever it is.
/// </summary>
public sealed class GroundMap
{
    /// <summary>How far above the spawn Z the game still finds ground (measured for NPCs in spikes/npc-spawn-ground).</summary>
    public const double AcceptAbove = 300;
    /// <summary>Measurements closer than this (5 m) are the same spot and are merged.</summary>
    public const double MergeRadius = 500;

    public List<GroundSample> Samples { get; set; } = new();

    /// <summary>
    /// What a climb says about the ground at (x, y): accepted at <paramref name="acceptedZ"/> after
    /// <paramref name="refusedZ"/> was refused. Null when the two steps aren't neighbours (e.g. the climb's
    /// first step was accepted), because then the ground could be anywhere below.
    /// </summary>
    public static GroundSample? FromClimb(double x, double y, double refusedZ, double acceptedZ, double step, string source, DateTime at)
    {
        if (acceptedZ <= refusedZ || acceptedZ - refusedZ > step * 1.01) return null;
        return new GroundSample { X = x, Y = y, Low = refusedZ + AcceptAbove, High = acceptedZ + AcceptAbove, Source = source, At = at };
    }

    /// <summary>Adds a measurement, merging it into a sample at the same spot (bounds narrowed to where both agree).</summary>
    public GroundSample Add(GroundSample s)
    {
        var same = Samples.Where(o => Dist(o, s.X, s.Y) <= MergeRadius).OrderBy(o => Dist(o, s.X, s.Y)).FirstOrDefault();
        if (same == null) { Samples.Add(s); return s; }
        var low = Math.Max(same.Low, s.Low);
        var high = Math.Min(same.High, s.High);
        // Bounds that don't overlap mean the spot differs a little (slope, a rock); keep the newer one.
        if (low > high) { low = s.Low; high = s.High; }
        same.Low = low;
        same.High = high;
        same.Hits++;
        same.At = s.At;
        if (s.Source.Length > 0 && !same.Source.Contains(s.Source, StringComparison.Ordinal))
            same.Source = same.Source.Length == 0 ? s.Source : same.Source + "," + s.Source;
        return same;
    }

    /// <summary>The nearest sample within <paramref name="maxDistance"/>, or null.</summary>
    public GroundSample? Nearest(double x, double y, double maxDistance, out double distance)
    {
        GroundSample? best = null;
        distance = double.PositiveInfinity;
        foreach (var s in Samples)
        {
            var d = Dist(s, x, y);
            if (d <= maxDistance && d < distance) { best = s; distance = d; }
        }
        return best;
    }

    private static double Dist(GroundSample s, double x, double y) => Math.Sqrt((s.X - x) * (s.X - x) + (s.Y - y) * (s.Y - y));
}
