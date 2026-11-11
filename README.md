# Lease Import Assessment

The solution imports and validates lease CSV data in C#, exports accepted leases and problems as JSON, and calculates the assessment's fields in classic JScript. WPF is an optional interface for trying this flow.

## Build and run tests

Requirements: Windows, the .NET 10 SDK, and Node.js. Run these commands from the repository root:

```powershell
dotnet build .\LeaseImport.WPF\LeaseImport.WPF.slnx
dotnet test .\LeaseImport.WPF\LeaseImport.Tests\LeaseImport.Tests.csproj
node --test .\LeaseImport.WPF\JScript\LeaseCalculatorTests.js
```

For an editable demonstration with sample data:

```powershell
node .\LeaseImport.WPF\TestRunner.js
```

## CSV import and JSON export

`CsvImportService.Import(filePath)` returns an `ImportResult`. The expected UTF-8, comma-delimited columns are:

```text
LeaseId,AssetClass,Description,Currency,Opening,Additions,Depreciation,Disposals
```

Header matching is case-insensitive and allows reordered columns. Missing, duplicate or extra columns stop the import. Quoted commas, escaped quotes and multiline descriptions are supported; blank lines are ignored and surrounding field whitespace is trimmed.

Every field is required. Rows are rejected for incorrect field counts, malformed CSV, duplicate accepted lease IDs, invalid currency format or invalid decimal amounts. IDs are compared case-insensitively. All detectable field problems are collected before rejecting a row; later records are still processed where the CSV parser can recover.

Amounts use a dot as the decimal separator, without thousands separators or exponent notation. Negative values and fractional cents are retained. Currency validation checks three uppercase letters only; mixed currencies are accepted.

`ImportResult.Leases` contains accepted records. `ImportResult.Issues` contains `LineNumber`, `Field` and `Message`. Line numbers refer to physical lines, including blanks; multiline records use their starting line. File-level failures use line 0.

`JsonExportService.Export(result)` returns formatted JSON with the same `Leases` and `Issues` structure. `ExportToFile(result, path)` writes that JSON to a file.

## JScript calculations

`LeaseCalculator.js` exposes `calculateLeases(jsonText, answers)`. The host supplies `JsonParser.parse(text)`. The answers are:

- `Framework`: `IFRS` or `LocalGAAP`.
- `EntitySize`: `Micro`, `Small`, `Medium` or `Large`.
- `HasSubleases`: `Yes`, `No`, or blank when there are no leases.

The calculator combines these answers with accepted leases and returns all ten specified fields. Yes/No fields are strings. Closing amounts are numeric and rounded to two decimal places, halfway away from zero. Individual input amounts are not rounded before summing. Mixed currencies make both closing amounts null. With no leases, total closing is zero and average closing is null.

## Specification decisions and limitations

| Item | Handling |
| --- | --- |
| F-04 refers to undefined `KpiRequired` | `SummaryRequired` is null; no input is invented. |
| F-08 mixes AND and OR without grouping | Interpreted as `(HasLeases AND IFRS) OR Large`; a large entity shows maturity analysis even without leases. |
| F-09 requires subleases but no leases | The script preserves the literal condition. Normally the question is skipped for no leases, so this disclosure stays hidden. Direct tests also cover contradictory answers. |
| F-10 refers to undefined `TotalCashOutflow` | `ShowCashOutflow` is null. |
| F-06 divides by lease count | Uses the rounded `TotalClosing` field; returns null for zero leases or mixed currencies. |

C# amounts use `decimal`; JavaScript uses binary floating-point numbers. Decimal-shift rounding handles the tested halfway cases, but does not provide arbitrary-precision arithmetic. Extremely large or highly precise amounts can lose precision, and results outside the C# decimal range cannot be converted back. No additional amount limits are imposed.

The calculator uses ES3 syntax. Tests run under Node and Jint; they do not establish execution compatibility with Microsoft's classic JScript engine itself.

## Test coverage and optional interface

NUnit tests cover CSV validation and physical line locations, JSON content and file export, and Jint integration. The separate Node suite calls the actual calculator with hand-written JSON and answers, covering disclosures, empty and mixed-currency inputs, arithmetic, rounding and malformed JSON. Failed assertions produce a nonzero exit code.

The optional WPF app provides CSV selection, JSON export, answer selection and calculation results. Its Jint adapter supplies the JSON parser, embeds the existing script, and skips the subleases question when no accepted leases exist.

## AI usage

AI assisted with implementation, tests and review. Decisions and corrections are recorded in the chat history, including simplifying numeric handling and keeping WPF optional. Include that history with the assessment submission, as requested by the brief. Specification interpretation and numeric edge cases require independent review; generated code and passing tests alone are insufficient evidence of correctness.
