using System.Diagnostics;
using System.Text.Json;
using Arctrix.PersonalMoneyTracker.Models;

namespace Arctrix.PersonalMoneyTracker.Services.Telegram;

/// <summary>
/// A transaction the bot has read but not saved. Nothing reaches the database until the user
/// confirms it in the chat, the same "review before saving" rule the scan screen follows.
/// </summary>
public sealed class BotDraft
{
    public TransactionType Type { get; set; } = TransactionType.Expense;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "MYR";
    public int AccountId { get; set; }
    public string AccountName { get; set; } = string.Empty;
    public int CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public DateTime Date { get; set; } = DateTime.Today;
    public string Notes { get; set; } = string.Empty;

    /// <summary>Stored receipt photo (see ReceiptPhotoStore), when this came from a photo.</summary>
    public string? ReceiptPath { get; set; }

    /// <summary>The Telegram chat that asked for it, so the reply goes back to the right place.</summary>
    public long ChatId { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// What the bot needs to remember between runs: drafts waiting for a yes, which updates it has
/// already handled, and when it last emptied the mailbox. Kept as a small json file in app data -
/// it is conversation state, not financial data, so it stays out of the database.
/// </summary>
public sealed class BotState
{
    private const string FileName = "telegram-bot-state.json";

    // Enough to outlast a relay run that hands the same update over twice, without growing forever.
    private const int RememberedUpdates = 200;

    /// <summary>Drafts awaiting confirmation, oldest first; only the first one is being asked about.</summary>
    public List<BotDraft> Drafts { get; set; } = [];

    /// <summary>True after /add, when the next message is expected to describe a transaction.</summary>
    public bool AwaitingEntry { get; set; }

    /// <summary>
    /// Set by /addexpense and /addincome so the entry that follows is recorded as that type even
    /// when the words don't say which it is.
    /// </summary>
    public TransactionType? AwaitingEntryType { get; set; }

    /// <summary>"amount" after the Edit amount button, when the next message is that new amount.</summary>
    public string? AwaitingField { get; set; }

    public List<long> ProcessedUpdateIds { get; set; } = [];

    /// <summary>Last time the app successfully emptied the mailbox; null before the first run.</summary>
    public DateTime? LastPullUtc { get; set; }

    /// <summary>Stops one missed-relay warning from being repeated on every pull.</summary>
    public DateTime? LastGapWarningUtc { get; set; }

    public BotDraft? ActiveDraft => Drafts.FirstOrDefault();

    public bool AlreadyProcessed(long updateId) => ProcessedUpdateIds.Contains(updateId);

    public void MarkProcessed(long updateId)
    {
        if (ProcessedUpdateIds.Contains(updateId))
            return;

        ProcessedUpdateIds.Add(updateId);
        if (ProcessedUpdateIds.Count > RememberedUpdates)
            ProcessedUpdateIds.RemoveRange(0, ProcessedUpdateIds.Count - RememberedUpdates);
    }

    private static string Path => System.IO.Path.Combine(FileSystem.AppDataDirectory, FileName);

    public static BotState Load()
    {
        try
        {
            if (File.Exists(Path))
                return JsonSerializer.Deserialize<BotState>(File.ReadAllText(Path)) ?? new BotState();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"Couldn't read the bot state, starting fresh: {ex.Message}");
        }

        return new BotState();
    }

    public void Save()
    {
        try
        {
            File.WriteAllText(Path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Losing conversation state only costs a repeated question; never fail the app over it.
            Debug.WriteLine($"Couldn't save the bot state: {ex.Message}");
        }
    }
}
