using System.Globalization;
using System.Text.Json;
using LeaseImport.Core.Models;
using LeaseImport.Core.Services;

namespace LeaseImport.Tests;

public class JsonExportTests
{
    private readonly JsonExportService service = new();
    private string directoryPath = null!;
    private string filePath = null!;

    [SetUp]
    public void SetUp()
    {
        directoryPath = Path.Combine(Path.GetTempPath(), $"json-export-{Guid.NewGuid()}");
        Directory.CreateDirectory(directoryPath);
        filePath = Path.Combine(directoryPath, "leases.json");
    }

    [TearDown]
    public void TearDown()
    {
        File.Delete(filePath);
        Directory.Delete(directoryPath);
    }

    private static ImportResult CreateResult() => new()
    {
        Leases =
        [
            new Lease
            {
                LeaseId = "L-2001",
                AssetClass = "Property",
                Description = "Office Aarhus",
                Currency = "DKK",
                Opening = 1250000.50m,
                Additions = 123.456m,
                Depreciation = 125000.05m,
                Disposals = -10.25m
            },
            new Lease
            {
                LeaseId = "L-2002",
                AssetClass = "Equipment",
                Description = "Printer",
                Currency = "EUR",
                Opening = 0m,
                Additions = 0m,
                Depreciation = 0m,
                Disposals = 0m
            }
        ],
        Issues =
        [
            new ValidationIssue
            {
                LineNumber = 4,
                Field = "Opening",
                Message = "Invalid decimal amount."
            },
            new ValidationIssue
            {
                LineNumber = 5,
                Field = "LeaseId",
                Message = "Duplicate lease ID."
            }
        ]
    };

    [Test]
    public void ExportPreservesLeaseFieldsAndMultipleRecords()
    {
        using var document = JsonDocument.Parse(service.Export(CreateResult()));
        var leases = document.RootElement.GetProperty("Leases");

        Assert.That(leases.GetArrayLength(), Is.EqualTo(2));
        var lease = leases[0];
        Assert.That(lease.GetProperty("LeaseId").GetString(), Is.EqualTo("L-2001"));
        Assert.That(lease.GetProperty("AssetClass").GetString(), Is.EqualTo("Property"));
        Assert.That(lease.GetProperty("Description").GetString(), Is.EqualTo("Office Aarhus"));
        Assert.That(lease.GetProperty("Currency").GetString(), Is.EqualTo("DKK"));
        Assert.That(lease.GetProperty("Opening").GetDecimal(), Is.EqualTo(1250000.50m));
        Assert.That(lease.GetProperty("Additions").GetDecimal(), Is.EqualTo(123.456m));
        Assert.That(lease.GetProperty("Depreciation").GetDecimal(), Is.EqualTo(125000.05m));
        Assert.That(lease.GetProperty("Disposals").GetDecimal(), Is.EqualTo(-10.25m));
        Assert.That(lease.TryGetProperty("Deprecation", out _), Is.False);
        Assert.That(leases[1].GetProperty("LeaseId").GetString(), Is.EqualTo("L-2002"));
        Assert.That(leases[1].GetProperty("Currency").GetString(), Is.EqualTo("EUR"));
        Assert.That(leases[1].GetProperty("Opening").GetDecimal(), Is.Zero);
    }

    [Test]
    public void ExportPreservesValidationIssues()
    {
        using var document = JsonDocument.Parse(service.Export(CreateResult()));
        var issues = document.RootElement.GetProperty("Issues");

        Assert.That(issues.GetArrayLength(), Is.EqualTo(2));
        Assert.That(issues[0].GetProperty("LineNumber").GetInt32(), Is.EqualTo(4));
        Assert.That(issues[0].GetProperty("Field").GetString(), Is.EqualTo("Opening"));
        Assert.That(issues[0].GetProperty("Message").GetString(), Is.EqualTo("Invalid decimal amount."));
        Assert.That(issues[1].GetProperty("LineNumber").GetInt32(), Is.EqualTo(5));
        Assert.That(issues[1].GetProperty("Field").GetString(), Is.EqualTo("LeaseId"));
        Assert.That(issues[1].GetProperty("Message").GetString(), Is.EqualTo("Duplicate lease ID."));
    }

