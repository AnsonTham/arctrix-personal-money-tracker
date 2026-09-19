using System.Text.Json;

namespace Arctrix.PersonalMoneyTracker.Services.Telegram;

/// <summary>
/// One Telegram update as the relay stored it: a message with its photo, if the relay downloaded
/// one, or a tap on one of the bot's inline buttons.
/// </summary>
public sealed record RelayUpdate(
    long UpdateId,
    long FromUserId,
    long ChatId,
    string Text,
    DateTime SentUtc,
    bool HasPhoto)
{
    /// <summary>The json file this came from; deleted once the update has been dealt with.</summary>
    public required RelayFile Source { get; init; }

    /// <summary>The photo the relay saved next to it (pending/&lt;update id&gt;.jpg), when there is one.</summary>
    public RelayFile? Photo { get; init; }

    /// <summary>What a tapped button carries; null for an ordinary message.</summary>
    public string? CallbackData { get; init; }

    /// <summary>Telegram wants every button tap acknowledged, by this id.</summary>
    public string? CallbackId { get; init; }

    /// <summary>The message the tapped button belongs to, so its buttons can be taken away.</summary>
    public int? MessageId { get; init; }

    public bool IsButtonTap => CallbackData is not null;

    /// <summary>
    /// True only for the account the app belongs to. Everything else is dropped without being read
    /// any further - no parsing, no OCR, no reply.
    /// </summary>
    public bool IsFrom(long ownerTelegramUserId) => ownerTelegramUserId > 0 && FromUserId == ownerTelegramUserId;

    /// <summary>
    /// Reads a relay json file. Returns null for anything that isn't a message or a button tap the
    /// app can act on (edits, channel posts, malformed files).
    /// </summary>
    public static RelayUpdate? FromJson(byte[] json, RelayFile source, RelayFile? photo)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!root.TryGetProperty("update_id", out var updateId))
                return null;

            if (root.TryGetProperty("callback_query", out var callback))
                return FromCallback(updateId.GetInt64(), callback, source);

            if (!root.TryGetProperty("message", out var message))
                return null;
            if (!message.TryGetProperty("from", out var from) || !from.TryGetProperty("id", out var fromId))
                return null;

            // A photo message carries its text in "caption"; the relay saves the image separately.
            var text = message.TryGetProperty("text", out var t) ? t.GetString()
                : message.TryGetProperty("caption", out var c) ? c.GetString()
                : null;

            return new RelayUpdate(
                updateId.GetInt64(),
                fromId.GetInt64(),
                ChatOf(message, fromId.GetInt64()),
                text?.Trim() ?? string.Empty,
                SentAt(message),
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

    private static RelayUpdate? FromCallback(long updateId, JsonElement callback, RelayFile source)
    {
        if (!callback.TryGetProperty("from", out var from) || !from.TryGetProperty("id", out var fromId))
            return null;
        if (!callback.TryGetProperty("data", out var data) || data.GetString() is not { } payload)
            return null;

        var message = callback.TryGetProperty("message", out var m) ? m : default;
        var chatId = message.ValueKind == JsonValueKind.Object ? ChatOf(message, fromId.GetInt64()) : fromId.GetInt64();

        return new RelayUpdate(updateId, fromId.GetInt64(), chatId, string.Empty, DateTime.UtcNow, HasPhoto: false)
        {
            Source = source,
            CallbackData = payload,
            CallbackId = callback.TryGetProperty("id", out var id) ? id.GetString() : null,
            MessageId = message.ValueKind == JsonValueKind.Object && message.TryGetProperty("message_id", out var messageId)
                ? messageId.GetInt32()
                : null
        };
    }

    private static long ChatOf(JsonElement message, long fallback) =>
        message.TryGetProperty("chat", out var chat) && chat.TryGetProperty("id", out var id)
            ? id.GetInt64()
            : fallback;

    private static DateTime SentAt(JsonElement message) =>
        message.TryGetProperty("date", out var date)
            ? DateTimeOffset.FromUnixTimeSeconds(date.GetInt64()).UtcDateTime
            : DateTime.UtcNow;
}
