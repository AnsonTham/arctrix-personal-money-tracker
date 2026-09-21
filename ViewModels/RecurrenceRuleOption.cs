using CommunityToolkit.Mvvm.ComponentModel;
using Arctrix.PersonalMoneyTracker.Models;

namespace Arctrix.PersonalMoneyTracker.ViewModels;

/// <summary>A selectable chip for how a recurring payment picks its date.</summary>
public partial class RecurrenceRuleOption : ObservableObject
{
    public RecurrenceRuleOption(RecurrenceRuleType rule, string label)
    {
        Rule = rule;
        Label = label;
    }

    public RecurrenceRuleType Rule { get; }

    public string Label { get; }

    [ObservableProperty] public partial bool IsSelected { get; set; }
}
