using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Sacc.Core.Model;

namespace Sacc.Core;

// Map layers come from scum-map.com (operator NOTIMEO), shown in SACC with their permission
// on condition of clear attribution and a link back. See CLEANROOM.md.

public sealed record MapTypeInfo(string Name, string TemplateUrl);

public sealed record MapSettings(
    int MinNativeZoom, int MaxNativeZoom, int TileSize,
    double BoundsNorth, double BoundsSouth, double BoundsWest, double BoundsEast,
    double WorldTop, double WorldBottom, double WorldLeft, double WorldRight);

public sealed record MapTemplate(string Id, string Name, MapSettings Settings, IReadOnlyList<MapTypeInfo> Types);

public sealed record MapCategory(int Id, string Name, string? Section, string Color, string Background, string? Icon);

public sealed record MapMarker(long Id, int CategoryId, string Title, double MapX, double MapY, double? Height);

/// <summary>
/// Converts between map units (scum-map.com's Leaflet space: x right, y down) and SCUM world
/// coordinates, using the template's own bounds. scum-map labels its world box with
/// "longitude" for X and "latitude" for Y; whatever sign the game uses, a point maps to the
/// same coordinates scum-map's own Copy TP produces.
/// </summary>
public sealed class MapProjection
{
    private readonly MapSettings _s;
    public MapProjection(MapSettings settings) => _s = settings;

    public double MapWidth => _s.BoundsEast - _s.BoundsWest;
    public double MapHeight => _s.BoundsSouth - _s.BoundsNorth;

    public Vec3 ToWorld(double mapX, double mapY, double z = 0) => new(
        _s.WorldLeft + (mapX - _s.BoundsWest) / MapWidth * (_s.WorldRight - _s.WorldLeft),
        _s.WorldTop + (mapY - _s.BoundsNorth) / MapHeight * (_s.WorldBottom - _s.WorldTop),
        z);

    public (double MapX, double MapY) ToMap(double worldX, double worldY) => (
        _s.BoundsWest + (worldX - _s.WorldLeft) / (_s.WorldRight - _s.WorldLeft) * MapWidth,
        _s.BoundsNorth + (worldY - _s.WorldTop) / (_s.WorldBottom - _s.WorldTop) * MapHeight);

    public bool Contains(double mapX, double mapY) =>
        mapX >= _s.BoundsWest && mapX <= _s.BoundsEast && mapY >= _s.BoundsNorth && mapY <= _s.BoundsSouth;

    /// <summary>The #Teleport line scum-map's own tool would produce: coordinates first, optional target last.</summary>
    public static string TeleportLine(Vec3 v, string? player) =>
        FormattableString.Invariant($"#Teleport {v.X:0.####} {v.Y:0.####} {v.Z:0.####}")
        + (string.IsNullOrWhiteSpace(player) ? "" : " " + player.Trim());
}

/// <summary>Minimal client for the three public queries scum-map.com's own map page uses.</summary>
public sealed class ScumMapClient
{
    public const string Endpoint = "https://scum-map.com/en/gql/";
    public const string SiteUrl = "https://scum-map.com/en/interactive_map";
    public const string Attribution = "Map © scum-map.com";

    private readonly HttpClient _http;
    public ScumMapClient(HttpClient http) => _http = http;

    private const string TemplateQuery =
        "query GetHomePageMapTemplate($collectionSlug: MapCollectionSlugScalar!, $templateSlug: MapTemplateSlugScalar!) { mapCatalog { template { dataBySlug(collection: $collectionSlug, slug: $templateSlug) { id name settings { minNativeZoom maxNativeZoom boundsNorth boundsSouth boundsWest boundsEast tileSize geoBoundingBox { latitudeTop latitudeBottom longitudeLeft longitudeRight } } typeList { name templateUrl } } } } }";
    private const string CategoryQuery =
        "query GetHomePageCategoryList($mapTemplateId: MapTemplateIdAtMapScalar!) { mapCategory { listForMapTemplate(mapTemplateId: $mapTemplateId) { id name section { name } appearance { color colorBackground icon } } } }";
    private const string MarkerQuery =
        "query GetTemplateMarkerList($mapTemplateId: MapTemplateIdAtMapScalar!) { mapTemplate { layer { markerList(mapTemplateId: $mapTemplateId) { id categoryId title longitude latitude ingameHeight } } } }";

