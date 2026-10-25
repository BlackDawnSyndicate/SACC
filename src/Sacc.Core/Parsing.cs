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

    private static double Num(string s) => double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
}
