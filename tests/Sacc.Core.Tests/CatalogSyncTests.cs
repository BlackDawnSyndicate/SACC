using Sacc.Core;

namespace Sacc.Core.Tests;

public class CatalogSyncTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sacc-catalogsync-" + Guid.NewGuid().ToString("N"));
    private string User => Path.Combine(_dir, "commands.json");
    private string Bundled => Path.Combine(_dir, "bundled.json");

    public CatalogSyncTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, recursive: true); } catch (IOException) { } }

    [Fact]
    public void NoUserCopy_DoesNothing()
    {
        File.WriteAllText(Bundled, "BUNDLED");
        var r = CatalogSync.Reconcile(User, Bundled);
        Assert.Equal(CatalogSyncAction.None, r.Action);
        Assert.False(File.Exists(User));
    }

    [Fact]
    public void IdenticalCopy_AdoptsAndRecordsSidecar_NoMessage()
    {
        File.WriteAllText(Bundled, "SAME");
        File.WriteAllText(User, "SAME");
        var r = CatalogSync.Reconcile(User, Bundled);
        Assert.Equal(CatalogSyncAction.None, r.Action);
        Assert.Null(r.Message);
        Assert.True(File.Exists(CatalogSync.SidecarPath(User)));
        Assert.Equal(CatalogSync.HashText("SAME"), File.ReadAllText(CatalogSync.SidecarPath(User)).Trim());
    }

    [Fact]
    public void UneditedOlderCopy_RefreshedSilentlyInPlace()
    {
        // User copy was made from an older bundled catalog (sidecar == user hash), bundled has since changed.
        File.WriteAllText(User, "OLD");
        File.WriteAllText(CatalogSync.SidecarPath(User), CatalogSync.HashText("OLD"));
        File.WriteAllText(Bundled, "NEW");

        var r = CatalogSync.Reconcile(User, Bundled);

        Assert.Equal(CatalogSyncAction.Refreshed, r.Action);
        Assert.Equal("NEW", File.ReadAllText(User));
        Assert.Equal(CatalogSync.HashText("NEW"), File.ReadAllText(CatalogSync.SidecarPath(User)).Trim());
        Assert.Null(r.BackupPath);
    }

    [Fact]
    public void StaleCopyNoProvenance_RefreshedWithBackup()
    {
        // The reported bug: a pre-feature copy that differs from bundled and has no sidecar.
        File.WriteAllText(User, "STALE #TeleportToPlayer");
        File.WriteAllText(Bundled, "FRESH #TeleportTo");

        var r = CatalogSync.Reconcile(User, Bundled, now: new DateTime(2026, 11, 15, 13, 36, 39));

        Assert.Equal(CatalogSyncAction.RefreshedWithBackup, r.Action);
        Assert.Equal("FRESH #TeleportTo", File.ReadAllText(User));
        Assert.NotNull(r.BackupPath);
        Assert.Equal("STALE #TeleportToPlayer", File.ReadAllText(r.BackupPath!));
        Assert.Equal(CatalogSync.HashText("FRESH #TeleportTo"), File.ReadAllText(CatalogSync.SidecarPath(User)).Trim());
    }

    [Fact]
    public void EditedCopy_IsKept()
    {
        // Provenance recorded, but the user then edited their copy (hash no longer matches the sidecar).
        File.WriteAllText(User, "MY EDITS");
        File.WriteAllText(CatalogSync.SidecarPath(User), CatalogSync.HashText("ORIGINAL COPY"));
        File.WriteAllText(Bundled, "NEWER BUNDLED");

        var r = CatalogSync.Reconcile(User, Bundled);

        Assert.Equal(CatalogSyncAction.KeptEdited, r.Action);
        Assert.Equal("MY EDITS", File.ReadAllText(User));
    }

    [Fact]
    public void Hash_IgnoresLineEndingDifferences()
    {
        Assert.Equal(CatalogSync.HashText("a\nb\nc"), CatalogSync.HashText("a\r\nb\r\nc"));
    }

    [Fact]
    public void RecordSource_WritesBundledHash()
    {
        File.WriteAllText(Bundled, "BUNDLED CONTENT");
        File.WriteAllText(User, "BUNDLED CONTENT");
        CatalogSync.RecordSource(User, Bundled);
        Assert.Equal(CatalogSync.HashText("BUNDLED CONTENT"), File.ReadAllText(CatalogSync.SidecarPath(User)).Trim());
    }
}
