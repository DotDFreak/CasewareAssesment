using LeaseImport.Core.Models;
using LeaseImport.Core.Services;

namespace LeaseImport.Tests;

public class LeaseCalculationTests
{
    private readonly LeaseCalculationService service = new();

    private static ImportResult CreateImport(string secondCurrency = "DKK") => new()
    {
        Leases =
        [
            new Lease
            {
                LeaseId = "L-1", AssetClass = "Property", Description = "Office", Currency = "DKK",
                Opening = 1000m, Additions = 100m, Depreciation = 200m, Disposals = 0m
            },
            new Lease
            {
                LeaseId = "L-2", AssetClass = "Equipment", Description = "Printer", Currency = secondCurrency,
                Opening = 500m, Additions = 0m, Depreciation = 50m, Disposals = 0m
            }
        ],
        Issues = [new ValidationIssue { LineNumber = 4, Field = "Opening", Message = "Rejected row." }]
    };

    private static LeaseCalculationAnswers Answers(Framework framework = Framework.IFRS,
        EntitySize size = EntitySize.Medium, HasSubleases? subleases = HasSubleases.No) => new()
    {
        Framework = framework,
        EntitySize = size,
        HasSubleases = subleases
    };

    [Test]
    public void RunsEmbeddedScriptWithSerializedImportAndEnumAnswers()
    {
        var imported = CreateImport();
        var result = service.Calculate(imported, Answers());

        Assert.That(result.LeaseCount, Is.EqualTo(2));
        Assert.That(result.HasLeases, Is.EqualTo("Yes"));
        Assert.That(result.MixedCurrency, Is.EqualTo("No"));
        Assert.That(result.TotalClosing, Is.EqualTo(1350m));
        Assert.That(result.AverageClosing, Is.EqualTo(675m));
        Assert.That(result.ShowRouDisclosure, Is.EqualTo("Yes"));
        Assert.That(result.ShowMaturityAnalysis, Is.EqualTo("Yes"));
        Assert.That(result.ShowSubleaseDisclosure, Is.EqualTo("No"));
        Assert.That(result.SummaryRequired, Is.Null);
        Assert.That(result.ShowCashOutflow, Is.Null);
        Assert.That(imported.Leases, Has.Count.EqualTo(2));
        Assert.That(imported.Issues, Has.Count.EqualTo(1));
    }

    [Test]
    public void MixedCurrenciesLeaveClosingAmountsUnavailable()
    {
        var result = service.Calculate(CreateImport("EUR"), Answers());
        Assert.That(result.MixedCurrency, Is.EqualTo("Yes"));
        Assert.That(result.TotalClosing, Is.Null);
        Assert.That(result.AverageClosing, Is.Null);
    }

    [TestCase(Framework.IFRS, EntitySize.Micro, "Yes", "Yes")]
    [TestCase(Framework.LocalGAAP, EntitySize.Small, "No", "No")]
    [TestCase(Framework.LocalGAAP, EntitySize.Large, "No", "Yes")]
    public void EnumAnswersDriveDisclosureFields(Framework framework, EntitySize size,
        string expectedRou, string expectedMaturity)
    {
        var result = service.Calculate(CreateImport(), Answers(framework, size, HasSubleases.Yes));
        Assert.That(result.ShowRouDisclosure, Is.EqualTo(expectedRou));
        Assert.That(result.ShowMaturityAnalysis, Is.EqualTo(expectedMaturity));
        Assert.That(result.ShowSubleaseDisclosure, Is.EqualTo("No"));
    }

    [Test]
    public void EmptyImportSkipsSubleasesAndPreservesScriptEmptyResults()
    {
        var result = service.Calculate(new ImportResult(), Answers(subleases: null));
        Assert.That(result.LeaseCount, Is.Zero);
        Assert.That(result.HasLeases, Is.EqualTo("No"));
        Assert.That(result.MixedCurrency, Is.EqualTo("No"));
        Assert.That(result.TotalClosing, Is.EqualTo(0m));
        Assert.That(result.AverageClosing, Is.Null);
        Assert.That(result.ShowRouDisclosure, Is.EqualTo("No"));
        Assert.That(result.ShowSubleaseDisclosure, Is.EqualTo("No"));
    }

    [Test]
    public void EmptyImportIgnoresPreviouslySelectedSubleasesAnswer()
    {
        var result = service.Calculate(new ImportResult(), Answers(subleases: HasSubleases.Yes));
        Assert.That(result.ShowSubleaseDisclosure, Is.EqualTo("No"));
    }

