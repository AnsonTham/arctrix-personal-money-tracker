using System.Text.Json;

namespace Arctrix.PersonalMoneyTracker.Services.Telegram;

/// <summary>
/// One Telegram message as the relay stored it: the parsed json file, plus the photo file the relay
/// downloaded alongside it, if any.
/// </summary>
public sealed record RelayUpdate(
    long UpdateId,
    long FromUserId,
    long ChatId,
    string Text,
    DateTime SentUtc,
    bool HasPhoto)
{
    /// <summary>The json file this came from; deleted once the message has been dealt with.</summary>
    public required RelayFile Source { get; init; }

    /// <summary>The photo the relay saved next to it (pending/&lt;update id&gt;.jpg), when there is one.</summary>
    public RelayFile? Photo { get; init; }

    /// <summary>
    /// True only for the account the app belongs to. Everything else is dropped without being read
    /// any further - no parsing, no OCR, no reply.
    /// </summary>
    public bool IsFrom(long ownerTelegramUserId) => ownerTelegramUserId > 0 && FromUserId == ownerTelegramUserId;

    /// <summary>
    /// Reads a relay json file. Returns null for anything that isn't a message the app can act on
    /// (edits, channel posts, malformed files).
    /// </summary>
    public static RelayUpdate? FromJson(byte[] json, RelayFile source, RelayFile? photo)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!root.TryGetProperty("update_id", out var updateId))
                return null;
            if (!root.TryGetProperty("message", out var message))
                return null;
            if (!message.TryGetProperty("from", out var from) || !from.TryGetProperty("id", out var fromId))
                return null;

            var chatId = message.TryGetProperty("chat", out var chat) && chat.TryGetProperty("id", out var id)
                ? id.GetInt64()
                : fromId.GetInt64();

            // A photo message carries its text in "caption"; the relay saves the image separately.
            var text = message.TryGetProperty("text", out var t) ? t.GetString()
                : message.TryGetProperty("caption", out var c) ? c.GetString()
                : null;

            var sent = message.TryGetProperty("date", out var date)
                ? DateTimeOffset.FromUnixTimeSeconds(date.GetInt64()).UtcDateTime
                : DateTime.UtcNow;

            return new RelayUpdate(
                updateId.GetInt64(),
                fromId.GetInt64(),
                chatId,
                text?.Trim() ?? string.Empty,
                sent,
                message.TryGetProperty("photo", out var photos) && photos.GetArrayLength() > 0)
            {
                Source = source,
                Photo = photo
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
