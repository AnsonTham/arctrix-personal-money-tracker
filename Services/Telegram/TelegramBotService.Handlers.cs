using System.Globalization;
using Arctrix.PersonalMoneyTracker.Models;

namespace Arctrix.PersonalMoneyTracker.Services.Telegram;

/// <summary>
/// What the bot does with a message: answer a command, read a receipt photo, or take a transaction
/// through its confirmation. Everything that reaches the database goes through ITransactionService,
/// exactly like the screens do - this is a new way in, not a second set of rules.
/// </summary>
public sealed partial class TelegramBotService
{
    private const string Help =
        "Arctrix money tracker bot. I can:\n\n"
        + "/balance - account balances and net worth\n"
        + "/add - add a transaction step by step\n"
        + "/cancel - drop whatever I'm asking about\n\n"
        + "Or just tell me: \"spent 12.50 on lunch\", \"salary 3200\".\n"
        + "Send a photo of a receipt and I'll read it.\n\n"
        + "Nothing is ever saved until you confirm it.";

    private async Task HandleAsync(RelayUpdate update, CancellationToken cancellationToken)
    {
        _lastConversationUtc = DateTime.UtcNow;

        // A photo is always a new receipt, even mid-conversation: it can't be an answer to a question.
        if (update.HasPhoto && update.Photo is not null)
        {
            await HandleReceiptAsync(update, cancellationToken);
            return;
        }

        var text = update.Text;
        if (text.Length == 0)
        {
            await SendAsync(update.ChatId, "I can only read text messages and photos.", cancellationToken);
            return;
        }

        var command = text.Split([' ', '@'], 2)[0].ToLowerInvariant();
        switch (command)
        {
            case "/start":
            case "/help":
                await SendAsync(update.ChatId, Help, cancellationToken);
                return;

            case "/balance":
            case "balance":
                await SendAsync(update.ChatId, await BalanceReportAsync(), cancellationToken);
                return;

            case "/cancel":
                await CancelDraftAsync(update.ChatId, cancellationToken);
                return;

            case "/add":
                _state.AwaitingEntry = true;
                _state.Save();
                await SendAsync(update.ChatId, "How much, and what for? For example: 12.50 lunch", cancellationToken);
                return;
        }

        // A pending question takes priority: this message is an answer to it.
        if (_state.ActiveDraft is { } draft)
        {
            await AnswerDraftAsync(draft, text, update.ChatId, cancellationToken);
            return;
        }

        var wasAsked = _state.AwaitingEntry;
        _state.AwaitingEntry = false;

        var entry = EntryParser.TryParse(text, DateTime.Today);
        if (entry is null)
        {
            _state.Save();
            await SendAsync(
                update.ChatId,
                wasAsked
                    ? "I couldn't find an amount in that. Try something like: 12.50 lunch"
                    : "I didn't catch an amount in that. Send /help to see what I understand.",
                cancellationToken);
            return;
        }

        var newDraft = await BuildDraftAsync(entry.Type, entry.Amount, entry.Description, entry.Date, null, update.ChatId);
        await QueueAsync(newDraft, cancellationToken);
    }

    /// <summary>
    /// Reads a receipt photo with the same pipeline the phone uses: on-device OCR, then the shared
    /// ReceiptParser. The photo is stored first, so it can be attached to the saved transaction.
    /// </summary>
    private async Task HandleReceiptAsync(RelayUpdate update, CancellationToken cancellationToken)
    {
        if (_relay is null || update.Photo is null)
            return;

        await SendAsync(update.ChatId, "Reading that receipt...", cancellationToken);

        string storedPath;
        try
        {
            var bytes = await _relay.DownloadAsync(update.Photo, cancellationToken);
            var temporaryPath = Path.Combine(Path.GetTempPath(), update.Photo.Name);
            await File.WriteAllBytesAsync(temporaryPath, bytes, cancellationToken);

            storedPath = await _photos.SaveAsync(new FileResult(temporaryPath));
            File.Delete(temporaryPath);
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException or UnauthorizedAccessException)
        {
            await SendAsync(update.ChatId, $"I couldn't download that photo: {ex.Message}", cancellationToken);
            return;
        }

        ReceiptScanResult scan;
        try
        {
            var text = await _ocr.RecognizeTextAsync(_photos.GetFullPath(storedPath), cancellationToken);
            scan = ReceiptParser.Parse(text, DateTime.Today);
        }
        catch (Exception ex)
        {
            _photos.Delete(storedPath);
            await SendAsync(update.ChatId, $"I couldn't read that photo: {ex.Message}", cancellationToken);
            return;
        }

        if (!scan.HasUsefulData || scan.Total is null)
        {
            _photos.Delete(storedPath);
            await SendAsync(
                update.ChatId,
                "I couldn't find a total on that receipt. Try a straighter, better-lit photo, or just tell me the amount.",
                cancellationToken);
            return;
        }

        var draft = await BuildDraftAsync(
            TransactionType.Expense,
            scan.Total.Value,
            scan.ShopName ?? "Receipt",
            scan.Date?.Date ?? DateTime.Today,
            storedPath,
            update.ChatId,
            scan.SuggestedCategory);

        var itemCount = scan.Items.Count;
        await SendAsync(
            update.ChatId,
            $"Receipt read{(itemCount > 0 ? $" ({itemCount} item{(itemCount == 1 ? "" : "s")})" : "")}:",
            cancellationToken);
        await QueueAsync(draft, cancellationToken);
    }

