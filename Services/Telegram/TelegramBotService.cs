using System.Diagnostics;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace Arctrix.PersonalMoneyTracker.Services.Telegram;

/// <summary>
/// The Telegram side of the app: reads messages the relay collected, answers them, and saves the
/// transactions the user confirms.
///
/// The app never calls Telegram's getUpdates - only the relay workflow does, so the two can never
/// take each other's messages. Telegram is used here for one thing: sending replies. Incoming work
/// always arrives through the relay repository.
/// </summary>
public sealed partial class TelegramBotService : IAsyncDisposable
{
    // Each check costs one Actions run, billed as a whole minute, against the 2,000 a month a
    // private repository gets free. So the mailbox is checked every 90 seconds while a conversation
    // is going and every half hour otherwise: a busy day of three bursts plus twelve idle hours
    // comes to roughly 45 runs, about 1,400 a month. The cost of that thrift is the first message
    // after a quiet spell, which can wait for the idle check; everything after it is prompt.
    private static readonly TimeSpan ActiveInterval = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan IdleInterval = TimeSpan.FromMinutes(30);

    /// <summary>How long a relay run takes to collect and push before its result is worth reading.</summary>
    private static readonly TimeSpan CollectionDelay = TimeSpan.FromSeconds(45);

    /// <summary>A conversation counts as active for this long after the last message.</summary>
    private static readonly TimeSpan ConversationWindow = TimeSpan.FromMinutes(10);

    /// <summary>Gap after which the user is welcomed back with a summary instead of a bare reply.</summary>
    private static readonly TimeSpan WelcomeBackGap = TimeSpan.FromHours(3);

    /// <summary>The relay runs every four hours; nothing for this long means it has stopped.</summary>
    private static readonly TimeSpan MissedRelayGap = TimeSpan.FromHours(20);

    private static readonly TimeSpan GapWarningInterval = TimeSpan.FromHours(6);

    private readonly TelegramSettings _settings;
    private readonly ITransactionService _transactions;
    private readonly IAccountService _accounts;
    private readonly ICategoryService _categories;
    private readonly ICurrencyService _currency;
    private readonly ISettingsService _appSettings;
    private readonly IReceiptPhotoStore _photos;
    private readonly IReceiptOcrService _ocr;
    private readonly IRecurringPaymentService _recurring;
    private readonly IPrepaidCreditService _credits;

    private readonly BotState _state = BotState.Load();
    private readonly SemaphoreSlim _sync = new(1, 1);

    private RelayClient? _relay;
    private ITelegramBotClient? _bot;
    private CancellationTokenSource? _stopping;
    private Task? _loop;
    private DateTime _lastConversationUtc = DateTime.MinValue;

    public TelegramBotService(
        ITransactionService transactions,
        IAccountService accounts,
        ICategoryService categories,
        ICurrencyService currency,
        ISettingsService appSettings,
        IReceiptPhotoStore photos,
        IReceiptOcrService ocr,
        IRecurringPaymentService recurring,
        IPrepaidCreditService credits)
    {
        _settings = TelegramSettings.Load();
        _transactions = transactions;
        _accounts = accounts;
        _categories = categories;
        _currency = currency;
        _appSettings = appSettings;
        _photos = photos;
        _ocr = ocr;
        _recurring = recurring;
        _credits = credits;
    }

    /// <summary>False when the local config is missing or half filled in; the app then behaves as before.</summary>
    public bool IsConfigured => _settings.IsComplete;

