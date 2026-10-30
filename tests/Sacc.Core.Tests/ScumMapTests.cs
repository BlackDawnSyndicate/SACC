using System.Net;
using Sacc.Core;
using Sacc.Core.Model;

namespace Sacc.Core.Tests;

public class ScumMapTests
{
    // Shapes recorded from scum-map.com's public map page (2026-10-30), trimmed.
    private const string TemplateJson = """
        {"data":{"mapCatalog":{"template":{"dataBySlug":{"id":"01JTAKS9ACWQCZJTZ1R1N573KD","name":"Island",
        "settings":{"minNativeZoom":2,"maxNativeZoom":6,"boundsNorth":0,"boundsSouth":320,"boundsWest":0,"boundsEast":320,"tileSize":1280,
        "geoBoundingBox":{"latitudeTop":619199.938,"latitudeBottom":-904800,"longitudeLeft":619200,"longitudeRight":-904800}},
        "typeList":[{"name":"Regular","templateUrl":"https:\/\/cdn.scum-map.com\/tiles\/scum\/island\/v72\/regular\/{z}\/{x}_{y}.webp"},
                    {"name":"Bad","templateUrl":"javascript:alert(1)"}]}}}}}
        """;

    private const string CategoriesJson = """
        {"data":{"mapCategory":{"listForMapTemplate":[
          {"id":456,"name":"Abandoned bunkers","section":{"name":"Bunkers"},"appearance":{"color":"#ffffff","colorBackground":"#351e56","icon":"faSkull"}},
          {"id":869,"name":"Animal feeders","section":null,"appearance":{"color":"#ffffff","colorBackground":"#a16207","icon":"faWheatAwn"}}]}}}
        """;

    private const string MarkersJson = """
        {"data":{"mapTemplate":{"layer":{"markerList":[
          {"id":235319,"categoryId":78,"title":"Aircraft spawns","longitude":27.52123,"latitude":5.55738,"ingameHeight":8007.635},
          {"id":1,"categoryId":null,"title":"No category","longitude":1,"latitude":2,"ingameHeight":null},
          {"id":2,"categoryId":5,"title":"Broken","longitude":null,"latitude":2}]}}}}
        """;

    private static MapProjection Projection() => new(ScumMapClient.ParseTemplate(TemplateJson).Settings);

    [Fact]
    public void ParsesTemplateAndDropsNonHttpsTypes()
    {
        var t = ScumMapClient.ParseTemplate(TemplateJson);
        Assert.Equal("01JTAKS9ACWQCZJTZ1R1N573KD", t.Id);
        Assert.Equal((2, 6, 1280), (t.Settings.MinNativeZoom, t.Settings.MaxNativeZoom, t.Settings.TileSize));
        Assert.Equal(new[] { "Regular" }, t.Types.Select(x => x.Name));
    }

    [Fact]
    public void ProjectionMatchesScumMapsOwnCopyTp()
    {
        // scum-map.com's Copy TP at map (x 240, y 80) produced "#Teleport -523800 238199.9531 0".
        var v = Projection().ToWorld(240, 80);
        Assert.Equal(-523800, v.X, 3);
        Assert.Equal(238199.95, v.Y, 1);
        // Centre of the map is the centre of the world box.
        var c = Projection().ToWorld(160, 160);
        Assert.Equal(-142800, c.X, 3);
        Assert.Equal(-142800, c.Y, 0);
    }

    [Fact]
    public void ProjectionRoundTrips()
    {
        var p = Projection();
        var (mx, my) = p.ToMap(-242680.219, -105232.688);
        var back = p.ToWorld(mx, my);
        Assert.Equal(-242680.219, back.X, 3);
        Assert.Equal(-105232.688, back.Y, 3);
        Assert.True(p.Contains(mx, my));
        Assert.False(p.Contains(-1, 10));
    }

    [Fact]
    public void TeleportLineIsCoordinatesFirst()
    {
        Assert.Equal("#Teleport -523800 238199.9531 0", MapProjection.TeleportLine(new Vec3(-523800, 238199.95312, 0), null));
        Assert.Equal("#Teleport 1.5 -2 30 76561198000000001", MapProjection.TeleportLine(new Vec3(1.5, -2, 30), " 76561198000000001 "));
    }

    [Fact]
    public void ParsesCategoriesAndMarkers()
    {
        var cats = ScumMapClient.ParseCategories(CategoriesJson);
        Assert.Equal(new[] { "Bunkers", null }, cats.Select(c => c.Section)); // sections first, unsectioned last
        Assert.Equal("#351e56", cats[0].Background);

        var markers = ScumMapClient.ParseMarkers(MarkersJson);
        Assert.Equal(2, markers.Count); // the one without coordinates is skipped
        Assert.Equal((27.52123, 5.55738, 8007.635), (markers[0].MapX, markers[0].MapY, markers[0].Height!.Value));
        Assert.Equal(0, markers[1].CategoryId);
        Assert.Null(markers[1].Height);
    }

    [Fact]
    public void TileUrlFillsPlaceholders() =>
        Assert.Equal("https://x/6/15_3.webp", ScumMapClient.TileUrl("https://x/{z}/{x}_{y}.webp", 6, 15, 3));

    private sealed class FakeHandler(HttpStatusCode code, string body) : HttpMessageHandler
    {
        public string? LastBody;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastBody = await request.Content!.ReadAsStringAsync(ct);
            return new HttpResponseMessage(code) { Content = new StringContent(body) };
        }
    }

    [Fact]
    public async Task ClientPostsOperationAndSurfacesGraphQlErrors()
    {
        var ok = new FakeHandler(HttpStatusCode.OK, TemplateJson);
        var json = await new ScumMapClient(new HttpClient(ok)).TemplateJsonAsync("scum", "island", default);
        Assert.Contains("\"operationName\":\"GetHomePageMapTemplate\"", ok.LastBody);
        Assert.Contains("\"templateSlug\":\"island\"", ok.LastBody);
        Assert.Equal("Island", ScumMapClient.ParseTemplate(json).Name);

        var bad = new FakeHandler(HttpStatusCode.OK, """{"errors":[{"message":"nope"}]}""");
        await Assert.ThrowsAsync<InvalidDataException>(() => new ScumMapClient(new HttpClient(bad)).MarkersJsonAsync("x", default));
    }
}
