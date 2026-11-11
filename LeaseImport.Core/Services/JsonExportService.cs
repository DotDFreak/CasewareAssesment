using System;
using System.Text.Json;
using System.IO;
using LeaseImport.Core.Models;

namespace LeaseImport.Core.Services
{
    public class JsonExportService
    {
        public string Export(ImportResult importResult)
        {
            ArgumentNullException.ThrowIfNull(importResult);
            return JsonSerializer.Serialize(importResult, new JsonSerializerOptions
            {
                WriteIndented = true
            });
        }

        public void ExportToFile(ImportResult importResult, string filePath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
            File.WriteAllText(filePath, Export(importResult));
        }
    }
}
