using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using LeaseImport.Core.Models;
using LeaseImport.Core.Services;
using Microsoft.Win32;

namespace LeaseImport.WPF
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly CsvImportService importService = new();
        private readonly JsonExportService exportService = new();
        private readonly LeaseCalculationService calculationService = new();
        private readonly RelayCommand browseCommand;
        private readonly RelayCommand exportCommand;
        private readonly RelayCommand calculateCommand;
        private ImportResult importResult = new();
        private string selectedFilePath = string.Empty;
        private string statusMessage = "Choose a CSV file to import.";
        private bool isImporting;
        private bool isExporting;
        private bool hasImportResult;
        private bool isCalculating;
        private Framework selectedFramework = Framework.IFRS;
        private EntitySize selectedEntitySize = EntitySize.Medium;
        private HasSubleases selectedHasSubleases = HasSubleases.No;
        private LeaseCalculationResult? calculationResult;

        public MainViewModel()
        {
            browseCommand = new RelayCommand(BrowseAndImport, () => CanEditAnswers);
            exportCommand = new RelayCommand(ExportToFile, () => hasImportResult && CanEditAnswers);
            calculateCommand = new RelayCommand(Calculate, () => hasImportResult && CanEditAnswers);
        }

        public ICommand BrowseCommand => browseCommand;
        public ICommand ExportCommand => exportCommand;
        public ICommand CalculateCommand => calculateCommand;

        public bool CanEditAnswers => !isImporting && !isExporting && !isCalculating;
        public bool CanAnswerSubleases => CanEditAnswers && hasImportResult && ImportResult.Leases.Count > 0;

        public Framework SelectedFramework
        {
            get => selectedFramework;
            set
            {
                if (selectedFramework == value) return;
                selectedFramework = value;
                OnPropertyChanged();
                InvalidateCalculation();
            }
        }

        public EntitySize SelectedEntitySize
        {
            get => selectedEntitySize;
            set
            {
                if (selectedEntitySize == value) return;
                selectedEntitySize = value;
                OnPropertyChanged();
                InvalidateCalculation();
            }
        }

        public HasSubleases SelectedHasSubleases
        {
            get => selectedHasSubleases;
            set
            {
                if (selectedHasSubleases == value) return;
                selectedHasSubleases = value;
                OnPropertyChanged();
                InvalidateCalculation();
            }
        }

        public LeaseCalculationResult? CalculationResult
        {
            get => calculationResult;
            private set
            {
                calculationResult = value;
                OnPropertyChanged();
            }
        }

        public ImportResult ImportResult
        {
            get => importResult;
            private set
            {
                importResult = value;
                hasImportResult = true;
                OnPropertyChanged();
                InvalidateCalculation();
                UpdateCommands();
            }
        }

        public string SelectedFilePath
        {
            get => selectedFilePath;
            private set
            {
                selectedFilePath = value;
                OnPropertyChanged();
            }
        }

        public string StatusMessage
        {
            get => statusMessage;
            private set
            {
                statusMessage = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private async void BrowseAndImport()
        {
            // Prevent overlapping imports, including direct command invocations.
            if (!CanEditAnswers)
                return;

            isImporting = true;
            UpdateCommands();
            try
            {
                var dialog = new OpenFileDialog
                {
                    Title = "Select a lease CSV file",
                    Filter = "CSV files (*.csv)|*.csv",
                    DefaultExt = ".csv",
                    Multiselect = false,
                    CheckFileExists = true,
                    CheckPathExists = true,
                    ValidateNames = true
                };

                if (dialog.ShowDialog(Application.Current.MainWindow) != true)
                    return;

                if (!string.Equals(Path.GetExtension(dialog.FileName), ".csv", StringComparison.OrdinalIgnoreCase))
                {
                    StatusMessage = "Select a file with the .csv extension.";
                    return;
                }

                if (!File.Exists(dialog.FileName))
                {
                    StatusMessage = "The selected file no longer exists. Choose another CSV file.";
                    return;
                }

                SelectedFilePath = dialog.FileName;
                StatusMessage = "Importing CSV file…";
                // Parse off the UI thread so the window stays responsive.
                ImportResult = await Task.Run(() => importService.Import(dialog.FileName));
                StatusMessage = $"{ImportResult.Leases.Count} leases imported. {ImportResult.Issues.Count} issues found.";
            }
            catch (Exception ex)
            {
                // Async command exceptions must be handled on the UI thread.
                StatusMessage = $"Unable to import the selected file: {ex.Message}";
            }
            finally
            {
                isImporting = false;
                UpdateCommands();
            }
        }

        private async void ExportToFile()
        {
            if (!exportCommand.CanExecute(null))
                return;

            isExporting = true;
            UpdateCommands();
            try
            {
                var dialog = new SaveFileDialog
                {
                    Title = "Export import result as JSON",
                    Filter = "JSON files (*.json)|*.json",
                    DefaultExt = ".json",
                    AddExtension = true,
                    FileName = Path.GetFileNameWithoutExtension(SelectedFilePath) + ".json",
                    CheckPathExists = true,
                    ValidateNames = true,
                    OverwritePrompt = true
                };

                if (dialog.ShowDialog(Application.Current.MainWindow) != true)
                    return;

                if (!string.Equals(Path.GetExtension(dialog.FileName), ".json", StringComparison.OrdinalIgnoreCase))
                {
                    StatusMessage = "Choose a file name with the .json extension.";
                    return;
                }

                string filePath = dialog.FileName;
                StatusMessage = "Exporting JSON file…";
                await Task.Run(() => exportService.ExportToFile(ImportResult, filePath));
                StatusMessage = $"JSON exported to {filePath}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Unable to export the JSON file: {ex.Message}";
            }
            finally
            {
                isExporting = false;
                UpdateCommands();
            }
        }

        private async void Calculate()
        {
            if (!calculateCommand.CanExecute(null))
                return;

            var answers = new LeaseCalculationAnswers
            {
                Framework = SelectedFramework,
                EntitySize = SelectedEntitySize,
                HasSubleases = ImportResult.Leases.Count > 0 ? SelectedHasSubleases : null
            };
            var importedData = ImportResult;
            isCalculating = true;
            UpdateCommands();
            CalculationResult = null;
            StatusMessage = "Calculating lease results…";
            try
            {
                CalculationResult = await Task.Run(() => calculationService.Calculate(importedData, answers));
                StatusMessage = "Calculation complete.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Unable to calculate lease results: {ex.Message}";
            }
            finally
            {
                isCalculating = false;
                UpdateCommands();
            }
        }

        private void InvalidateCalculation()
        {
            if (CalculationResult == null) return;
            CalculationResult = null;
            StatusMessage = "Inputs changed. Select Calculate to refresh the results.";
        }

        private void UpdateCommands()
        {
            browseCommand.RaiseCanExecuteChanged();
            exportCommand.RaiseCanExecuteChanged();
            calculateCommand.RaiseCanExecuteChanged();
            OnPropertyChanged(nameof(CanEditAnswers));
            OnPropertyChanged(nameof(CanAnswerSubleases));
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
