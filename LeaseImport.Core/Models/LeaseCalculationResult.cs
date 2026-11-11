namespace LeaseImport.Core.Models;

public class LeaseCalculationResult
{
    public int LeaseCount { get; set; }
    public string HasLeases { get; set; } = string.Empty;
    public string MixedCurrency { get; set; } = string.Empty;
    public string? SummaryRequired { get; set; }
    public decimal? TotalClosing { get; set; }
    public decimal? AverageClosing { get; set; }
    public string ShowRouDisclosure { get; set; } = string.Empty;
    public string ShowMaturityAnalysis { get; set; } = string.Empty;
    public string ShowSubleaseDisclosure { get; set; } = string.Empty;
    public string? ShowCashOutflow { get; set; }
}