    [TestCase("1.125", "1.13")]
    [TestCase("-1.125", "-1.13")]
    [TestCase("1.005", "1.01")]
    [TestCase("-1.005", "-1.01")]
    [TestCase("1.015", "1.02")]
    [TestCase("-1.015", "-1.02")]
    [TestCase("0", "0")]
    [TestCase("0.004", "0")]
    [TestCase("-0.004", "0")]
    [TestCase("1.0049999", "1.00")]
    [TestCase("-1.0049999", "-1.00")]
    [TestCase("1.0050001", "1.01")]
    [TestCase("-1.0050001", "-1.01")]
    [TestCase("0.0000001", "0")]
    [TestCase("-0.0000001", "0")]
    public void ScriptRoundsPositiveAndNegativeAmountsAwayFromZero(string amount, string expected)
    {
        var imported = CreateImport();
        imported.Leases.RemoveAt(1);
        var lease = imported.Leases[0];
        lease.Opening = decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture);
        lease.Additions = lease.Depreciation = lease.Disposals = 0m;

        var result = service.Calculate(imported, Answers());

        Assert.That(result.TotalClosing,
            Is.EqualTo(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture)));
        Assert.That(result.AverageClosing, Is.EqualTo(result.TotalClosing));
    }

    [TestCase("2.01", "1.01")]
    [TestCase("-2.01", "-1.01")]
    public void RoundsAverageHalfwayValuesAwayFromZero(string total, string expectedAverage)
    {
        var imported = CreateImport();
        foreach (var lease in imported.Leases)
            lease.Opening = lease.Additions = lease.Depreciation = lease.Disposals = 0m;
        imported.Leases[0].Opening = decimal.Parse(total, System.Globalization.CultureInfo.InvariantCulture);

        var result = service.Calculate(imported, Answers());

        Assert.That(result.TotalClosing,
            Is.EqualTo(decimal.Parse(total, System.Globalization.CultureInfo.InvariantCulture)));
        Assert.That(result.AverageClosing,
            Is.EqualTo(decimal.Parse(expectedAverage, System.Globalization.CultureInfo.InvariantCulture)));
    }

    [TestCase("0.335", "1.01")]
    [TestCase("-0.335", "-1.01")]
    [TestCase("0.004", "0.01")]
    [TestCase("-0.004", "-0.01")]
    public void RoundsAfterSummingLeaseClosingsWithoutRoundingIndividualAmounts(string opening, string expectedTotal)
    {
        var imported = CreateImport();
        imported.Leases.Add(new Lease
        {
            LeaseId = "L-3", AssetClass = "Equipment", Description = "Scanner", Currency = "DKK"
        });
        foreach (var lease in imported.Leases)
        {
            lease.Opening = decimal.Parse(opening, System.Globalization.CultureInfo.InvariantCulture);
            lease.Additions = 0.1m;
            lease.Depreciation = 0.05m;
            lease.Disposals = 0.05m;
        }

        var result = service.Calculate(imported, Answers());

        Assert.That(result.TotalClosing,
            Is.EqualTo(decimal.Parse(expectedTotal, System.Globalization.CultureInfo.InvariantCulture)));
    }

    [Test]
    public void FloatingPointResidueFromAdditionAndSubtractionRoundsToZero()
    {
        var imported = CreateImport();
        imported.Leases.RemoveAt(1);
        var lease = imported.Leases[0];
        lease.Opening = 0.1m;
        lease.Additions = 0.2m;
        lease.Depreciation = 0.3m;
        lease.Disposals = 0m;

        var result = service.Calculate(imported, Answers());

        Assert.That(result.TotalClosing, Is.EqualTo(0m));
        Assert.That(result.AverageClosing, Is.EqualTo(0m));
    }

    [Test]
    public void RequiresSubleasesAnswerWhenAcceptedLeasesExist()
    {
        Assert.Throws<ArgumentException>(() => service.Calculate(CreateImport(), Answers(subleases: null)));
    }

    [Test]
    public void RejectsNullArguments()
    {
        Assert.Throws<ArgumentNullException>(() => service.Calculate(null!, Answers()));
        Assert.Throws<ArgumentNullException>(() => service.Calculate(CreateImport(), null!));
    }

    [Test]
    public void RejectsUndefinedEnumValues()
    {
        Assert.Throws<ArgumentException>(() => service.Calculate(CreateImport(), Answers(framework: (Framework)99)));
        Assert.Throws<ArgumentException>(() => service.Calculate(CreateImport(), Answers(size: (EntitySize)99)));
        Assert.Throws<ArgumentException>(() => service.Calculate(CreateImport(), Answers(subleases: (HasSubleases)99)));
    }

    [Test]
    public void RepeatedCalculationsDoNotReusePreviousEngineState()
    {
        service.Calculate(CreateImport("EUR"), Answers(Framework.LocalGAAP, EntitySize.Small));
        var result = service.Calculate(CreateImport(), Answers());
        Assert.That(result.MixedCurrency, Is.EqualTo("No"));
        Assert.That(result.TotalClosing, Is.EqualTo(1350m));
        Assert.That(result.ShowRouDisclosure, Is.EqualTo("Yes"));
    }
}
