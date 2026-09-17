using System.Diagnostics;

namespace Arctrix.PersonalMoneyTracker.Services;

/// <summary>
/// Receipt photos kept in the app's private data folder. Transactions store the path relative to
/// that folder ("receipts/…"), so it stays valid if the platform moves the app's data.
/// </summary>
public interface IReceiptPhotoStore
{
    /// <summary>Copies a captured photo into app storage and returns its stored path.</summary>
    Task<string> SaveAsync(FileResult photo);

    string GetFullPath(string storedPath);

    /// <summary>Deletes a stored photo. Does nothing for a missing path or a path outside the receipts folder.</summary>
    void Delete(string? storedPath);
}

public class ReceiptPhotoStore : IReceiptPhotoStore
{
    private const string Folder = "receipts";

    private static string Root => Path.Combine(FileSystem.AppDataDirectory, Folder);

    public async Task<string> SaveAsync(FileResult photo)
    {
        Directory.CreateDirectory(Root);

        var extension = Path.GetExtension(photo.FileName);
        if (string.IsNullOrEmpty(extension))
            extension = ".jpg";
        var fileName = $"receipt-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..8]}{extension.ToLowerInvariant()}";

        await using var source = await photo.OpenReadAsync();
        await using var target = File.Create(Path.Combine(Root, fileName));
        await source.CopyToAsync(target);

        return $"{Folder}/{fileName}";
    }

    public string GetFullPath(string storedPath) =>
        Path.Combine(FileSystem.AppDataDirectory, storedPath.Replace('/', Path.DirectorySeparatorChar));

    public void Delete(string? storedPath)
    {
        if (string.IsNullOrWhiteSpace(storedPath))
            return;

        var fullPath = Path.GetFullPath(GetFullPath(storedPath));
        if (!fullPath.StartsWith(Path.GetFullPath(Root) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            return;

        try
        {
            File.Delete(fullPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A leftover photo only costs storage; never fail the user's action over it.
            Debug.WriteLine($"Couldn't delete receipt photo {storedPath}: {ex.Message}");
        }
    }
}
