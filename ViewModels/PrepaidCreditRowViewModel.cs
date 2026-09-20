using Arctrix.PersonalMoneyTracker.Models;

namespace Arctrix.PersonalMoneyTracker.ViewModels;

/// <summary>Read-only projection of a prepaid credit for the Recurring page card.</summary>
public class PrepaidCreditRowViewModel
{
    public required PrepaidCredit Credit { get; init; }
    public required PrepaidCreditStatus Status { get; init; }

    /// <summary>Name of the subscription it pays for; empty when it isn't linked to one.</summary>
    public string LinkedPaymentName { get; init; } = string.Empty;

    public int Id => Credit.Id;
    public string Name => Credit.Name;
    public string UsageLabel => Status.UsageLabel;
    public string RemainingLabel => Status.RemainingLabel;
    public double UsedShare => Status.UsedShare;
    public bool NeedsCollecting => Status.NeedsCollecting;

    public bool HasLinkedPayment => LinkedPaymentName.Length > 0;
    public string LinkedLabel => $"Covers {LinkedPaymentName}";

    /// <summary>What to say under the bar: a call to collect, a warning, or when it runs out.</summary>
    public string NoticeLabel => Status switch
    {
        { NeedsCollecting: true } => "Time to collect from friends again",
        { IsNearlyOut: true } => $"Last month covered - runs out {Status.RunsOutOn:d MMM}",
        _ => $"Runs out {Status.RunsOutOn:d MMM yyyy}"
    };

    /// <summary>Only the two urgent states are coloured; the ordinary one stays quiet.</summary>
    public bool IsNoticeUrgent => Status.NeedsCollecting || Status.IsNearlyOut;
}
