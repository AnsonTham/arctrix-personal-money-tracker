using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Arctrix.PersonalMoneyTracker.Services.Telegram;

/// <summary>One file waiting in the relay's pending folder.</summary>
public sealed record RelayFile(string Name, string Path, string Sha, long Size);

/// <summary>When the relay workflow last ran, and what it found.</summary>
public sealed record RelayRunInfo(DateTime RanAtUtc, int Collected, long LastUpdateId);

/// <summary>
/// Reads and empties the relay mailbox over the GitHub REST API, and asks it to check Telegram now.
/// The app never talks to Telegram's getUpdates - only the relay workflow does, so the two can't
/// take each other's messages.
/// </summary>
public sealed class RelayClient : IDisposable
{
    private const string ApiRoot = "https://api.github.com";
    private const string PendingFolder = "pending";
    private const string WorkflowFile = "relay.yml";

    private readonly HttpClient _http;
    private readonly TelegramSettings _settings;

    public RelayClient(TelegramSettings settings)
    {
        _settings = settings;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.GitHubToken);
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Arctrix-PersonalMoneyTracker", "1.0"));
        _http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
    }

    /// <summary>Everything waiting in pending/, oldest update first. The folder's .gitkeep is skipped.</summary>
    public async Task<IReadOnlyList<RelayFile>> ListPendingAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _http.GetAsync(
            $"{ApiRoot}/repos/{_settings.RelayRepository}/contents/{PendingFolder}?ref={_settings.RelayBranch}",
            cancellationToken);

        // An empty mailbox is a normal state, not a failure.
        if (response.StatusCode == HttpStatusCode.NotFound)
            return [];
        response.EnsureSuccessStatusCode();

        var entries = await response.Content.ReadFromJsonAsync<List<ContentEntry>>(cancellationToken) ?? [];
        return entries
            .Where(e => e is { Type: "file", Name: not null, Path: not null, Sha: not null } && !e.Name.StartsWith('.'))
            .Select(e => new RelayFile(e.Name!, e.Path!, e.Sha!, e.Size))
            .OrderBy(f => f.Name, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Downloads one pending file's bytes.</summary>
    public async Task<byte[]> DownloadAsync(RelayFile file, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{ApiRoot}/repos/{_settings.RelayRepository}/contents/{Uri.EscapeDataString(file.Path).Replace("%2F", "/")}?ref={_settings.RelayBranch}");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.raw"));

        using var response = await _http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    /// <summary>
    /// Removes a file from the mailbox once it has been dealt with. A file that someone else already
    /// deleted (409/422 on a stale sha) counts as done.
    /// </summary>
    public async Task<bool> DeleteAsync(RelayFile file, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Delete,
            $"{ApiRoot}/repos/{_settings.RelayRepository}/contents/{file.Path}")
        {
            Content = JsonContent.Create(new
            {
                message = $"App: processed {file.Name}",
                sha = file.Sha,
                branch = _settings.RelayBranch
            })
        };

        using var response = await _http.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
            return true;

        return response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity;
    }

    /// <summary>
    /// Asks the relay workflow to check Telegram now, so a reply arrives in about a minute instead of
    /// waiting for the next scheduled run.
    /// </summary>
    public async Task<bool> TriggerCollectionAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _http.PostAsJsonAsync(
            $"{ApiRoot}/repos/{_settings.RelayRepository}/actions/workflows/{WorkflowFile}/dispatches",
            new { @ref = _settings.RelayBranch },
            cancellationToken);
        return response.IsSuccessStatusCode;
    }

    /// <summary>
    /// When the relay last ran, from the state file it writes on every run. Null when it can't be read,
    /// which is treated the same as an unknown gap rather than as "up to date".
    /// </summary>
    public async Task<RelayRunInfo?> GetLastRunAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var bytes = await DownloadAsync(new RelayFile("last-run.json", "state/last-run.json", string.Empty, 0), cancellationToken);
            using var document = JsonDocument.Parse(bytes);
            var root = document.RootElement;
            if (!root.TryGetProperty("ranAt", out var ranAt) || ranAt.GetString() is not { } text)
                return null;

            return new RelayRunInfo(
                DateTime.Parse(text, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal),
                root.TryGetProperty("collected", out var collected) ? collected.GetInt32() : 0,
                root.TryGetProperty("lastUpdateId", out var last) ? last.GetInt64() : 0);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or FormatException)
        {
            return null;
        }
    }

    public void Dispose() => _http.Dispose();

    private sealed class ContentEntry
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("path")] public string? Path { get; set; }
        [JsonPropertyName("sha")] public string? Sha { get; set; }
        [JsonPropertyName("size")] public long Size { get; set; }
        [JsonPropertyName("type")] public string? Type { get; set; }
    }
}
