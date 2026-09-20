using System.Diagnostics;
using System.Globalization;
using Arctrix.PersonalMoneyTracker.Models;
using Telegram.Bot.Types.ReplyMarkups;

namespace Arctrix.PersonalMoneyTracker.Services.Telegram;

/// <summary>
/// What the bot does with an update: answer a command, read a receipt photo, act on a tapped
/// button, or take a transaction through its confirmation. Everything that reaches the database
/// goes through the existing services - ITransactionService for money, IRecurringPaymentService for
/// subscriptions - so this is a new way in, not a second set of rules.
/// </summary>
public sealed partial class TelegramBotService
{
    private const string Help =
        "Arctrix money tracker bot. I can:\n\n"
        + "/balance - account balances and net worth\n"
        + "/addexpense - record an expense\n"
        + "/addincome - record income\n"
        + "/subscriptions - recurring payments, and cancel one\n"
        + "/savings - projected savings at this rate\n"
        + "/cancel - drop whatever I'm asking about\n\n"
        + "Or just tell me: \"spent 12.50 on lunch\", \"salary 3200\".\n"
        + "Send a photo of a receipt and I'll read it.\n\n"
        + "Nothing is ever saved until you confirm it.";

    // Button payloads. Short by necessity: Telegram allows 64 bytes of callback data.
    private const string ConfirmAction = "d:ok";
    private const string CancelAction = "d:no";
    private const string EditAmountAction = "d:amt";
    private const string EditCategoryAction = "d:cat";
    private const string OverdrawAction = "d:any";
    private const string CategoryPrefix = "c:";
    private const string StopSubscriptionPrefix = "s:";
    private const string CoverShortfallPrefix = "t:";

    private async Task HandleAsync(RelayUpdate update, CancellationToken cancellationToken)
    {
        _lastConversationUtc = DateTime.UtcNow;

        if (update.IsButtonTap)
        {
            await HandleButtonAsync(update, cancellationToken);
            return;
        }

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

            case "/savings":
            case "savings":
                await SendAsync(update.ChatId, await SavingsReportAsync(), cancellationToken);
                return;

            case "/subscriptions":
            case "subscriptions":
                await SendSubscriptionsAsync(update.ChatId, cancellationToken);
                return;

            case "/cancel":
                await CancelDraftAsync(update.ChatId, cancellationToken);
                return;

            case "/add":
            case "/addexpense":
                await AskForEntryAsync(update.ChatId, TransactionType.Expense, cancellationToken);
                return;

            case "/addincome":
                await AskForEntryAsync(update.ChatId, TransactionType.Income, cancellationToken);
                return;
        }

        // A field the bot is waiting for, then a pending question, then a new entry.
        if (_state.AwaitingField == "amount" && _state.ActiveDraft is { } editing)
        {
            await ApplyNewAmountAsync(editing, text, update.ChatId, cancellationToken);
            return;
        }

        if (_state.ActiveDraft is { } draft)
        {
            await AnswerDraftAsync(draft, text, update.ChatId, cancellationToken);
            return;
        }

        var wasAsked = _state.AwaitingEntry;
        var askedType = _state.AwaitingEntryType;
        _state.AwaitingEntry = false;
        _state.AwaitingEntryType = null;

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