    /// <summary>Adds a draft to the queue and asks about it, unless an older one is still being answered.</summary>
    private async Task QueueAsync(BotDraft draft, CancellationToken cancellationToken)
    {
        var wasBusy = _state.ActiveDraft is not null;
        _state.Drafts.Add(draft);
        _state.Save();

        await SendAsync(
            draft.ChatId,
            wasBusy
                ? "I'll ask about that one once we're done with the current entry."
                : Describe(draft),
            cancellationToken);
    }

    /// <summary>A yes, a no, or a correction to the transaction currently being asked about.</summary>
    private async Task AnswerDraftAsync(BotDraft draft, string text, long chatId, CancellationToken cancellationToken)
    {
        switch (text.Trim().ToLowerInvariant())
        {
            case "y" or "yes" or "ok" or "okay" or "save" or "confirm" or "correct":
                await SaveDraftAsync(draft, chatId, cancellationToken);
                return;

            case "n" or "no" or "cancel" or "discard" or "nope":
                await CancelDraftAsync(chatId, cancellationToken);
                return;
        }

        if (!await ApplyCorrectionAsync(draft, text))
        {
            await SendAsync(
                chatId,
                "I didn't understand that. Reply yes to save, no to discard, or correct one field:\n"
                + "amount 28.20 | account Cash | category Food | date 18/09/2026 | note Lunch | income | expense",
                cancellationToken);
            return;
        }

        _state.Save();
        await SendAsync(chatId, Describe(draft), cancellationToken);
    }

    /// <summary>Applies "amount 28.20", "account Cash", "category Food", "date 18/09", "note ...", "income"/"expense".</summary>
    private async Task<bool> ApplyCorrectionAsync(BotDraft draft, string text)
    {
        var parts = text.Trim().Split(' ', 2, StringSplitOptions.TrimEntries);
        var field = parts[0].ToLowerInvariant();
        var value = parts.Length > 1 ? parts[1] : string.Empty;

        switch (field)
        {
            case "income":
                draft.Type = TransactionType.Income;
                return true;

            case "expense":
                draft.Type = TransactionType.Expense;
                return true;

            case "amount" when decimal.TryParse(value.Replace(",", string.Empty), NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) && amount > 0:
                draft.Amount = amount;
                return true;

            case "note" or "notes" or "description" when value.Length > 0:
                draft.Notes = value;
                return true;

            case "date" when TryParseDate(value, out var date):
                draft.Date = date;
                return true;

            case "account" when value.Length > 0:
            {
                var account = (await _accounts.GetAllAsync())
                    .FirstOrDefault(a => a.Name.Contains(value, StringComparison.OrdinalIgnoreCase));
                if (account is null)
                    return false;

                draft.AccountId = account.Id;
                draft.AccountName = account.Name;
                draft.Currency = account.Currency;
                return true;
            }

            case "category" when value.Length > 0:
            {
                var category = (await _categories.GetAllAsync())
                    .FirstOrDefault(c => c.Name.Contains(value, StringComparison.OrdinalIgnoreCase));
                if (category is null)
                    return false;

                draft.CategoryId = category.Id;
                draft.CategoryName = category.Name;
                return true;
            }
        }

        return false;
    }

