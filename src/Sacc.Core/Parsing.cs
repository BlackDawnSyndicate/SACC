using System.Globalization;
using System.Text.RegularExpressions;
using Sacc.Core.Model;

namespace Sacc.Core;

/// <summary>
/// Tolerant parsers for text the game puts in chat or on the clipboard.
/// The exact output format is not publicly documented, so these key off
/// stable anchors (SteamID64s, "Key: value" pairs, X/Y/Z triples) rather
/// than a fixed layout.
/// </summary>
public static partial class Parsing
{
    [GeneratedRegex(@"\b(7656119\d{10})\b")]
    private static partial Regex SteamId();

    [GeneratedRegex(@"^\s*\d+\s*[.)]\s+")]
    private static partial Regex IndexPrefix();

    [GeneratedRegex(@"X\s*[:=]\s*(-?\d+(?:\.\d+)?)\D{0,6}?Y\s*[:=]\s*(-?\d+(?:\.\d+)?)\D{0,6}?Z\s*[:=]\s*(-?\d+(?:\.\d+)?)", RegexOptions.IgnoreCase)]
    private static partial Regex Xyz();

    [GeneratedRegex(@"-?\d+(?:\.\d+)?")]
    private static partial Regex AnyNumber();

    [GeneratedRegex(@"\bFame(?:\s*points)?\s*[:=]\s*(-?\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex Fame();

    [GeneratedRegex(@"\b(?:Account(?:\s*balance)?|Balance|Money|Cash)\s*[:=]\s*(-?\d+(?:\.\d+)?)", RegexOptions.IgnoreCase)]
    private static partial Regex Account();

    [GeneratedRegex(@"\bGold\s*[:=]\s*(-?\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex Gold();

    [GeneratedRegex(@"\b(?:Name|Character|Nickname)\s*[:=]\s*([^,|\t\r\n]+)", RegexOptions.IgnoreCase)]
    private static partial Regex ExplicitName();

    [GeneratedRegex(@"\b(?:Fame(?:\s*points)?|Account(?:\s*balance)?|Balance|Money|Cash|Gold|Steam(?:\s*ID(?:64)?)?|Location|ID)\s*[:=]\s*\S*", RegexOptions.IgnoreCase)]
    private static partial Regex KeyValue();

    public static bool TryParseLocation(string? text, out Vec3 location)
    {
        location = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var m = Xyz().Match(text);
        if (m.Success)
        {
            location = new Vec3(Num(m.Groups[1].Value), Num(m.Groups[2].Value), Num(m.Groups[3].Value));
            return true;
        }

        // Fallback: three numbers separated by spaces, commas or pipes (e.g. "-1.5|20|3").
        var nums = AnyNumber().Matches(SteamId().Replace(text, " ")).Select(n => n.Value).ToList();
        if (nums.Count != 3) return false;
        location = new Vec3(Num(nums[0]), Num(nums[1]), Num(nums[2]));
        return true;
    }

    public static List<PlayerRecord> ParsePlayerList(string? text, DateTime? seenAt = null)
    {
        var result = new List<PlayerRecord>();
        if (string.IsNullOrWhiteSpace(text)) return result;
        var now = seenAt ?? DateTime.Now;

        var blocks = new List<List<string>>();
        List<string>? current = null;
        bool currentHasId = false;

        foreach (var line in text.Split('\n').Select(l => l.TrimEnd('\r')))
        {
            if (line.Trim().Length == 0) continue;
            var startsIndexed = IndexPrefix().IsMatch(line);
            var hasId = SteamId().IsMatch(line);
            if (current == null || startsIndexed || (hasId && currentHasId))
            {
                current = new List<string>();
                blocks.Add(current);
                currentHasId = false;
            }
            current.Add(line);
            currentHasId |= hasId;
        }

        foreach (var block in blocks)
        {
            var joined = string.Join("\n", block);
            var id = SteamId().Match(joined);
            if (!id.Success) continue;

            var rec = new PlayerRecord { SteamId = id.Groups[1].Value, LastSeen = now };
            rec.Nickname = GuessName(block, rec.SteamId);
            if (Fame().Match(joined) is { Success: true } f) rec.Fame = long.Parse(f.Groups[1].Value, CultureInfo.InvariantCulture);
            if (Account().Match(joined) is { Success: true } a) rec.Account = Num(a.Groups[1].Value);
            if (Gold().Match(joined) is { Success: true } g) rec.Gold = long.Parse(g.Groups[1].Value, CultureInfo.InvariantCulture);
            if (Xyz().Match(joined) is { Success: true } xyz)
                rec.Location = new Vec3(Num(xyz.Groups[1].Value), Num(xyz.Groups[2].Value), Num(xyz.Groups[3].Value));

            // Duplicate IDs (e.g. pasted twice): last one wins.
            result.RemoveAll(r => r.SteamId == rec.SteamId);
            result.Add(rec);
        }
        return result;
    }

    private static string GuessName(List<string> block, string steamId)
    {
        var joined = string.Join("\n", block);
        var explicitName = ExplicitName().Match(joined);
        if (explicitName.Success) return explicitName.Groups[1].Value.Trim();

        var first = IndexPrefix().Replace(block[0], "");
        first = first.Replace(steamId, " ");
        first = Xyz().Replace(first, " ");
        first = KeyValue().Replace(first, " ");
        first = first.Trim(' ', '\t', ',', '|', ':', '-', '(', ')', '[', ']');
        return Regex.Replace(first, @"\s{2,}", " ");
    }

    [GeneratedRegex(@"(?<!Steam\s*)\b(?:Flag\s*)?ID\s*[:=#]?\s*(\d{1,9})\b", RegexOptions.IgnoreCase)]
    private static partial Regex FlagId();

    [GeneratedRegex(@"\bOwner(?:\s*name)?\s*[:=]\s*([^,|\t\r\n(]+)", RegexOptions.IgnoreCase)]
    private static partial Regex OwnerName();

    /// <summary>
    /// #ListFlags output. Its layout is undocumented, so a record is every line up to and
    /// including the next X/Y/Z triple; the flag id, owner SteamID64 and owner name are
    /// picked out of those lines wherever they appear.
    /// </summary>
    public static List<FlagRecord> ParseFlagList(string? text) =>
        LocatedBlocks(text).Select(b =>
        {
            var flag = new FlagRecord
            {
                Id = FlagId().Match(b.Text) is { Success: true } id ? id.Groups[1].Value : "",
                OwnerSteamId = SteamId().Match(b.Text) is { Success: true } s ? s.Groups[1].Value : "",
                Location = b.Location,
            };
            var owner = OwnerName().Match(b.Text);
            if (!owner.Success) owner = ExplicitName().Match(b.Text);
            if (owner.Success) flag.OwnerName = owner.Groups[1].Value.Trim();
            return flag;
        }).ToList();

    /// <summary>
    /// Adds flags not already in <paramref name="target"/> (same id, or same position when there is
    /// no id) and returns how many were new. #ListFlags is paged, so pages are merged one by one.
    /// </summary>
    public static int MergeFlags(IList<FlagRecord> target, IEnumerable<FlagRecord> incoming)
    {
        static string Key(FlagRecord f) => f.Id.Length > 0 ? "id:" + f.Id : "at:" + f.Location;
        var seen = target.Select(Key).ToHashSet();
        var added = 0;
        foreach (var f in incoming)
            if (seen.Add(Key(f))) { target.Add(f); added++; }
        return added;
    }

    [GeneratedRegex(@"\bBPC?_\w+")]
    private static partial Regex Blueprint();

    /// <summary>#ListSpawnedVehicles output, read the same tolerant way as flags: id, blueprint and X/Y/Z.</summary>
    public static List<VehicleRecord> ParseVehicleList(string? text) =>
        LocatedBlocks(text).Select(b => new VehicleRecord
        {
            Id = FlagId().Match(b.Text) is { Success: true } id ? id.Groups[1].Value : "",
            Code = Blueprint().Match(b.Text) is { Success: true } c ? c.Value : "",
            Location = b.Location,
        }).ToList();

    /// <summary>Records of undocumented layout: every line up to and including the next X/Y/Z triple.</summary>
    private static IEnumerable<(string Text, Vec3 Location)> LocatedBlocks(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) yield break;
        var block = new List<string>();
        foreach (var line in text.Split('\n').Select(l => l.TrimEnd('\r')))
        {
            if (line.Trim().Length == 0) continue;
            block.Add(line);
            var joined = string.Join("\n", block);
            if (Xyz().Match(joined) is not { Success: true } xyz) continue;
            block.Clear();
            yield return (joined, new Vec3(Num(xyz.Groups[1].Value), Num(xyz.Groups[2].Value), Num(xyz.Groups[3].Value)));
        }
    }

    private static double Num(string s) => double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
}
