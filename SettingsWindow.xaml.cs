using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace LogViewer
{
    public partial class SettingsWindow : Window
    {
        private ObservableCollection<LogFile> _logFiles;
        private HashSet<string> _keywords;
        private int _maxLinesPerFile;
        private bool _showLineRateWarning;
        private AppSettings _appSettings;
        private ObservableCollection<RecurringLine> _recurringLines = new ObservableCollection<RecurringLine>();
        
        public HashSet<string> Keywords => _keywords;
        public ObservableCollection<LogFile> LogFiles => _logFiles;
        public int MaxLinesPerFile => _maxLinesPerFile;
        public bool ShowLineRateWarning => _showLineRateWarning;
        public ObservableCollection<RecurringLine> RecurringLines => _recurringLines;

        public class RecurringLine
        {
            public string FileName { get; set; }
            public string LineText { get; set; }
            public int Count { get; set; }
            public bool HasKeywords { get; set; }
            public bool IsSelectedForExport { get; set; }
        }

        public SettingsWindow(ObservableCollection<LogFile> logFiles, HashSet<string> keywords, int maxLinesPerFile, bool showLineRateWarning, AppSettings appSettings)
        {
            InitializeComponent();
            
            _logFiles = logFiles;
            _keywords = new HashSet<string>(keywords, StringComparer.OrdinalIgnoreCase);
            _maxLinesPerFile = maxLinesPerFile;
            _showLineRateWarning = showLineRateWarning;
            _appSettings = appSettings;
            
            // Populate the UI
            LoadKeywordsIntoTextBox();
            LoadAliasesIntoDataGrid();
            LoadLineRateLimitSettings();
            LoadThemeSettings();
            LoadPerformanceSettings();
            
            // Initialize recurring lines data grid
            RecurringLinesDataGrid.ItemsSource = _recurringLines;
        }

        private void LoadKeywordsIntoTextBox()
        {
            KeywordsTextBox.Text = string.Join(Environment.NewLine, _keywords.OrderBy(k => k));
        }

        private void LoadThemeSettings()
        {
            DefaultDarkThemeCheckBox.IsChecked = _appSettings.DefaultDarkTheme;
        }

        private void LoadPerformanceSettings()
        {
            MaxEntriesTextBox.Text = _appSettings.MaxDisplayedEntries.ToString();
            AutoRefreshCheckBox.IsChecked = _appSettings.AutoRefreshEnabled;
            AutoScrollCheckBox.IsChecked = _appSettings.AutoScrollEnabled;
        }

        private void LoadAliasesIntoDataGrid()
        {
            AliasesDataGrid.ItemsSource = _logFiles;
        }

        private void LoadLineRateLimitSettings()
        {
            MaxLinesTextBox.Text = _maxLinesPerFile.ToString();
            ShowWarningCheckBox.IsChecked = _showLineRateWarning;
        }

        private void LoadDefaultKeywords_Click(object sender, RoutedEventArgs e)
        {
            var defaultKeywords = new[] {
                "Error", "Exception", "Warning", "Critical", "Fatal", "Failed", "Timeout", "Null", "Invalid",
                "Denied", "Unauthorized", "Forbidden", "NotFound", "Crash", "Abort", "Panic", "Alert",
                "Emergency", "Severe", "High", "Medium", "Low", "Debug", "Info", "Trace", "Success",
                "Complete", "Started", "Stopped", "Connected", "Disconnected", "Retry", "Attempt",
                "Backup", "Restore", "Update", "Delete", "Create", "Insert", "Select", "Query",
                "Database", "Connection", "Network", "Memory", "CPU", "Disk", "Performance",
                "Authentication", "Authorization", "Login", "Logout", "Session", "Token"
            };

            KeywordsTextBox.Text = string.Join(Environment.NewLine, defaultKeywords);
        }

        private void ClearKeywords_Click(object sender, RoutedEventArgs e)
        {
            KeywordsTextBox.Text = "";
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            // Update keywords from text box
            _keywords.Clear();
            var keywordLines = KeywordsTextBox.Text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in keywordLines)
            {
                var keyword = line.Trim();
                if (!string.IsNullOrEmpty(keyword))
                {
                    _keywords.Add(keyword);
                }
            }

            // Update line rate limit settings
            if (int.TryParse(MaxLinesTextBox.Text, out int maxLines) && maxLines > 0)
            {
                _maxLinesPerFile = maxLines;
            }
            else
            {
                MessageBox.Show("Please enter a valid positive number for maximum lines per file.", "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _showLineRateWarning = ShowWarningCheckBox.IsChecked ?? true;

            // Update theme settings
            _appSettings.DefaultDarkTheme = DefaultDarkThemeCheckBox.IsChecked ?? false;

            // Update performance settings
            if (int.TryParse(MaxEntriesTextBox.Text, out int maxEntries) && maxEntries > 0)
            {
                _appSettings.MaxDisplayedEntries = maxEntries;
            }
            else
            {
                MessageBox.Show("Please enter a valid positive number for maximum entries.", "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _appSettings.AutoRefreshEnabled = AutoRefreshCheckBox.IsChecked ?? false;
            _appSettings.AutoScrollEnabled = AutoScrollCheckBox.IsChecked ?? false;

            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void AnalyzeRecurringLines_Click(object sender, RoutedEventArgs e)
        {
            _recurringLines.Clear();
            ExportRecurringLinesButton.IsEnabled = false;

            foreach (var logFile in _logFiles)
            {
                if (!logFile.IsVisible) continue;

                try
                {
                    var fileLines = File.ReadAllLines(logFile.FilePath);
                    var lineCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                    var keywordRegex = new Regex($"\b({string.Join("|", _keywords.Select(k => Regex.Escape(k)))})\b", 
                        RegexOptions.IgnoreCase);

                    foreach (var line in fileLines)
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;

                        if (!lineCounts.ContainsKey(line))
                        {
                            lineCounts[line] = 0;
                        }
                        lineCounts[line]++;
                    }

                    // Get top 5 recurring lines
                    var topLines = lineCounts.OrderByDescending(x => x.Value)
                        .Take(5)
                        .Select(x => new RecurringLine
                        {
                            FileName = logFile.Name,
                            LineText = x.Key,
                            Count = x.Value,
                            HasKeywords = keywordRegex.IsMatch(x.Key),
                            IsSelectedForExport = false
                        });

                    foreach (var line in topLines)
                    {
                        _recurringLines.Add(line);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error analyzing file {logFile.Name}: {ex.Message}", 
                        "Analysis Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }

            ExportRecurringLinesButton.IsEnabled = _recurringLines.Any(x => x.IsSelectedForExport);
        }

        private void ExportRecurringLines_Click(object sender, RoutedEventArgs e)
        {
            var selectedLines = _recurringLines.Where(x => x.IsSelectedForExport).ToList();
            if (!selectedLines.Any())
            {
                MessageBox.Show("No lines selected for export.", "Export Error", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var saveDialog = new SaveFileDialog
            {
                Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
                DefaultExt = "txt",
                FileName = "recurring_lines.txt"
            };

            if (saveDialog.ShowDialog() == true)
            {
                try
                {
                    using var writer = new StreamWriter(saveDialog.FileName);
                    writer.WriteLine("File Name\tLine Text\tCount\tHas Keywords\n");
                    foreach (var line in selectedLines)
                    {
                        writer.WriteLine($"{line.FileName}\t{line.LineText}\t{line.Count}\t{line.HasKeywords}");
                    }
                    MessageBox.Show("Export completed successfully!", "Export Complete", 
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error exporting recurring lines: {ex.Message}", 
                        "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }
}
