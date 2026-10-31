using Sacc.Core;
using Sacc.Core.Model;

namespace Sacc.Core.Tests;

public class CommandLineTests
{
    [Theory]
    [InlineData("#ListPlayers", true)]
    [InlineData("  #SetTime 12  ", true)]
    [InlineData("", false)]
    [InlineData("hello everyone", false)]
    [InlineData("#Announce hi\n#Ban 76561190000000000", false)]
    public void Check(string input, bool ok) => Assert.Equal(ok, CommandLine.Check(input).Ok);

    [Fact]
    public void Trims() => Assert.Equal("#SetTime 12", CommandLine.Check("  #SetTime 12 ").Text);

    [Fact]
    public void WarnsOnLongLine() => Assert.NotNull(CommandLine.Check("#Announce " + new string('a', 300)).Warning);
}

public class TemplateRendererTests
{
    private static CommandDef Spawn() => new()
    {
        Template = "#SpawnItem {item} {amount}",
        Params =
        {
            new ParamDef { Name = "item", Kind = ParamKind.Text },
            new ParamDef { Name = "amount", Kind = ParamKind.Integer, Optional = true },
        },
    };

    private static Dictionary<string, string?> V(params (string k, string? v)[] kv) =>
        kv.ToDictionary(p => p.k, p => p.v);

    [Fact]
    public void RendersAllParams() =>
        Assert.Equal("#SpawnItem Banana 3", TemplateRenderer.Render(Spawn(), V(("item", "Banana"), ("amount", "3"))).Text);

    [Fact]
    public void DropsEmptyOptional()
    {
        var r = TemplateRenderer.Render(Spawn(), V(("item", "Banana")));
        Assert.True(r.Ok);
        Assert.Equal("#SpawnItem Banana", r.Text);
    }

    [Fact]
    public void RequiredMissing() => Assert.False(TemplateRenderer.Render(Spawn(), V(("amount", "3"))).Ok);

    [Fact]
    public void RejectsBadInteger() => Assert.False(TemplateRenderer.Render(Spawn(), V(("item", "A"), ("amount", "lots"))).Ok);

    [Fact]
    public void RejectsSpacesInText() => Assert.False(TemplateRenderer.Render(Spawn(), V(("item", "Two Words"))).Ok);

    [Fact]
    public void RejectsNewlineInMessage()
    {
        var def = new CommandDef { Template = "#Announce {m}", Params = { new ParamDef { Name = "m", Kind = ParamKind.Message } } };
        Assert.False(TemplateRenderer.Render(def, V(("m", "a\n#Ban x"))).Ok);
        Assert.Equal("#Announce hello there", TemplateRenderer.Render(def, V(("m", "hello there"))).Text);
    }

    [Fact]
    public void NormalizesNumbersAndChoices()
    {
        var def = new CommandDef
        {
            Template = "#X {c} {n}",
            Params =
            {
                new ParamDef { Name = "c", Kind = ParamKind.Choice, Choices = { "Normal", "Gold" } },
                new ParamDef { Name = "n", Kind = ParamKind.Number },
            },
        };
        Assert.Equal("#X Gold -1.5", TemplateRenderer.Render(def, V(("c", "gold"), ("n", "-1,5"))).Text);
    }

    [Fact]
    public void OptionalSegmentOnlyWhenFilled()
    {
        var def = Spawn();
        def.Template = "#SpawnItem {item} {amount}[ StackCount {stack}]";
        def.Params.Add(new ParamDef { Name = "stack", Kind = ParamKind.Integer, Optional = true });
        Assert.Equal("#SpawnItem Mag 1 StackCount 200", TemplateRenderer.Render(def, V(("item", "Mag"), ("amount", "1"), ("stack", "200"))).Text);
        Assert.Equal("#SpawnItem Mag 1", TemplateRenderer.Render(def, V(("item", "Mag"), ("amount", "1"), ("stack", ""))).Text);
        Assert.False(TemplateRenderer.Render(def, V(("item", "Mag"), ("stack", "lots"))).Ok);
    }

    [Fact]
    public void UsesDefault()
    {
        var def = Spawn();
        def.Params[1].Default = "1";
        Assert.Equal("#SpawnItem A 1", TemplateRenderer.Render(def, V(("item", "A"))).Text);
    }
}

public class ParsingTests
{
    [Theory]
    [InlineData("X=-242680.219 Y=-105232.688 Z=36476.027", -242680.219, -105232.688, 36476.027)]
    [InlineData("Location: X: 10, Y: -20.5, Z: 3", 10, -20.5, 3)]
    [InlineData("-1.5|20|3", -1.5, 20, 3)]
    [InlineData("100 200 300", 100, 200, 300)]
    public void Location(string text, double x, double y, double z)
    {
        Assert.True(Parsing.TryParseLocation(text, out var v));
        Assert.Equal(new Vec3(x, y, z), v);
    }

    [Fact]
    public void LocationRejectsJunk() => Assert.False(Parsing.TryParseLocation("no coordinates here", out _));

    [Fact]
    public void PlayerList_MultiLineBlocks()
    {
        const string text = """
            1. Alice
            Steam: Alice_S (76561198000000001)
            Fame: 612
            Account balance: 28673
            Gold: 5
            Location: X=-242680.219 Y=-105232.688 Z=36476.027

            2. Bob Smith
            Steam: bobby (76561198000000002)
            Fame: 10
            """;
        var players = Parsing.ParsePlayerList(text);
        Assert.Equal(2, players.Count);
        Assert.Equal("Alice", players[0].Nickname);
        Assert.Equal(612, players[0].Fame);
        Assert.Equal(28673, players[0].Account);
        Assert.Equal(5, players[0].Gold);
        Assert.Equal(-242680.219, players[0].Location!.Value.X);
        Assert.Equal("Bob Smith", players[1].Nickname);
        Assert.Null(players[1].Location);
    }

