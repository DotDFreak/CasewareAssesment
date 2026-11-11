namespace LeaseImport.Core.Models;

public class LeaseCalculationAnswers
{
    public Framework Framework { get; init; }
    public EntitySize EntitySize { get; init; }
    // The question is skipped when there are no leases.
    public HasSubleases? HasSubleases { get; init; }
}
