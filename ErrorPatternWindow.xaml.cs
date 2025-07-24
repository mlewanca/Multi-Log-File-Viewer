using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using System.IO;
using System.Text;

namespace LogViewer
{
    public partial class ErrorPatternWindow : Window
    {
        private List<LogEntry> _logEntries;
        private List<ErrorPattern> _patterns;
        private ErrorPatternDetector _detector;
        
        public ErrorPatternWindow(List<LogEntry> logEntries)
        {
            InitializeComponent();
            _logEntries = logEntries;
            _detector = new ErrorPatternDetector(_logEntries);
            AnalyzePatterns();
        }
        
        private void AnalyzePatterns()
        {
            StatusText.Text = "Analyzing error patterns...";
            
            _patterns = _detector.DetectPatterns();
            PatternsDataGrid.ItemsSource = _patterns;
            
            // Update status
            TotalPatternsText.Text = $"Patterns: {_patterns.Count}";
            TotalErrorsText.Text = $"Total Errors: {_patterns.Sum(p => p.Count)}";
            
            if (_patterns.Count == 0)
            {
                StatusText.Text = "No error patterns detected";
            }
            else
            {
                StatusText.Text = $"Found {_patterns.Count} error patterns";
            }
        }
        
        private void PatternsDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PatternsDataGrid.SelectedItem is ErrorPattern pattern)
            {
                OccurrencesDataGrid.ItemsSource = pattern.Occurrences.OrderByDescending(o => o.Timestamp);
            }
            else
            {
                OccurrencesDataGrid.ItemsSource = null;
            }
        }
        
        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            AnalyzePatterns();
        }
        
        private void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            var saveDialog = new SaveFileDialog
            {
                Filter = "CSV files (*.csv)|*.csv|Text files (*.txt)|*.txt",
                DefaultExt = "csv",
                FileName = $"error_patterns_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
            };
            
            if (saveDialog.ShowDialog() == true)
            {
                try
                {
                    var content = new StringBuilder();
                    content.AppendLine("Pattern,Count,First Occurrence,Last Occurrence,Duration,Average Per Hour");
                    
                    foreach (var pattern in _patterns)
                    {
                        content.AppendLine($"\"{pattern.PatternName}\",{pattern.Count}," +
                                         $"{pattern.FirstOccurrence:yyyy-MM-dd HH:mm:ss}," +
                                         $"{pattern.LastOccurrence:yyyy-MM-dd HH:mm:ss}," +
                                         $"{pattern.TimeSpan},{pattern.AveragePerHour:F2}");
                    }
                    
                    content.AppendLine();
                    content.AppendLine("Detailed Occurrences:");
                    content.AppendLine("Pattern,Timestamp,File,Level,Message");
                    
                    foreach (var pattern in _patterns)
                    {
                        foreach (var occurrence in pattern.Occurrences)
                        {
                            content.AppendLine($"\"{pattern.PatternName}\"," +
                                             $"{occurrence.Timestamp:yyyy-MM-dd HH:mm:ss}," +
                                             $"\"{occurrence.FileAlias}\"," +
                                             $"{occurrence.LogLevel}," +
                                             $"\"{occurrence.Content.Replace("\"", "\"\"")}\"");
                        }
                    }
                    
                    File.WriteAllText(saveDialog.FileName, content.ToString());
                    MessageBox.Show($"Error patterns exported to {saveDialog.FileName}", "Export Successful", 
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error exporting patterns: {ex.Message}", "Export Error", 
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }
}