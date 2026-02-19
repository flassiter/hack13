using System.Text.Json;

namespace Hack13.TerminalServer.Navigation;

/// <summary>
/// Loads and provides access to test loan data from JSON.
/// </summary>
public class TestDataStore
{
    private readonly Dictionary<string, LoanRecord> _loans = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public void LoadFromFile(string path)
    {
        var json = File.ReadAllText(path);
        var dataFile = JsonSerializer.Deserialize<TestDataFile>(json, JsonOptions)
            ?? throw new InvalidOperationException($"Failed to deserialize test data: {path}");

        foreach (var loan in dataFile.Loans)
        {
            _loans[loan.LoanNumber] = loan;
        }
    }

    public bool TryGetLoan(string loanNumber, out LoanRecord? loan)
    {
        return _loans.TryGetValue(loanNumber, out loan);
    }

    /// <summary>
    /// Returns all loan fields as a flat string dictionary for screen rendering.
    /// </summary>
    public Dictionary<string, string> GetLoanData(string loanNumber)
    {
        if (!_loans.TryGetValue(loanNumber, out var loan))
            return new Dictionary<string, string>();

        return loan.ToDataDictionary();
    }
}

public class TestDataFile
{
    public List<LoanRecord> Loans { get; set; } = new();
}

public class LoanRecord
{
    public string LoanNumber { get; set; } = string.Empty;
    public string BorrowerName { get; set; } = string.Empty;
    public string PropertyAddress { get; set; } = string.Empty;
    public string LoanType { get; set; } = string.Empty;
    public string OriginalAmount { get; set; } = string.Empty;
    public string CurrentBalance { get; set; } = string.Empty;
    public string InterestRate { get; set; } = string.Empty;
    public string MonthlyPayment { get; set; } = string.Empty;
    public string NextDueDate { get; set; } = string.Empty;
    public string LoanStatus { get; set; } = string.Empty;
    public string OriginationDate { get; set; } = string.Empty;
    public string MaturityDate { get; set; } = string.Empty;

    // Escrow fields
    public string EscrowBalance { get; set; } = string.Empty;
    public string EscrowPayment { get; set; } = string.Empty;
    public string RequiredReserve { get; set; } = string.Empty;
    public string ShortageAmount { get; set; } = string.Empty;
    public string SurplusAmount { get; set; } = string.Empty;
    public string EscrowStatus { get; set; } = string.Empty;
    public string TaxAmount { get; set; } = string.Empty;
    public string HazardInsurance { get; set; } = string.Empty;
    public string FloodInsurance { get; set; } = string.Empty;
    public string MortgageInsurance { get; set; } = string.Empty;
    public string LastAnalysisDate { get; set; } = string.Empty;
    public string NextAnalysisDate { get; set; } = string.Empty;
    public string ProjectedBalance { get; set; } = string.Empty;
    public string ProjectedShortage { get; set; } = string.Empty;

    public Dictionary<string, string> ToDataDictionary()
    {
        return new Dictionary<string, string>
        {
            ["loan_number"] = LoanNumber,
            ["borrower_name"] = BorrowerName,
            ["property_address"] = PropertyAddress,
            ["loan_type"] = LoanType,
            ["original_amount"] = OriginalAmount,
            ["current_balance"] = CurrentBalance,
            ["interest_rate"] = InterestRate,
            ["monthly_payment"] = MonthlyPayment,
            ["next_due_date"] = NextDueDate,
            ["loan_status"] = LoanStatus,
            ["origination_date"] = OriginationDate,
            ["maturity_date"] = MaturityDate,
            ["escrow_balance"] = EscrowBalance,
            ["escrow_payment"] = EscrowPayment,
            ["required_reserve"] = RequiredReserve,
            ["shortage_amount"] = ShortageAmount,
            ["surplus_amount"] = SurplusAmount,
            ["escrow_status"] = EscrowStatus,
            ["tax_amount"] = TaxAmount,
            ["hazard_insurance"] = HazardInsurance,
            ["flood_insurance"] = FloodInsurance,
            ["mortgage_insurance"] = MortgageInsurance,
            ["last_analysis_date"] = LastAnalysisDate,
            ["next_analysis_date"] = NextAnalysisDate,
            ["projected_balance"] = ProjectedBalance,
            ["projected_shortage"] = ProjectedShortage,
        };
    }
}
