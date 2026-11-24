using System.Security.Cryptography;
using System.Text;

namespace Sacc.Core;

public enum CatalogSyncAction
{
    /// <summary>No user copy, or it already matches the bundled catalog (nothing to do).</summary>
    None,
    /// <summary>The user copy equalled the bundled one; recorded its provenance so future updates are tracked.</summary>
    Adopted,
    /// <summary>The user copy was an unedited copy of an older bundled catalog; refreshed it in place.</summary>
    Refreshed,
    /// <summary>The user copy pre-dated provenance tracking and differed; refreshed it and backed the old one up.</summary>
    RefreshedWithBackup,
    /// <summary>The user deliberately edited their copy; kept it and left the bundled update for them to pull in.</summary>
    KeptEdited,
}

public sealed record CatalogSyncResult(CatalogSyncAction Action, string? BackupPath = null, string? Message = null);

/// <summary>
/// Keeps the user's editable catalog copy (<c>commands.json</c> in the data folder) from silently shadowing a newer
/// bundled catalog. The bundled catalog is "versioned" by the hash of its contents; a sidecar next to the user copy
/// records the bundled hash the copy was made from. On load we reconcile:
/// an unedited copy of an older bundled catalog is refreshed automatically; a copy the user actually edited is kept.
/// This is why a stale copy (e.g. one still using #TeleportToPlayer) can't freeze a user on an old command set.
/// </summary>
public static class CatalogSync
{
    public static string SidecarPath(string userCatalogPath) => userCatalogPath + ".source";

    /// <summary>SHA-256 (hex) of a catalog file's bytes, normalised for line endings so CRLF/LF don't look like edits.</summary>
    public static string HashFile(string path) => HashText(File.ReadAllText(path));

    public static string HashText(string text)
    {
        var normalised = text.Replace("\r\n", "\n").Replace("\r", "\n");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalised)));
    }

    /// <summary>
    /// Reconcile the user catalog copy against the bundled one. Does nothing when there is no user copy (the app then
    /// uses the bundled catalog directly, always current). <paramref name="now"/> is injectable for deterministic tests.
    /// </summary>
    public static CatalogSyncResult Reconcile(string userCatalogPath, string bundledCatalogPath, DateTime? now = null)
    {
        if (!File.Exists(userCatalogPath) || !File.Exists(bundledCatalogPath))
            return new CatalogSyncResult(CatalogSyncAction.None);

        var bundledHash = HashFile(bundledCatalogPath);
        var userHash = HashFile(userCatalogPath);
        var sidecar = SidecarPath(userCatalogPath);
        var stored = File.Exists(sidecar) ? File.ReadAllText(sidecar).Trim() : null;

        // Already identical: just record provenance so a later bundled change is recognised as an update.
        if (userHash == bundledHash)
        {
            if (stored != bundledHash) File.WriteAllText(sidecar, bundledHash);
            return new CatalogSyncResult(CatalogSyncAction.None);
        }

        // Unedited copy of an older bundled catalog (its hash still matches what it was copied from): refresh silently.
        if (stored == userHash)
        {
            File.Copy(bundledCatalogPath, userCatalogPath, overwrite: true);
            File.WriteAllText(sidecar, bundledHash);
            return new CatalogSyncResult(CatalogSyncAction.Refreshed,
                Message: "Updated your command catalog to the bundled version.");
        }

        // No provenance recorded (copy pre-dates this feature) and it differs from the bundled catalog: the common case
        // is an accidental stale copy. Refresh it, but back up the old copy first so any real edits are recoverable.
        if (stored == null)
        {
            var stamp = (now ?? DateTime.Now).ToString("yyyyMMdd-HHmmss");
            var backup = userCatalogPath + $".user-{stamp}.bak";
            File.Copy(userCatalogPath, backup, overwrite: true);
            File.Copy(bundledCatalogPath, userCatalogPath, overwrite: true);
            File.WriteAllText(sidecar, bundledHash);
            return new CatalogSyncResult(CatalogSyncAction.RefreshedWithBackup, backup,
                $"Refreshed your command catalog to the bundled version; your old copy was saved to {Path.GetFileName(backup)}.");
        }

        // Provenance recorded and the copy no longer matches it: the user edited their catalog. Keep it.
        return new CatalogSyncResult(CatalogSyncAction.KeptEdited,
            Message: "Kept your edited command catalog; the bundled catalog has newer commands (delete your copy to take them).");
    }

    /// <summary>Record that the user copy was just made from the current bundled catalog (call after EditCatalog copies it).</summary>
    public static void RecordSource(string userCatalogPath, string bundledCatalogPath)
    {
        if (File.Exists(bundledCatalogPath))
            File.WriteAllText(SidecarPath(userCatalogPath), HashFile(bundledCatalogPath));
    }
}
