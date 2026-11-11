using System.Globalization;
using LeaseImport.Core.Models;
using LeaseImport.Core.Services;

namespace LeaseImport.Tests;

public class CsvImportServiceTests
{
    private const string Header = "LeaseId,AssetClass,Description,Currency,Opening,Additions,Depreciation,Disposals";
    private string filePath = null!;

    [SetUp]
    public void SetUp() => filePath = Path.Combine(Path.GetTempPath(), $"leases-{Guid.NewGuid()}.csv");

    [TearDown]
    public void TearDown() => File.Delete(filePath);

    private ImportResult Import(string csv)
    {
        File.WriteAllText(filePath, csv);
        return new CsvImportService().Import(filePath);
    }

    [Test]
    public void ImportsMixedCurrenciesAndQuotedFieldsUsingInvariantAmounts()
    {
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ro-RO");
            var result = Import(Header + "\nL-1,Property,\"Office, \"\"West\"\"\",DKK,1250000.50,0,125000.05,0\n" +
                "L-2,Equipment,Printer,EUR,-20.25,0,5,0");
            Assert.That(result.Issues, Is.Empty);
            Assert.That(result.Leases, Has.Count.EqualTo(2));
            Assert.That(result.Leases[0].Description, Is.EqualTo("Office, \"West\""));
            Assert.That(result.Leases[0].Opening, Is.EqualTo(1250000.50m));
            Assert.That(result.Leases[0].Depreciation, Is.EqualTo(125000.05m));
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    [Test]
    public void LogsEachInvalidFieldAndContinuesWithValidRows()
    {
        var result = Import(Header + "\n,Property,Office,dkk,invalid,0,999999999999999999999999999999999,0\n" +
            "L-2,Equipment,Printer,DKK,20,0,5,0");
        Assert.That(result.Leases.Select(l => l.LeaseId), Is.EqualTo(new[] { "L-2" }));
        Assert.That(result.Issues.Select(i => i.Field), Is.EquivalentTo(new[] { "LeaseId", "Currency", "Opening", "Depreciation" }));
        Assert.That(result.Issues.All(i => i.LineNumber == 2 && i.Message.Length > 0), Is.True);
    }

    [Test]
    public void RejectsDuplicateLeaseIds()
    {
        var result = Import(Header + "\nL-1,Property,Office,DKK,1,0,0,0\nl-1,Property,Office,DKK,2,0,0,0");
        Assert.That(result.Leases, Has.Count.EqualTo(1));
        Assert.That(result.Issues.Single().Field, Is.EqualTo("LeaseId"));
        Assert.That(result.Issues[0].LineNumber, Is.EqualTo(3));
    }

    [TestCase("\n")]
    [TestCase("\r\n")]
    [TestCase("\r")]
    public void ReportsPhysicalLineAfterBlankAndMultilineRows(string newline)
    {
        var result = Import(
            Header + newline +
            "L-1,Property,\"Office" + newline + "West\",DKK,1,0,0,0" + newline + newline +
            "L-2,Property,Office,DKK,bad,0,0,0");

        Assert.That(result.Leases, Has.Count.EqualTo(1));
        Assert.That(result.Issues, Has.Count.EqualTo(1));
        Assert.That(result.Issues.Single().LineNumber, Is.EqualTo(5));
    }

    [Test]
    public void ReportsActualHeaderLineAfterLeadingBlankLines()
    {
        var result = Import("\n \t\nLeaseId,AssetClass");

        Assert.That(result.Leases, Is.Empty);
        Assert.That(result.Issues, Is.Not.Empty);
        Assert.That(result.Issues.All(issue => issue.LineNumber == 3), Is.True);
    }

    [Test]
    public void ReportsRecordStartForInvalidMultilineRowAndFollowingRow()
    {
        var result = Import("\n" + Header + "\n\n" +
            "L-1,Property,\"Office\n\nWest\",DKK,bad,0,0,0\n \t\n" +
            "L-2,Property,Office,DKK,bad,0,0,0\n");

        Assert.That(result.Leases, Is.Empty);
        Assert.That(result.Issues.Select(issue => issue.LineNumber), Is.EqualTo(new[] { 4, 8 }));
        Assert.That(result.Issues.All(issue => issue.Field == "Opening"), Is.True);
    }

    [Test]
    public void ReportsWrongFieldCountAtItsPhysicalLineAfterBlankLines()
    {
        var result = Import(Header + "\n\n\nL-1,Property\n");

        Assert.That(result.Leases, Is.Empty);
        Assert.That(result.Issues.Single().Field, Is.EqualTo("Row"));
        Assert.That(result.Issues[0].LineNumber, Is.EqualTo(4));
    }

    [Test]
    public void ReportsMalformedRowAndContinuesAtCorrectPhysicalLine()
    {
        var result = Import(Header + "\n\nL-1,Property,\"Office\"junk,DKK,1,0,0,0\n\n" +
            "L-2,Property,Office,DKK,bad,0,0,0\nL-3,Property,Office,DKK,1,0,0,0");

        Assert.That(result.Leases.Select(lease => lease.LeaseId), Is.EqualTo(new[] { "L-3" }));
        Assert.That(result.Issues.Select(issue => issue.LineNumber), Is.EqualTo(new[] { 3, 5 }));
        Assert.That(result.Issues.Select(issue => issue.Field), Is.EqualTo(new[] { "Row", "Opening" }));
    }

    [Test]
    public void ReportsMalformedHeaderAfterLeadingBlankLines()
    {
        var result = Import("\n \t\n\"LeaseId\"junk,AssetClass");

        Assert.That(result.Leases, Is.Empty);
        Assert.That(result.Issues.Single().Field, Is.EqualTo("Header"));
        Assert.That(result.Issues[0].LineNumber, Is.EqualTo(3));
    }

    [TestCase("")]
    [TestCase("\n")]
    public void IgnoresTrailingBlankLinesAfterFinalValidRecord(string finalNewline)
    {
        var result = Import(Header + "\nL-1,Property,Office,DKK,1,0,0,0\n \t\n" + finalNewline);

        Assert.That(result.Leases, Has.Count.EqualTo(1));
        Assert.That(result.Issues, Is.Empty);
    }

    [Test]
    public void RecoversAfterMalformedQuotedRow()
    {
        var result = Import(Header + "\nL-1,Property,\"Office\"junk,DKK,1,0,0,0\nL-2,Property,Office,DKK,1,0,0,0");
        Assert.That(result.Leases.Select(l => l.LeaseId), Is.EqualTo(new[] { "L-2" }));
        Assert.That(result.Issues.Single().LineNumber, Is.EqualTo(2));
    }

    [TestCase("")]
    [TestCase("LeaseId,AssetClass")]
    [TestCase("LeaseId,AssetClass,Description,Currency,Opening,Additions,Depreciation,LeaseId")]
    [TestCase("\"LeaseId\"junk,AssetClass")]
    public void RejectsInvalidHeaders(string csv)
    {
        var result = Import(csv);
        Assert.That(result.Leases, Is.Empty);
        Assert.That(result.Issues, Is.Not.Empty);
    }

    [Test]
    public void AcceptsReorderedHeaders()
    {
        var result = Import("Currency,LeaseId,Description,AssetClass,Disposals,Depreciation,Additions,Opening\nDKK,L-1,Office,Property,0,5,0,10");
        Assert.That(result.Issues, Is.Empty);
        Assert.That(result.Leases.Single().Opening, Is.EqualTo(10m));
    }

    [Test]
    public void RejectsWrongFieldCountAndContinues()
    {
        var result = Import(Header + "\nL-1,Property\nL-2,Property,Office,DKK,1,0,0,0");
        Assert.That(result.Leases, Has.Count.EqualTo(1));
        Assert.That(result.Issues.Single().Field, Is.EqualTo("Row"));
    }

    [Test]
    public void ReturnsEmptyCollectionsForHeaderOnlyFile()
    {
        var result = Import(Header);
        Assert.That(result.Leases, Is.Empty);
        Assert.That(result.Issues, Is.Empty);
    }

    [Test]
    public void LogsMissingFile()
    {
        var result = new CsvImportService().Import(filePath);
        Assert.That(result.Leases, Is.Empty);
        Assert.That(result.Issues.Single().Field, Is.EqualTo("File"));
        Assert.That(result.Issues[0].LineNumber, Is.Zero);
    }

    [TestCase("")]
    [TestCase(" ")]
    public void LogsEmptyFilePath(string path)
    {
        Assert.That(new CsvImportService().Import(path).Issues.Single().Field, Is.EqualTo("File"));
    }
}