        // Free text still decides its own type; /addexpense and /addincome only settle it when the
        // words leave it open.
        var type = askedType ?? entry.Type;
        var newDraft = await BuildDraftAsync(type, entry.Amount, entry.Description, entry.Date, null, update.ChatId);
        await QueueAsync(newDraft, cancellationToken);
    }

    private async Task AskForEntryAsync(long chatId, TransactionType type, CancellationToken cancellationToken)
    {
        _state.AwaitingEntry = true;
        _state.AwaitingEntryType = type;
        _state.Save();
        await SendAsync(
            chatId,
            type == TransactionType.Income
                ? "How much came in, and what for? For example: 3200 salary"
                : "How much, and what for? For example: 12.50 lunch",
            cancellationToken);
    }

    /// <summary>A tap on one of the bot's inline buttons.</summary>
    private async Task HandleButtonAsync(RelayUpdate update, CancellationToken cancellationToken)
    {
        var data = update.CallbackData ?? string.Empty;

        if (data.StartsWith(StopSubscriptionPrefix, StringComparison.Ordinal))
        {
            await StopSubscriptionAsync(update, data[StopSubscriptionPrefix.Length..], cancellationToken);
            return;
        }

        if (data.StartsWith(CoverShortfallPrefix, StringComparison.Ordinal))
        {
            await CoverShortfallAsync(update, data[CoverShortfallPrefix.Length..], cancellationToken);
            return;
        }

        if (_state.ActiveDraft is not { } draft)
        {
            await AcknowledgeAsync(update, cancellationToken, "That entry is no longer waiting.");
            return;
        }

        if (data.StartsWith(CategoryPrefix, StringComparison.Ordinal))
        {
            await PickCategoryAsync(update, draft, data[CategoryPrefix.Length..], cancellationToken);
            return;
        }

        switch (data)
        {
            case ConfirmAction:
                await AcknowledgeAsync(update, cancellationToken, "Saving...");
                await SaveDraftAsync(draft, update.ChatId, allowOverdraw: false, cancellationToken);
                return;

            case OverdrawAction:
                await AcknowledgeAsync(update, cancellationToken, "Saving anyway...");
                await SaveDraftAsync(draft, update.ChatId, allowOverdraw: true, cancellationToken);
                return;

            case CancelAction:
                await AcknowledgeAsync(update, cancellationToken, "Dropped.");
                await CancelDraftAsync(update.ChatId, cancellationToken);
                return;

            case EditAmountAction:
                _state.AwaitingField = "amount";
                _state.Save();
                await AcknowledgeAsync(update, cancellationToken);
                await SendAsync(update.ChatId, $"What should the amount be? Currently {Money(draft.Amount, draft.Currency)}.", cancellationToken);
                return;

            case EditCategoryAction:
                await AcknowledgeAsync(update, cancellationToken);
                await SendAsync(update.ChatId, "Which category?", cancellationToken, await CategoryButtonsAsync(draft.Type));
                return;

            default:
                await AcknowledgeAsync(update, cancellationToken);
                return;
        }
    }

    private async Task PickCategoryAsync(RelayUpdate update, BotDraft draft, string categoryId, CancellationToken cancellationToken)
    {
        await AcknowledgeAsync(update, cancellationToken);

        if (!int.TryParse(categoryId, out var id) || await _categories.GetByIdAsync(id) is not { } category)
        {
            await SendAsync(update.ChatId, "I couldn't find that category any more.", cancellationToken);
            return;
        }

        draft.CategoryId = category.Id;
        draft.CategoryName = category.Name;
        _state.Save();
        await SendAsync(update.ChatId, Describe(draft), cancellationToken, DraftButtons());
    }

    private async Task ApplyNewAmountAsync(BotDraft draft, string text, long chatId, CancellationToken cancellationToken)
    {
        if (EntryParser.TryParse(text, DateTime.Today) is not { } parsed)
        {
            await SendAsync(chatId, "That didn't look like an amount. Try 28.20.", cancellationToken);
            return;
        }

        draft.Amount = parsed.Amount;
        _state.AwaitingField = null;
        _state.Save();
        await SendAsync(chatId, Describe(draft), cancellationToken, DraftButtons());
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

        if (wasBusy)
        {
            await SendAsync(draft.ChatId, "I'll ask about that one once we're done with the current entry.", cancellationToken);
            return;
        }

        await SendAsync(draft.ChatId, Describe(draft), cancellationToken, DraftButtons());
    }

    /// <summary>A yes, a no, or a correction typed out instead of tapped.</summary>
    private async Task AnswerDraftAsync(BotDraft draft, string text, long chatId, CancellationToken cancellationToken)
    {
        switch (text.Trim().ToLowerInvariant())
        {
            case "y" or "yes" or "ok" or "okay" or "save" or "confirm" or "correct":
                await SaveDraftAsync(draft, chatId, allowOverdraw: false, cancellationToken);
                return;

            case "n" or "no" or "cancel" or "discard" or "nope":
                await CancelDraftAsync(chatId, cancellationToken);
                return;

            case "anyway" or "save anyway" or "yes anyway":
                await SaveDraftAsync(draft, chatId, allowOverdraw: true, cancellationToken);
                return;
        }

        if (!await ApplyCorrectionAsync(draft, text))
        {
            await SendAsync(
                chatId,
                "I didn't understand that. Use the buttons, or correct one field:\n"
                + "amount 28.20 | account Cash | category Food | date 18/09/2026 | note Lunch | income | expense",
                cancellationToken,
                DraftButtons());
            return;
        }

        _state.Save();
        await SendAsync(chatId, Describe(draft), cancellationToken, DraftButtons());
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

    private async Task SaveDraftAsync(BotDraft draft, long chatId, bool allowOverdraw, CancellationToken cancellationToken)
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
            await _transactions.AddAsync(record, allowOverdraw);
        }
        catch (InsufficientFundsException shortfall)
        {
            // Nothing was written. The draft stays put, so tapping Save anyway retries the same entry.
            await SendAsync(
                chatId,
                $"{shortfall.AccountName} only holds {Money(shortfall.Balance, shortfall.Currency)}, "
                + $"and this needs {Money(shortfall.Required, shortfall.Currency)}. "
                + "Save it anyway, switch the account (\"account Cash\"), or drop it.",
                cancellationToken,
                OverdrawButtons());
            return;
        }
        catch (Exception ex)
        {
            await SendAsync(chatId, $"I couldn't save that: {ex.Message}", cancellationToken);
            return;
        }

        _state.Drafts.Remove(draft);
        _state.AwaitingField = null;
        _state.Save();

        var account = await _accounts.GetByIdAsync(draft.AccountId);
        var balance = account is null
            ? string.Empty
            : $"\n{account.Name} is now {Money(account.Balance, account.Currency)}.";

        await SendAsync(chatId, $"Saved. {Summary(draft)}{balance}", cancellationToken);

        // Money coming in may be exactly what a skipped subscription was waiting for.
        if (draft.Type is TransactionType.Income or TransactionType.Transfer)
            await RunDuePaymentsAsync(cancellationToken);

        await AskNextAsync(chatId, cancellationToken);
    }

    private async Task CancelDraftAsync(long chatId, CancellationToken cancellationToken)
    {
        if (_state.ActiveDraft is not { } draft)
        {
            _state.AwaitingEntry = false;
            _state.AwaitingEntryType = null;
            _state.Save();
            await SendAsync(chatId, "Nothing to cancel.", cancellationToken);
            return;
        }

        // A receipt photo that was never saved is just a leftover file.
        _photos.Delete(draft.ReceiptPath);
        _state.Drafts.Remove(draft);
        _state.AwaitingEntry = false;
        _state.AwaitingEntryType = null;
        _state.AwaitingField = null;
        _state.Save();

        await SendAsync(chatId, "Dropped - nothing was saved.", cancellationToken);
        await AskNextAsync(chatId, cancellationToken);
    }

    private async Task AskNextAsync(long chatId, CancellationToken cancellationToken)
    {
        if (_state.ActiveDraft is { } next)
            await SendAsync(chatId, $"Next one:\n{Describe(next)}", cancellationToken, DraftButtons());
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

    /// <summary>The same figure and the same wording as the Dashboard shows.</summary>
    private async Task<string> SavingsReportAsync()
    {
        var baseCurrency = (await _appSettings.GetAsync()).BaseCurrency;
        var outlook = await _transactions.GetSavingsOutlookAsync(DateTime.Now, baseCurrency);

        if (!outlook.HasData)
            return "No savings projection yet - it needs at least one full calendar month of history.";

        return $"{ViewModels.DashboardViewModel.SavingsProjection(outlook)}\n"
            + $"{outlook.Basis}.\n"
            + $"Average so far: {Money(outlook.AverageMonthlyNet, outlook.Currency)} a month.";
    }

    private async Task SendSubscriptionsAsync(long chatId, CancellationToken cancellationToken)
    {
        var payments = (await _recurring.GetAllAsync()).OrderBy(p => p.NextDueDate).ToList();
        if (payments.Count == 0)
        {
            await SendAsync(chatId, "No active recurring payments.", cancellationToken);
            return;
        }

        var accounts = (await _accounts.GetAllAsync(includeArchived: true)).ToDictionary(a => a.Id);
        var skips = (await _recurring.GetUnresolvedSkipsAsync())
            .GroupBy(s => s.RecurringPaymentId)
            .ToDictionary(g => g.Key, g => g.OrderBy(s => s.DueDate).First());

        var lines = payments.Select(p =>
        {
            var account = accounts.GetValueOrDefault(p.AccountId)?.Name ?? "unknown account";
            var sign = p.Type == TransactionType.Income ? "+" : "-";
            var skipped = skips.TryGetValue(p.Id, out var skip)
                ? $"\n   Skipped {skip.DueDate:d MMM}: {Money(skip.Shortfall, skip.Currency)} short"
                : string.Empty;
            return $"{p.Name}: {sign}{Money(p.Amount, p.Currency)} from {account}, due {p.NextDueDate:d MMM}{skipped}";
        });

        var buttons = payments.Select(p => new[]
        {
            InlineKeyboardButton.WithCallbackData($"Cancel {Trim(p.Name, 24)}", $"{StopSubscriptionPrefix}{p.Id}")
        });

        await SendAsync(
            chatId,
            $"Recurring payments\n{string.Join('\n', lines)}",
            cancellationToken,
            new InlineKeyboardMarkup(buttons));
    }

    /// <summary>Cancels through the same Deactivate the app's own Stop button uses.</summary>
    private async Task StopSubscriptionAsync(RelayUpdate update, string paymentId, CancellationToken cancellationToken)
    {
        await AcknowledgeAsync(update, cancellationToken);

        if (!int.TryParse(paymentId, out var id))
            return;

        var payment = (await _recurring.GetAllAsync(includeInactive: true)).FirstOrDefault(p => p.Id == id);
        if (payment is null || !payment.IsActive)
        {
            await SendAsync(update.ChatId, "That subscription is already stopped.", cancellationToken);
            return;
        }

        await _recurring.DeactivateAsync(id);
        await SendAsync(
            update.ChatId,
            $"Stopped {payment.Name}. No further transactions will be posted for it; the ones already posted are kept.",
            cancellationToken);
    }

    /// <summary>
    /// Moves money from an account that has it into the one a subscription is short on, as an
    /// ordinary transfer, then runs the due payments again so the occurrence posts straight away.
    /// </summary>
    private async Task CoverShortfallAsync(RelayUpdate update, string payload, CancellationToken cancellationToken)
    {
        await AcknowledgeAsync(update, cancellationToken, "Transferring...");

        var parts = payload.Split(':');
        if (parts.Length != 2 || !int.TryParse(parts[0], out var skipId) || !int.TryParse(parts[1], out var fromAccountId))
            return;

        var skip = (await _recurring.GetUnresolvedSkipsAsync()).FirstOrDefault(s => s.Id == skipId);
        if (skip is null)
        {
            await SendAsync(update.ChatId, "That shortfall is already dealt with.", cancellationToken);
            return;
        }

        var from = await _accounts.GetByIdAsync(fromAccountId);
        var to = await _accounts.GetByIdAsync(skip.AccountId);
        if (from is null || to is null)
        {
            await SendAsync(update.ChatId, "I couldn't find those accounts any more.", cancellationToken);
            return;
        }

        var baseCurrency = (await _appSettings.GetAsync()).BaseCurrency;
        // Move exactly what is missing, expressed in the paying account's currency.
        var amount = Math.Round(skip.Shortfall, 2, MidpointRounding.AwayFromZero);
        var transferCategory = (await _categories.GetAllAsync(includeArchived: true))
            .FirstOrDefault(c => c.DefaultType == TransactionType.Transfer);
        var rate = _currency.GetRate(to.Currency, baseCurrency);

        var transfer = new TransactionRecord
        {
            Type = TransactionType.Transfer,
            AccountId = from.Id,
            ToAccountId = to.Id,
            CategoryId = transferCategory?.Id ?? 0,
            OriginalAmount = amount,
            OriginalCurrency = to.Currency,
            ExchangeRate = rate,
            BaseAmount = amount * rate,
            BaseCurrencyAtEntry = baseCurrency,
            Date = DateTime.Today,
            Notes = $"Top up for {to.Name}",
            CreatedAt = DateTime.Now
        };

        try
        {
            // No override here either: a transfer that would overdraw the source is refused too.
            await _transactions.AddAsync(transfer);
        }
        catch (InsufficientFundsException shortfall)
        {
            await SendAsync(
                update.ChatId,
                $"{shortfall.AccountName} can't cover that either - it holds {Money(shortfall.Balance, shortfall.Currency)}.",
                cancellationToken);
            return;
        }
        catch (Exception ex)
        {
            await SendAsync(update.ChatId, $"I couldn't make that transfer: {ex.Message}", cancellationToken);
            return;
        }

        await SendAsync(
            update.ChatId,
            $"Moved {Money(amount, to.Currency)} from {from.Name} to {to.Name}.",
            cancellationToken);
        await RunDuePaymentsAsync(cancellationToken);
    }

    /// <summary>
    /// Posts whatever is due and reports anything that couldn't be paid. Each occurrence is
    /// mentioned once, and again if it is still unpaid a week later.
    /// </summary>
    private async Task RunDuePaymentsAsync(CancellationToken cancellationToken)
    {
        RecurringRunResult result;
        try
        {
            result = await _recurring.RunDuePaymentsAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Couldn't run the due payments: {ex.Message}");
            return;
        }

        foreach (var skip in result.Skipped.Where(NeedsTelling))
            await ReportShortfallAsync(skip, cancellationToken);

        if (result.Skipped.Count > 0)
            await _recurring.MarkSkipsNotifiedAsync(result.Skipped.Where(NeedsTelling).ToList());

        if (result.Resolved > 0)
            await SendAsync(
                _settings.OwnerTelegramUserId,
                $"{result.Resolved} skipped payment{(result.Resolved == 1 ? " has" : "s have")} now gone through.",
                cancellationToken);

        static bool NeedsTelling(RecurringSkip skip) =>
            skip.NotifiedAt is not { } told || DateTime.Now - told > TimeSpan.FromDays(7);
    }

    /// <summary>
    /// Tells the user when a prepaid pool has run out and the next round needs collecting. Purely a
    /// reminder: no transaction is created here, because the money itself is recorded when it
    /// actually arrives.
    /// </summary>
    private async Task ReportPrepaidCollectionsAsync(CancellationToken cancellationToken)
    {
        List<PrepaidCredit> due;
        try
        {
            due = await _credits.GetDueForCollectionAsync(DateTime.Today);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Couldn't check the prepaid credits: {ex.Message}");
            return;
        }

        if (due.Count == 0)
            return;

        foreach (var credit in due)
        {
            var status = credit.StatusOn(DateTime.Today);
            await SendAsync(
                _settings.OwnerTelegramUserId,
                $"{credit.Name} has run out: all {status.MonthsCovered} months of the "
                + $"{Money(credit.TotalAmount, credit.Currency)} collected on {credit.StartDate:d MMM yyyy} are used up.\n\n"
                + "Time to collect from friends again. Record the new lump sum as ordinary income, then update the "
                + "credit's start date in the app - nothing has been posted for you.",
                cancellationToken);
        }

        await _credits.MarkCollectionNotifiedAsync(due);
    }

    private async Task ReportShortfallAsync(RecurringSkip skip, CancellationToken cancellationToken)
    {
        var payment = (await _recurring.GetAllAsync(includeInactive: true)).FirstOrDefault(p => p.Id == skip.RecurringPaymentId);
        var account = await _accounts.GetByIdAsync(skip.AccountId);
        var name = payment?.Name ?? "A recurring payment";
        var accountName = account?.Name ?? "the paying account";

        // Accounts that could cover it, so the fix is one tap rather than a trip to the app.
        var others = (await _accounts.GetAllAsync())
            .Where(a => a.Id != skip.AccountId && _currency.Convert(a.Balance, a.Currency, skip.Currency) >= skip.Shortfall)
            .OrderByDescending(a => _currency.Convert(a.Balance, a.Currency, skip.Currency))
            .Take(3)
            .ToList();

        var buttons = others.Select(a => new[]
        {
            InlineKeyboardButton.WithCallbackData(
                $"Transfer {Money(skip.Shortfall, skip.Currency)} from {Trim(a.Name, 18)}",
                $"{CoverShortfallPrefix}{skip.Id}:{a.Id}")
        }).ToList();

        await SendAsync(
            _settings.OwnerTelegramUserId,
            $"{name} ({Money(skip.Amount, skip.Currency)}) was due on {skip.DueDate:d MMM} but wasn't paid: "
            + $"{accountName} only holds {Money(skip.Balance, skip.Currency)}, "
            + $"{Money(skip.Shortfall, skip.Currency)} short.\n\n"
            + "Nothing was posted and the account wasn't taken negative. Top it up or handle it manually - "
            + "I'll try again on the next run."
            + (buttons.Count == 0 ? string.Empty : "\n\nOr move the missing amount across now:"),
            cancellationToken,
            buttons.Count == 0 ? null : new InlineKeyboardMarkup(buttons));
    }

    private async Task<InlineKeyboardMarkup> CategoryButtonsAsync(TransactionType type)
    {
        var categories = await _categories.GetAllAsync();
        var ordered = categories
            .OrderByDescending(c => c.DefaultType == type)
            .ThenBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        // Two per row: long category names stay readable on a phone.
        var buttons = ordered
            .Select(c => InlineKeyboardButton.WithCallbackData(c.Name, $"{CategoryPrefix}{c.Id}"))
            .Chunk(2)
            .Select(row => row.AsEnumerable());

        return new InlineKeyboardMarkup(buttons);
    }

    private static InlineKeyboardMarkup DraftButtons() =>
        new(
        [
            [
                InlineKeyboardButton.WithCallbackData("Confirm", ConfirmAction),
                InlineKeyboardButton.WithCallbackData("Cancel", CancelAction)
            ],
            [
                InlineKeyboardButton.WithCallbackData("Edit amount", EditAmountAction),
                InlineKeyboardButton.WithCallbackData("Edit category", EditCategoryAction)
            ]
        ]);

    private static InlineKeyboardMarkup OverdrawButtons() =>
        new(
        [
            [
                InlineKeyboardButton.WithCallbackData("Save anyway", OverdrawAction),
                InlineKeyboardButton.WithCallbackData("Cancel", CancelAction)
            ]
        ]);

    private static string Describe(BotDraft draft) =>
        $"{Summary(draft)}\n"
        + $"Account: {draft.AccountName}\n"
        + $"Category: {draft.CategoryName}\n"
        + $"Date: {draft.Date:d MMM yyyy}\n"
        + (draft.ReceiptPath is null ? string.Empty : "Receipt photo attached\n")
        + "\nConfirm to save it, or correct a field by typing (account Cash, date 18/09, note Lunch).";

    private static string Summary(BotDraft draft) =>
        $"{(draft.Type == TransactionType.Income ? "Income" : "Expense")} {Money(draft.Amount, draft.Currency)}"
        + (string.IsNullOrWhiteSpace(draft.Notes) ? string.Empty : $" - {draft.Notes}");

    private static string Money(decimal amount, string currency) =>
        $"{currency} {amount.ToString("N2", CultureInfo.InvariantCulture)}";

    private static string Trim(string text, int maxLength) =>
        text.Length <= maxLength ? text : text[..(maxLength - 1)] + "…";
}