    public async Task<string> QueryRawAsync(string operation, string query, object variables, CancellationToken ct)
    {
        var body = JsonSerializer.Serialize(new { operationName = operation, variables, query });
        using var req = new HttpRequestMessage(HttpMethod.Post, Endpoint) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using var res = await _http.SendAsync(req, ct);
        res.EnsureSuccessStatusCode();
        var json = await res.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0)
            throw new InvalidDataException($"scum-map.com returned an error for {operation}: {errors[0].GetRawText()}");
        return json;
    }

    public Task<string> TemplateJsonAsync(string collection, string slug, CancellationToken ct) =>
        QueryRawAsync("GetHomePageMapTemplate", TemplateQuery, new { collectionSlug = collection, templateSlug = slug }, ct);

    public Task<string> CategoriesJsonAsync(string templateId, CancellationToken ct) =>
        QueryRawAsync("GetHomePageCategoryList", CategoryQuery, new { mapTemplateId = templateId }, ct);

    public Task<string> MarkersJsonAsync(string templateId, CancellationToken ct) =>
        QueryRawAsync("GetTemplateMarkerList", MarkerQuery, new { mapTemplateId = templateId }, ct);

    // ---------------------------------------------------------------- parsing (pure, testable)

    public static MapTemplate ParseTemplate(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var t = doc.RootElement.GetProperty("data").GetProperty("mapCatalog").GetProperty("template").GetProperty("dataBySlug");
        if (t.ValueKind != JsonValueKind.Object) throw new InvalidDataException("scum-map.com has no such map template.");
        var s = t.GetProperty("settings");
        var g = s.GetProperty("geoBoundingBox");
        var settings = new MapSettings(
            s.GetProperty("minNativeZoom").GetInt32(), s.GetProperty("maxNativeZoom").GetInt32(), s.GetProperty("tileSize").GetInt32(),
            Num(s, "boundsNorth"), Num(s, "boundsSouth"), Num(s, "boundsWest"), Num(s, "boundsEast"),
            Num(g, "latitudeTop"), Num(g, "latitudeBottom"), Num(g, "longitudeLeft"), Num(g, "longitudeRight"));
        var types = t.GetProperty("typeList").EnumerateArray()
            .Select(x => new MapTypeInfo(x.GetProperty("name").GetString() ?? "", x.GetProperty("templateUrl").GetString() ?? ""))
            .Where(x => x.TemplateUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            .ToList();
        return new MapTemplate(t.GetProperty("id").GetString() ?? "", t.GetProperty("name").GetString() ?? "", settings, types);
    }

    public static List<MapCategory> ParseCategories(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("data").GetProperty("mapCategory").GetProperty("listForMapTemplate").EnumerateArray()
            .Select(c =>
            {
                var a = c.GetProperty("appearance");
                var section = c.TryGetProperty("section", out var sec) && sec.ValueKind == JsonValueKind.Object ? sec.GetProperty("name").GetString() : null;
                return new MapCategory(c.GetProperty("id").GetInt32(), c.GetProperty("name").GetString() ?? "", section,
                    Str(a, "color") ?? "#ffffff", Str(a, "colorBackground") ?? "#444444", Str(a, "icon"));
            })
            .OrderBy(c => c.Section ?? "~", StringComparer.OrdinalIgnoreCase).ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static List<MapMarker> ParseMarkers(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("data").GetProperty("mapTemplate").GetProperty("layer").GetProperty("markerList").EnumerateArray()
            .Where(m => m.TryGetProperty("longitude", out var x) && x.ValueKind == JsonValueKind.Number
                     && m.TryGetProperty("latitude", out var y) && y.ValueKind == JsonValueKind.Number)
            .Select(m => new MapMarker(
                m.GetProperty("id").GetInt64(),
                m.TryGetProperty("categoryId", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetInt32() : 0,
                Str(m, "title") ?? "",
                Num(m, "longitude"), Num(m, "latitude"),
                m.TryGetProperty("ingameHeight", out var h) && h.ValueKind == JsonValueKind.Number ? h.GetDouble() : null))
            .ToList();
    }

    /// <summary>Tile URL for a map type template ("…/{z}/{x}_{y}.webp").</summary>
    public static string TileUrl(string template, int z, int x, int y) =>
        template.Replace("{z}", z.ToString(CultureInfo.InvariantCulture))
                .Replace("{x}", x.ToString(CultureInfo.InvariantCulture))
                .Replace("{y}", y.ToString(CultureInfo.InvariantCulture));

    private static double Num(JsonElement e, string name) => e.GetProperty(name).GetDouble();
    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
