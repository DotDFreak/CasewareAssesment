using LeaseImport.Core.Models;
using Microsoft.VisualBasic.FileIO;
using System.Globalization;
using System.IO;
using System.Text;

namespace LeaseImport.Core.Services
{
    public class CsvImportService
    {
        public ImportResult Import(string filePath)
        {
            var result = new ImportResult();
            string[] columns = ["LeaseId", "AssetClass", "Description", "Currency",
                "Opening", "Additions", "Depreciation", "Disposals"];

            if (string.IsNullOrWhiteSpace(filePath))
            {
                AddProblem(result, 0, "File", "A CSV file path is required.");
                return result;
            }

            try
            {
                // Use a CSV parser so quoted commas, escaped quotes and multiline fields stay intact.
                using var parser = new TextFieldParser(filePath, Encoding.UTF8);
                parser.TextFieldType = FieldType.Delimited;
                parser.SetDelimiters(",");
                parser.HasFieldsEnclosedInQuotes = true;
                parser.TrimWhiteSpace = true;

                // The parser skips blank lines without updating LineNumber first.
                // A second streaming reader locates each record's first physical line,
                // including skipped blanks, without buffering the entire file.
                using var physicalLines = File.ReadLines(filePath, Encoding.UTF8).GetEnumerator();
                int physicalLineNumber = 0;

                int GetRecordLineNumber()
                {
                    long parserLineNumber = parser.LineNumber;
                    while (physicalLines.MoveNext())
                    {
                        physicalLineNumber++;
                        if (physicalLineNumber >= parserLineNumber &&
                            !string.IsNullOrWhiteSpace(physicalLines.Current))
                            return physicalLineNumber;
                    }

                    return physicalLineNumber;
                }

                int headerLineNumber = GetRecordLineNumber();

                string[]? header;
                try
                {
                    header = parser.ReadFields();
                }
                catch (MalformedLineException)
                {
                    AddProblem(result, (int)parser.ErrorLineNumber, "Header", "Malformed CSV header.");
                    return result;
                }

                if (header == null)
                {
                    AddProblem(result, 1, "Header", "The CSV file is empty.");
                    return result;
                }

                // Map names to positions so column order and header casing may vary.
                var indexes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < header.Length; i++)
                {
                    if (!indexes.TryAdd(header[i], i))
                        AddProblem(result, headerLineNumber, "Header", $"Duplicate column '{header[i]}'.");
                }

                foreach (string column in columns)
                {
                    if (!indexes.ContainsKey(column))
                        AddProblem(result, headerLineNumber, column, "Required column is missing.");
                }

                if (header.Length != columns.Length)
                    AddProblem(result, headerLineNumber, "Header", $"Expected {columns.Length} columns; found {header.Length}.");

                // An invalid header makes row-to-field mapping unreliable; stop this import.
                if (result.Issues.Count > 0)
                    return result;

                // IDs are compared without case; only accepted rows reserve an ID.
                var leaseIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                while (!parser.EndOfData)
                {
                    int lineNumber = GetRecordLineNumber();

                    string[]? fields;
                    try
                    {
                        fields = parser.ReadFields();
                    }
                    catch (MalformedLineException)
                    {
                        // The parser supplies the malformed record's starting physical line.
                        AddProblem(result, (int)parser.ErrorLineNumber, "Row", "Malformed CSV row.");
                        continue;
                    }

                    if (fields == null)
                        break;

                    if (fields.Length != header.Length)
                    {
                        // Do not index fields when a row does not match the header's shape.
                        AddProblem(result, lineNumber, "Row", $"Expected {header.Length} fields; found {fields.Length}.");
                        continue;
                    }

                    // Collect all field problems for this row before deciding whether to accept it.
                    int issueCount = result.Issues.Count;
                    foreach (string column in columns)
                    {
                        if (string.IsNullOrWhiteSpace(fields[indexes[column]]))
                            AddProblem(result, lineNumber, column, "A value is required.");
                    }

                    string leaseId = fields[indexes["LeaseId"]];
                    if (leaseIds.Contains(leaseId))
                        AddProblem(result, lineNumber, "LeaseId", $"Duplicate lease ID '{leaseId}'.");

                    // Validate code format only. Mixed currencies are allowed and handled by the script.
                    string currency = fields[indexes["Currency"]];
                    if (currency.Length > 0 &&
                        (currency.Length != 3 || currency.Any(c => c < 'A' || c > 'Z')))
                        AddProblem(result, lineNumber, "Currency", "Expected a three-letter uppercase currency code (for example, DKK).");

                    // Match the CSV's dot-decimal format regardless of the machine's culture.
                    // Keep negative values and fractional cents; rounding belongs to the calculations.
                    var amounts = new decimal[4];
                    for (int i = 0; i < amounts.Length; i++)
                    {
                        string column = columns[i + 4];
                        string value = fields[indexes[column]];
                        if (!string.IsNullOrWhiteSpace(value) && !decimal.TryParse(value,
                            NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                            CultureInfo.InvariantCulture, out amounts[i]))
                            AddProblem(result, lineNumber, column, "Expected a decimal amount using a dot as the decimal separator, within the decimal range.");
                    }

                    // Reject the whole row if any field failed, then continue with later records.
                    if (result.Issues.Count != issueCount)
                        continue;

                    result.Leases.Add(new Lease
                    {
                        LeaseId = leaseId,
                        AssetClass = fields[indexes["AssetClass"]],
                        Description = fields[indexes["Description"]],
                        Currency = currency,
                        Opening = amounts[0],
                        Additions = amounts[1],
                        Depreciation = amounts[2],
                        Disposals = amounts[3]
                    });
                    leaseIds.Add(leaseId);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                // Line 0 identifies a file-level problem rather than a particular CSV record.
                AddProblem(result, 0, "File", $"Unable to read the CSV file: {ex.Message}");
            }

            return result;
        }

        private static void AddProblem(ImportResult result, int lineNumber, string field, string message)
        {
            result.Issues.Add(new ValidationIssue
            {
                LineNumber = lineNumber,
                Field = field,
                Message = message
            });
        }
    }
}