    [Test]
    public void ExportEmptyResultProducesEmptyArrays()
    {
        using var document = JsonDocument.Parse(service.Export(new ImportResult()));
        Assert.That(document.RootElement.GetProperty("Leases").GetArrayLength(), Is.Zero);
        Assert.That(document.RootElement.GetProperty("Issues").GetArrayLength(), Is.Zero);
    }

    [Test]
    public void ExportKeepsIssuesWhenThereAreNoAcceptedLeases()
    {
        var result = CreateResult();
        result.Leases.Clear();

        using var document = JsonDocument.Parse(service.Export(result));
        Assert.That(document.RootElement.GetProperty("Leases").GetArrayLength(), Is.Zero);
        Assert.That(document.RootElement.GetProperty("Issues").GetArrayLength(), Is.EqualTo(2));
    }

    [Test]
    public void ExportPreservesUnicodeAndEscapesSpecialCharacters()
    {
        const string text = "București / Århus \"West\" \\ storage\nFloor 2\t€";
        var result = CreateResult();
        result.Leases[0].Description = text;
        result.Issues[0].Message = text;

        using var document = JsonDocument.Parse(service.Export(result));
        Assert.That(document.RootElement.GetProperty("Leases")[0].GetProperty("Description").GetString(), Is.EqualTo(text));
        Assert.That(document.RootElement.GetProperty("Issues")[0].GetProperty("Message").GetString(), Is.EqualTo(text));
    }

    [Test]
    public void ExportAmountsRemainJsonNumbersUnderCommaDecimalCulture()
    {
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ro-RO");
            using var document = JsonDocument.Parse(service.Export(CreateResult()));
            var amount = document.RootElement.GetProperty("Leases")[0].GetProperty("Additions");
            Assert.That(amount.ValueKind, Is.EqualTo(JsonValueKind.Number));
            Assert.That(amount.GetDecimal(), Is.EqualTo(123.456m));
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    [Test]
    public void ExportRejectsNullResult()
    {
        Assert.Throws<ArgumentNullException>(() => service.Export(null!));
    }

    [Test]
    public void ExportToFileCreatesReadableJsonContainingLeasesAndIssues()
    {
        var result = CreateResult();
        result.Leases[0].Description = "Birou București – Århus";

        service.ExportToFile(result, filePath);

        using var document = JsonDocument.Parse(File.ReadAllText(filePath));
        Assert.That(document.RootElement.GetProperty("Leases").GetArrayLength(), Is.EqualTo(2));
        Assert.That(document.RootElement.GetProperty("Issues").GetArrayLength(), Is.EqualTo(2));
        Assert.That(document.RootElement.GetProperty("Leases")[0].GetProperty("Description").GetString(),
            Is.EqualTo("Birou București – Århus"));
    }

    [Test]
    public void ExportToFileReplacesExistingContentWithoutLeavingTrailingData()
    {
        File.WriteAllText(filePath, new string('x', 10000));

        service.ExportToFile(new ImportResult(), filePath);

        using var document = JsonDocument.Parse(File.ReadAllText(filePath));
        Assert.That(document.RootElement.GetProperty("Leases").GetArrayLength(), Is.Zero);
        Assert.That(document.RootElement.GetProperty("Issues").GetArrayLength(), Is.Zero);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void ExportToFileRejectsMissingPath(string? path)
    {
        Assert.That(() => service.ExportToFile(CreateResult(), path!), Throws.InstanceOf<ArgumentException>());
    }

    [Test]
    public void ExportToFileRejectsNullResultWithoutOverwritingExistingFile()
    {
        File.WriteAllText(filePath, "Existing content");

        Assert.Throws<ArgumentNullException>(() => service.ExportToFile(null!, filePath));

        Assert.That(File.ReadAllText(filePath), Is.EqualTo("Existing content"));
    }

    [Test]
    public void ExportToFileReportsMissingDestinationDirectory()
    {
        string missingPath = Path.Combine(directoryPath, "missing", "leases.json");

        Assert.Throws<DirectoryNotFoundException>(() => service.ExportToFile(CreateResult(), missingPath));
        Assert.That(File.Exists(missingPath), Is.False);
    }
}
