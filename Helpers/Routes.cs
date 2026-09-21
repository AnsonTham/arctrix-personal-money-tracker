namespace Arctrix.PersonalMoneyTracker.Helpers;

/// <summary>Shell route names, kept in one place so navigation strings never drift.</summary>
public static class Routes
{
    public const string Dashboard = "dashboard";
    public const string History = "history";
    public const string Accounts = "accounts";
    public const string Analytics = "analytics";
    public const string Recurring = "recurring";
    public const string Reports = "reports";
    public const string Settings = "settings";

    public const string AddTransaction = "addtransaction";
    public const string AddAccount = "addaccount";
    public const string EditAccount = "editaccount";
    public const string PublicHolidays = "publicholidays";
    public const string AddRecurring = "addrecurring";
    public const string AddPrepaidCredit = "addprepaidcredit";

    // Phone tab bar only: "More" hub, and the "Add" tab that opens AddTransaction instead of navigating.
    public const string More = "more";
    public const string QuickAdd = "quickadd";

    // Mobile only: receipt scanning, and viewing a transaction's receipt photo.
    public const string ScanReceipt = "scanreceipt";
    public const string ReceiptPhoto = "receiptphoto";

    public const string TransactionTypeParam = "type";
    public const string TransactionIdParam = "id";
    public const string PrepaidCreditIdParam = "creditid";
    public const string AccountIdParam = "accountid";
    public const string ReceiptScanParam = "receiptscan";
    public const string ReceiptPathParam = "receiptpath";
}
