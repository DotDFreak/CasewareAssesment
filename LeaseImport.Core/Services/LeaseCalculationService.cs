using System.IO;
using System.Text.Json;
using Jint;
using LeaseImport.Core.Models;

namespace LeaseImport.Core.Services;

public class LeaseCalculationService
{
    public LeaseCalculationResult Calculate(ImportResult importResult, LeaseCalculationAnswers answers)
    {
        ArgumentNullException.ThrowIfNull(importResult);
        ArgumentNullException.ThrowIfNull(answers);
        if (!Enum.IsDefined(answers.Framework) || !Enum.IsDefined(answers.EntitySize) ||
            (answers.HasSubleases.HasValue && !Enum.IsDefined(answers.HasSubleases.Value)))
            throw new ArgumentException("Choose valid calculation answers.", nameof(answers));
        if (importResult.Leases.Count > 0 && !answers.HasSubleases.HasValue)
            throw new ArgumentException("Answer the subleases question when there are leases.", nameof(answers));

        string importJson = new JsonExportService().Export(importResult);
        string answersJson = JsonSerializer.Serialize(new
        {
            Framework = answers.Framework.ToString(),
            EntitySize = answers.EntitySize.ToString(),
            HasSubleases = importResult.Leases.Count == 0 ? "" : answers.HasSubleases!.Value.ToString()
        });

        using var stream = typeof(LeaseCalculationService).Assembly
            .GetManifestResourceStream("LeaseImport.Core.Scripts.LeaseCalculator.js")
            ?? throw new InvalidOperationException("The lease calculator script could not be loaded.");
        using var reader = new StreamReader(stream);

        // Each calculation owns its engine; no JavaScript objects cross threads.
        var engine = new Engine(options => options.TimeoutInterval(TimeSpan.FromSeconds(5)));
        engine.Execute(reader.ReadToEnd());
        engine.Execute("""
            var JsonParser = { parse: JSON.parse };
            function calculateFromJson(importJson, answersJson) {
                return JSON.stringify(calculateLeases(importJson, JSON.parse(answersJson)));
            }
            """);
        string resultJson = engine.Invoke("calculateFromJson", importJson, answersJson).AsString();
        return JsonSerializer.Deserialize<LeaseCalculationResult>(resultJson)
            ?? throw new InvalidOperationException("The calculator did not return a result.");
    }
}
