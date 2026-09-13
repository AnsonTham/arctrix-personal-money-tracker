namespace Arctrix.PersonalMoneyTracker.Models;

public enum AccountType
{
    Bank,
    Cash,
    EWallet,
    Investment
}

public enum TransactionType
{
    Income,
    Expense,
    Transfer,
    Investment
}

public enum RecurrenceFrequency
{
    Monthly
}
