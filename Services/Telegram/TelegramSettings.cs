using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Arctrix.PersonalMoneyTracker.Services.Telegram;

/// <summary>
/// Credentials for the Telegram bot and the relay repository, read from a local file that is never
/// committed (see .gitignore and telegram.local.example.json). Nothing here is ever logged.
/// </summary>
public sealed class TelegramSettings
{
    private const string FileName = "telegram.local.json";

    [JsonPropertyName("botToken")]
    public string BotToken { get; init; } = string.Empty;

    [JsonPropertyName("gitHubToken")]
    public string GitHubToken { get; init; } = string.Empty;

    [JsonPropertyName("relayRepository")]
    public string RelayRepository { get; init; } = "AnsonTham/arctrix-telegram-relay";

    [JsonPropertyName("relayBranch")]
    public string RelayBranch { get; init; } = "main";

    /// <summary>
    /// The only Telegram account the bot obeys. Anyone can find a bot and message it, so anything
    /// from another account is dropped without being processed. It lives here rather than in source
    /// because this repository is public; the default of zero matches nobody, so a missing or
    /// mistyped value switches the bot off entirely instead of widening it.
    /// </summary>
    [JsonPropertyName("ownerTelegramUserId")]
    public long OwnerTelegramUserId { get; init; }

    /// <summary>True once both secrets and the owner's id are filled in; the bot stays off until then.</summary>
    [JsonIgnore]
    public bool IsComplete =>
        !string.IsNullOrWhiteSpace(BotToken)
        && !string.IsNullOrWhiteSpace(GitHubToken)
        && RelayRepository.Contains('/')
        && OwnerTelegramUserId > 0;

    /// <summary>
    /// Loads the config from the app's data folder, falling back to the copy next to the executable
    /// (which is where the project's own telegram.local.json lands when running from source).
    /// Returns an empty, incomplete instance when there is no file - a missing config is not an error.
    /// </summary>
    public static TelegramSettings Load()
    {
        foreach (var path in CandidatePaths())
        {
            if (!File.Exists(path))
                continue;

            try
            {
                var settings = JsonSerializer.Deserialize<TelegramSettings>(File.ReadAllText(path));
                if (settings is not null)
                    return settings;
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                // Report the problem, never the contents: the file holds two secrets.
                Debug.WriteLine($"Couldn't read {FileName}: {ex.GetType().Name}");
            }
        }

        return new TelegramSettings();
    }

    private static IEnumerable<string> CandidatePaths()
    {
        yield return Path.Combine(FileSystem.AppDataDirectory, FileName);
        yield return Path.Combine(AppContext.BaseDirectory, FileName);
    }

    /// <summary>Where to put the file, for the message shown when it is missing.</summary>
    public static string ExpectedPath => Path.Combine(FileSystem.AppDataDirectory, FileName);
}