    [Fact]
    public void PlayerList_OneLinePerPlayer()
    {
        const string text = "Alice 76561198000000001 Fame: 3\nBob 76561198000000002 Fame: 4\r\n";
        var players = Parsing.ParsePlayerList(text);
        Assert.Equal(new[] { "Alice", "Bob" }, players.Select(p => p.Nickname));
        Assert.Equal(new long?[] { 3, 4 }, players.Select(p => p.Fame));
    }

    [Fact]
    public void PlayerList_ExplicitNameField()
    {
        var players = Parsing.ParsePlayerList("SteamID: 76561198000000009, Name: Zed the Dead, Fame: 1");
        Assert.Single(players);
        Assert.Equal("Zed the Dead", players[0].Nickname);
    }

    [Fact]
    public void PlayerList_IgnoresTextWithoutIds() => Assert.Empty(Parsing.ParsePlayerList("nobody online"));

    [Fact]
    public void FlagList_OneLinePerFlag()
    {
        var flags = Parsing.ParseFlagList(
            "Flag ID: 12 | Owner: Zed (76561198000000009) | X=-1000.5 Y=2000 Z=30\n" +
            "Flag ID: 13 | Owner: Kane (76561198000000010) | X=5 Y=6 Z=7");
        Assert.Equal(2, flags.Count);
        Assert.Equal("12", flags[0].Id);
        Assert.Equal("Zed", flags[0].OwnerName);
        Assert.Equal("76561198000000009", flags[0].OwnerSteamId);
        Assert.Equal(new Vec3(-1000.5, 2000, 30), flags[0].Location);
        Assert.Equal("13", flags[1].Id);
    }

    [Fact]
    public void FlagList_MultiLineRecords_SteamIdIsNotTheFlagId()
    {
        var flags = Parsing.ParseFlagList(
            "SteamID: 76561198000000009\nID: 7\nLocation: X=1 Y=2 Z=3\n\n" +
            "SteamID: 76561198000000010\nLocation: X=4 Y=5 Z=6\n");
        Assert.Equal(2, flags.Count);
        Assert.Equal("7", flags[0].Id);
        Assert.Equal("", flags[1].Id);
        Assert.Equal("76561198000000010", flags[1].OwnerSteamId);
        Assert.Equal("Flag · 76561198000000010", flags[1].Label);
    }

    [Fact]
    public void FlagList_IgnoresTextWithoutCoordinates() => Assert.Empty(Parsing.ParseFlagList("Flag ID: 1 Owner: Zed"));
}

public class MacroScriptTests
{
    [Fact]
    public void ParsesStepsWaitsAndComments()
    {
        var r = MacroScript.Parse("// storm\n#SetTime 22\nwait 500\n\n#Announce Storm for {player}",
            new Dictionary<string, string> { ["player"] = "Bob" });
        Assert.True(r.Ok, string.Join("; ", r.Errors));
        Assert.Equal(3, r.Steps.Count);
        Assert.Equal(500, ((MacroScript.WaitStep)r.Steps[1]).Milliseconds);
        Assert.Equal("#Announce Storm for Bob", ((MacroScript.SendStep)r.Steps[2]).Text);
    }

    [Fact]
    public void MissingVariableIsError() => Assert.False(MacroScript.Parse("#Kick {player}").Ok);

    [Fact]
    public void PlainChatIsError() => Assert.False(MacroScript.Parse("hello").Ok);

    [Fact]
    public void WaitTooLong() => Assert.False(MacroScript.Parse("#A\nwait 999999").Ok);

    [Fact]
    public void ListsVariables() =>
        Assert.Equal(new[] { "player", "amount" }, MacroScript.Variables("#A {player} {amount} {Player}"));
}

public class CatalogTests
{
    private static string CatalogPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "SACC.sln"))) dir = dir.Parent;
        return Path.Combine(dir!.FullName, "data", "commands.json");
    }

    [Fact]
    public void ShippedCatalogIsConsistent()
    {
        var catalog = JsonStore.LoadCatalog(CatalogPath());
        Assert.NotEmpty(catalog.Commands);
        foreach (var c in catalog.Commands)
        {
            Assert.False(string.IsNullOrWhiteSpace(c.Id));
            Assert.StartsWith("#", c.Template);
            // Every placeholder has a param and every param is used.
            var tokens = System.Text.RegularExpressions.Regex.Matches(c.Template, @"\{(\w+)\}").Select(m => m.Groups[1].Value).ToList();
            Assert.Equal(tokens.OrderBy(t => t), c.Params.Select(p => p.Name).OrderBy(n => n));
        }
    }

    [Fact]
    public void ProfileRoundTrips()
    {
        var path = Path.Combine(Path.GetTempPath(), $"sacc-test-{Guid.NewGuid():N}.json");
        try
        {
            var p = new Profile();
            p.Players.Add(new PlayerRecord { SteamId = "76561198000000001", Location = new Vec3(1, 2, 3) });
            p.Settings.InputMode = InputMode.Type;
            JsonStore.Save(path, p);
            var back = JsonStore.LoadOrDefault<Profile>(path);
            Assert.Equal(new Vec3(1, 2, 3), back.Players[0].Location);
            Assert.Equal(InputMode.Type, back.Settings.InputMode);
        }
        finally { File.Delete(path); }
    }
}