    private static bool TryParseDate(string value, out DateTime date)
    {
        // Day first, matching the receipts and the rest of the app.
        string[] formats = ["d/M/yyyy", "d/M/yy", "d/M", "d-M-yyyy", "d-M-yy", "yyyy-MM-dd", "d MMM yyyy", "d MMM"];
        foreach (var format in formats)
        {
            if (DateTime.TryParseExact(value.Trim(), format, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
            {
                // A date with no year means this year.
                if (!format.Contains('y'))
                    date = new DateTime(DateTime.Today.Year, date.Month, date.Day);
                return true;
            }
        }

        return DateTime.TryParse(value.Trim(), CultureInfo.CurrentCulture, DateTimeStyles.None, out date);
    }

    private async Task SaveDraftAsync(BotDraft draft, long chatId, CancellationToken cancellationToken)
    {
        var settings = await _appSettings.GetAsync();
        var baseCurrency = settings.BaseCurrency;
        var rate = _currency.GetRate(draft.Currency, baseCurrency);

        var record = new TransactionRecord
        {
            Type = draft.Type,
            AccountId = draft.AccountId,
            CategoryId = draft.CategoryId,
            OriginalAmount = draft.Amount,
            OriginalCurrency = draft.Currency,
            ExchangeRate = rate,
            BaseAmount = draft.Amount * rate,
            BaseCurrencyAtEntry = baseCurrency,
            Date = draft.Date,
            Notes = draft.Notes,
            ReceiptImagePath = draft.ReceiptPath,
            CreatedAt = DateTime.Now
        };

        try
        {
            await _transactions.AddAsync(record);
        }
        catch (Exception ex)
        {
            // The save is atomic, so nothing was written; keep the draft so it can be retried.
            await SendAsync(chatId, $"I couldn't save that: {ex.Message}", cancellationToken);
            return;
        }

        _state.Drafts.Remove(draft);
        _state.Save();

        var account = await _accounts.GetByIdAsync(draft.AccountId);
        var balance = account is null
            ? string.Empty
            : $"\n{account.Name} is now {Money(account.Balance, account.Currency)}.";

        await SendAsync(chatId, $"Saved. {Summary(draft)}{balance}", cancellationToken);
        await AskNextAsync(chatId, cancellationToken);
    }

    private async Task CancelDraftAsync(long chatId, CancellationToken cancellationToken)
    {
        if (_state.ActiveDraft is not { } draft)
        {
            _state.AwaitingEntry = false;
            _state.Save();
            await SendAsync(chatId, "Nothing to cancel.", cancellationToken);
            return;
        }

        // A receipt photo that was never saved is just a leftover file.
        _photos.Delete(draft.ReceiptPath);
        _state.Drafts.Remove(draft);
        _state.AwaitingEntry = false;
        _state.Save();

        await SendAsync(chatId, "Dropped - nothing was saved.", cancellationToken);
        await AskNextAsync(chatId, cancellationToken);
    }

    private async Task AskNextAsync(long chatId, CancellationToken cancellationToken)
    {
        if (_state.ActiveDraft is { } next)
            await SendAsync(chatId, $"Next one:\n{Describe(next)}", cancellationToken);
    }

    /// <summary>Fills in the parts of a transaction the message didn't say: account, category, currency.</summary>
    private async Task<BotDraft> BuildDraftAsync(
        TransactionType type,
        decimal amount,
        string description,
        DateTime date,
        string? receiptPath,
        long chatId,
        string? suggestedCategory = null)
    {
        var accounts = await _accounts.GetAllAsync();
        var account = accounts.FirstOrDefault();
        var category = await MatchCategoryAsync(suggestedCategory ?? description, type);

        return new BotDraft
        {
            Type = type,
            Amount = amount,
            Currency = account?.Currency ?? (await _appSettings.GetAsync()).BaseCurrency,
            AccountId = account?.Id ?? 0,
            AccountName = account?.Name ?? "(no account)",
            CategoryId = category?.Id ?? 0,
            CategoryName = category?.Name ?? "Others",
            Date = date,
            Notes = description,
            ReceiptPath = receiptPath,
            ChatId = chatId
        };
    }

    private async Task<Category?> MatchCategoryAsync(string text, TransactionType type)
    {
        var categories = await _categories.GetAllAsync();
        if (categories.Count == 0)
            return null;

        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var match = categories.FirstOrDefault(c =>
            text.Contains(c.Name, StringComparison.OrdinalIgnoreCase)
            || words.Any(w => c.Name.Contains(w, StringComparison.OrdinalIgnoreCase) && w.Length > 2));

        return match
            ?? categories.FirstOrDefault(c => c.Name.Equals(type == TransactionType.Income ? "Income" : "Others", StringComparison.OrdinalIgnoreCase))
            ?? categories[0];
    }

    private async Task<string> BalanceReportAsync()
    {
        var accounts = await _accounts.GetAllAsync();
        if (accounts.Count == 0)
            return "There are no accounts in the app yet.";

        var baseCurrency = (await _appSettings.GetAsync()).BaseCurrency;
        var netWorth = accounts.Sum(a => _accounts.BalanceIn(a, baseCurrency));
        var lines = accounts
            .OrderByDescending(a => _accounts.BalanceIn(a, baseCurrency))
            .Select(a => $"{a.Name}: {Money(a.Balance, a.Currency)}");

        return $"Balances\n{string.Join('\n', lines)}\n\nNet worth: {Money(netWorth, baseCurrency)}";
    }

    private static string Describe(BotDraft draft) =>
        $"{Summary(draft)}\n"
        + $"Account: {draft.AccountName}\n"
        + $"Category: {draft.CategoryName}\n"
        + $"Date: {draft.Date:d MMM yyyy}\n"
        + (draft.ReceiptPath is null ? string.Empty : "Receipt photo attached\n")
        + "\nReply yes to save, no to discard, or correct a field (amount / account / category / date / note).";

    private static string Summary(BotDraft draft) =>
        $"{(draft.Type == TransactionType.Income ? "Income" : "Expense")} {Money(draft.Amount, draft.Currency)}"
        + (string.IsNullOrWhiteSpace(draft.Notes) ? string.Empty : $" - {draft.Notes}");

    private static string Money(decimal amount, string currency) =>
        $"{currency} {amount.ToString("N2", CultureInfo.InvariantCulture)}";
}