    /// <summary>
    /// Starts checking the mailbox in the background. Safe to call when unconfigured - it does
    /// nothing - and safe to call twice.
    /// </summary>
    public void Start()
    {
        if (!IsConfigured)
        {
            Debug.WriteLine($"Telegram bot is off: fill in {TelegramSettings.ExpectedPath} to switch it on.");
            return;
        }
        if (_loop is not null)
            return;

        _relay = new RelayClient(_settings);
        _bot = new TelegramBotClient(_settings.BotToken);
        _stopping = new CancellationTokenSource();
        _loop = Task.Run(() => RunAsync(_stopping.Token));
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await PublishCommandMenuAsync(cancellationToken);

            // Anything that arrived while the app was closed is already waiting; read it first, so
            // the welcome-back summary covers it before anything new is collected.
            await CatchUpAsync(cancellationToken);

            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(IsConversationActive ? ActiveInterval : IdleInterval, cancellationToken);
                await CollectAndSyncAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // The app is closing.
        }
        catch (Exception ex)
        {
            // The bot is a side channel: it must never take the app down with it.
            Debug.WriteLine($"Telegram bot stopped: {ex}");
        }
    }

    /// <summary>
    /// Any message at all starts a conversation, not just one that left a question open: a /balance
    /// is usually followed by something else, and waiting half an hour for that would be absurd.
    /// </summary>
    private bool IsConversationActive => DateTime.UtcNow - _lastConversationUtc < ConversationWindow;

    /// <summary>Empties the mailbox, warns about a stalled relay, and summarises a long absence.</summary>
    private async Task CatchUpAsync(CancellationToken cancellationToken)
    {
        var gap = _state.LastPullUtc is { } last ? DateTime.UtcNow - last : (TimeSpan?)null;

        await WarnIfRelayStalledAsync(cancellationToken);

        // Due payments are normally posted by the Dashboard; running them here as well means a
        // subscription that can't be paid is reported even on a day the app is never looked at.
        await RunDuePaymentsAsync(cancellationToken);
        await ReportPrepaidCollectionsAsync(cancellationToken);

        var handled = await SyncAsync(cancellationToken);
        if (handled > 0 && gap is null or { TotalHours: > 3 })
            await SendAsync(_settings.OwnerTelegramUserId, WelcomeBack(handled, gap), cancellationToken);

        // Then ask the relay for anything sent in the last few seconds, so a message sent while the
        // app was starting doesn't wait for the next cycle.
        await CollectAndSyncAsync(cancellationToken);
    }

    private static string WelcomeBack(int handled, TimeSpan? gap)
    {
        var howLong = gap switch
        {
            null => "since the app last ran",
            { TotalDays: >= 1 } d => $"over the last {(int)d.TotalDays} day{((int)d.TotalDays == 1 ? "" : "s")}",
            { TotalHours: >= 1 } h => $"over the last {(int)h.TotalHours} hours",
            _ => "just now"
        };
        return $"Welcome back - here's what came in while you were away.\n"
            + $"{handled} message{(handled == 1 ? "" : "s")} arrived {howLong}; my replies to them are above.";
    }

    /// <summary>Asks the relay to check Telegram now, gives it time to push, then reads the mailbox.</summary>
    private async Task CollectAndSyncAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (_relay is not null && await _relay.TriggerCollectionAsync(cancellationToken))
                await Task.Delay(CollectionDelay, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            Debug.WriteLine($"Couldn't trigger the relay: {ex.Message}");
        }

        await SyncAsync(cancellationToken);
    }

    /// <summary>
    /// Handles everything waiting in the mailbox and removes it. Returns how many messages were
    /// actually acted on (messages from other accounts are dropped, not counted).
    /// </summary>
    private async Task<int> SyncAsync(CancellationToken cancellationToken)
    {
        if (_relay is null)
            return 0;

        await _sync.WaitAsync(cancellationToken);
        try
        {
            var files = await _relay.ListPendingAsync(cancellationToken);
            var photos = files
                .Where(f => !f.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                .ToDictionary(f => Path.GetFileNameWithoutExtension(f.Name), StringComparer.Ordinal);

            var handled = 0;
            foreach (var file in files.Where(f => f.Name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
            {
                cancellationToken.ThrowIfCancellationRequested();

                photos.TryGetValue(Path.GetFileNameWithoutExtension(file.Name), out var photo);
                var update = RelayUpdate.FromJson(await _relay.DownloadAsync(file, cancellationToken), file, photo);

                if (update is null)
                {
                    await RemoveAsync(file, null, cancellationToken);
                    continue;
                }

                // The one security check that matters: anything not from the owner's account is
                // deleted unread - no parsing, no OCR, and no reply that would confirm the bot exists.
                if (!update.IsFrom(_settings.OwnerTelegramUserId))
                {
                    Debug.WriteLine($"Ignored relay item {update.UpdateId} from Telegram user {update.FromUserId}.");
                    await RemoveAsync(file, photo, cancellationToken);
                    continue;
                }

                if (!_state.AlreadyProcessed(update.UpdateId))
                {
                    await HandleAsync(update, cancellationToken);
                    _state.MarkProcessed(update.UpdateId);
                    handled++;
                }

                await RemoveAsync(file, photo, cancellationToken);
            }

            _state.LastPullUtc = DateTime.UtcNow;
            _state.Save();
            return handled;
        }
        catch (HttpRequestException ex)
        {
            // No network, or GitHub is unhappy: try again on the next cycle.
            Debug.WriteLine($"Couldn't read the relay mailbox: {ex.Message}");
            return 0;
        }
        finally
        {
            _sync.Release();
        }
    }

    private async Task RemoveAsync(RelayFile json, RelayFile? photo, CancellationToken cancellationToken)
    {
        if (_relay is null)
            return;

        await _relay.DeleteAsync(json, cancellationToken);
        if (photo is not null)
            await _relay.DeleteAsync(photo, cancellationToken);
    }

    /// <summary>
    /// The relay writes a state file on every run, whether or not anything arrived. A long silence
    /// there means messages may have expired in Telegram's 24-hour window unseen, so say so plainly.
    /// </summary>
    private async Task WarnIfRelayStalledAsync(CancellationToken cancellationToken)
    {
        if (_relay is null)
            return;
        if (_state.LastGapWarningUtc is { } warned && DateTime.UtcNow - warned < GapWarningInterval)
            return;

        var lastRun = await _relay.GetLastRunAsync(cancellationToken);
        if (lastRun is null || DateTime.UtcNow - lastRun.RanAtUtc <= MissedRelayGap)
            return;

        var silence = (int)(DateTime.UtcNow - lastRun.RanAtUtc).TotalHours;
        _state.LastGapWarningUtc = DateTime.UtcNow;
        _state.Save();
        await SendAsync(
            _settings.OwnerTelegramUserId,
            $"Warning: the relay hasn't run for {silence} hours - it should run every 4. "
            + "Telegram only keeps messages for 24 hours, so anything sent during that gap may have been lost. "
            + "Check the Relay workflow on GitHub: a scheduled workflow is disabled automatically after 60 days without repository activity.",
            cancellationToken);
    }

    private async Task SendAsync(long chatId, string text, CancellationToken cancellationToken, InlineKeyboardMarkup? buttons = null)
    {
        if (_bot is null)
            return;

        try
        {
            await _bot.SendMessage(chatId, text, replyMarkup: buttons, cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            // A reply that doesn't arrive must not stop the message being processed.
            Debug.WriteLine($"Couldn't send a Telegram reply: {ex.Message}");
        }
    }

    /// <summary>
    /// Clears the spinner on a tapped button, and takes the buttons off the message they belonged to
    /// so a stale question can't be answered twice.
    /// </summary>
    private async Task AcknowledgeAsync(RelayUpdate update, CancellationToken cancellationToken, string? toast = null)
    {
        if (_bot is null || update.CallbackId is null)
            return;

        try
        {
            await _bot.AnswerCallbackQuery(update.CallbackId, toast ?? string.Empty, cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            // Telegram forgets a callback id after about a minute, and a tap reaches the app through
            // the relay rather than instantly - so this often fails by design. The tap still counts;
            // taking the buttons away below is what the user actually sees.
            Debug.WriteLine($"Couldn't acknowledge a button tap: {ex.Message}");
        }

        if (update.MessageId is not int messageId)
            return;

        try
        {
            await _bot.EditMessageReplyMarkup(update.ChatId, messageId, replyMarkup: null, cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Couldn't clear the buttons on message {messageId}: {ex.Message}");
        }
    }

    /// <summary>
    /// Fills Telegram's "/" menu. Done through the API rather than by hand in BotFather, so the menu
    /// can't drift away from the commands the app actually implements.
    /// </summary>
    private async Task PublishCommandMenuAsync(CancellationToken cancellationToken)
    {
        if (_bot is null)
            return;

        try
        {
            await _bot.SetMyCommands(
                [
                    new BotCommand { Command = "balance", Description = "Account balances and net worth" },
                    new BotCommand { Command = "addexpense", Description = "Record an expense" },
                    new BotCommand { Command = "addincome", Description = "Record income" },
                    new BotCommand { Command = "subscriptions", Description = "Recurring payments, and cancel one" },
                    new BotCommand { Command = "savings", Description = "Projected savings at this rate" }
                ],
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Couldn't publish the command menu: {ex.Message}");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_stopping is not null)
            await _stopping.CancelAsync();

        if (_loop is not null)
        {
            try
            {
                await _loop;
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown.
            }
        }

        _stopping?.Dispose();
        _relay?.Dispose();
        _sync.Dispose();
    }
}
