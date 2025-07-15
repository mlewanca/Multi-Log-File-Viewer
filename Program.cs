using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using System.Xml.Linq;
using System.Xml.Serialization;
using Microsoft.Win32;
using System.Globalization;

namespace LogViewer
{
    public partial class MainWindow : Window
    {
        private ObservableCollection<LogFile> _logFiles;
        private ObservableCollection<LogEntry> _logEntries;
        private CollectionViewSource _logEntriesView;
        private const string ConfigFileName = "log_viewer_config.xml";
        private const string KeywordsFileName = "keywords.txt";
        private HashSet<string> _highlightKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private DateTime? _timeFilterCenter = null;
        private int _timeFilterSeconds = 30;
        private Dictionary<string, FileSystemWatcher> _fileWatchers = new Dictionary<string, FileSystemWatcher>();
        private bool _autoScroll = true;
        private int _maxLinesPerFile = 50000; // Default line rate limit
        private bool _showLineRateWarning = true;

        public MainWindow()
        {
            InitializeComponent();
            _logFiles = new ObservableCollection<LogFile>();
            _logEntries = new ObservableCollection<LogEntry>();
            _logEntriesView = new CollectionViewSource { Source = _logEntries };
            
            LogFilesDataGrid.ItemsSource = _logFiles;
            LogEntriesListView.ItemsSource = _logEntriesView.View;
            
            // Enable sorting by timestamp
            _logEntriesView.SortDescriptions.Add(new SortDescription("Timestamp", ListSortDirection.Ascending));
            
            // Auto-load configuration and keywords on startup
            Loaded += (s, e) => {
                LoadDefaultKeywords();
                AutoLoadConfiguration();
            };
        }

        private async void LoadFiles_Click(object sender, RoutedEventArgs e)
        {
            var folderDialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select folder containing log files",
                ShowNewFolderButton = false
            };

            if (folderDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                var patternDialog = new System.Windows.Forms.InputBox
                {
                    Prompt = "Enter file pattern (e.g., *.log;*.log.*):",
                    DefaultText = "*.log;*.log.*"
                };

                if (patternDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    var patterns = patternDialog.Text.Split(';')
                        .Select(p => p.Trim())
                        .Where(p => !string.IsNullOrWhiteSpace(p))
                        .ToArray();

                    if (patterns.Length == 0)
                    {
                        MessageBox.Show("Please enter at least one valid file pattern.", 
                            "Invalid Pattern", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    var files = new List<string>();
                    foreach (var pattern in patterns)
                    {
                        try
                        {
                            files.AddRange(Directory.GetFiles(folderDialog.SelectedPath, pattern));
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show($"Error loading files with pattern '{pattern}': {ex.Message}", 
                                "Load Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                    }

                    if (files.Count == 0)
                    {
                        MessageBox.Show("No files found matching the specified patterns.", 
                            "No Files Found", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }

                    if (_logFiles.Count + files.Count > 20)
                    {
                        MessageBox.Show($"Loading {files.Count} files would exceed the maximum limit of 20 files.", 
                            "Maximum Files Reached", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    LoadingIndicator.Visibility = Visibility.Visible;
                    await LoadLogFilesAsync(files.ToArray());
                    LoadingIndicator.Visibility = Visibility.Collapsed;
                }
            }
        }

        private async Task LoadLogFilesAsync(string[] filePaths)
        {
            var colorBrushes = new[]
            {
                Brushes.LightBlue, Brushes.LightGreen, Brushes.LightPink, Brushes.LightYellow,
                Brushes.LightCyan, Brushes.LightGray, Brushes.LightSalmon, Brushes.LightSeaGreen,
                Brushes.LightSkyBlue, Brushes.LightSlateGray, Brushes.LightSteelBlue, Brushes.LightGoldenrodYellow,
                Brushes.Lavender, Brushes.LemonChiffon, Brushes.PaleGreen, Brushes.PaleTurquoise,
                Brushes.PaleVioletRed, Brushes.PapayaWhip, Brushes.PeachPuff, Brushes.Peru
            };

            foreach (var filePath in filePaths)
            {
                if (_logFiles.Count >= 20) break;

                var fileName = Path.GetFileName(filePath);
                var alias = Path.GetFileNameWithoutExtension(filePath);
                var color = colorBrushes[_logFiles.Count % colorBrushes.Length];

                var logFile = new LogFile
                {
                    Id = Guid.NewGuid(),
                    Name = fileName,
                    Alias = alias,
                    FilePath = filePath,
                    Color = color,
                    IsVisible = true,
                    LineCount = 0
                };

                _logFiles.Add(logFile);

                // Load file content asynchronously
                await Task.Run(() => LoadFileContent(logFile));
                
                // Set up file watcher for auto-reload
                SetupFileWatcher(filePath);
            }

            RefreshLogEntries();
            AutoSaveConfiguration();
        }

        private void LoadFileContent(LogFile logFile)
        {
            try
            {
                // Get file size
                var fileInfo = new FileInfo(logFile.FilePath);
                logFile.FileSize = fileInfo.Length;

                using var reader = new StreamReader(logFile.FilePath);
                string line;
                int lineNumber = 0;
                bool showedWarning = false;

                while ((line = reader.ReadLine()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    lineNumber++;

                    // Check line rate limit
                    if (lineNumber > _maxLinesPerFile && _showLineRateWarning && !showedWarning)
                    {
                        showedWarning = true;
                        Application.Current.Dispatcher.Invoke(() => 
                        {
                            var result = MessageBox.Show(
                                $"File '{logFile.Name}' has {lineNumber} lines, which exceeds the limit of {_maxLinesPerFile} lines.\n\n" +
                                $"Loading large files may impact performance. Do you want to continue loading this file?\n\n" +
                                $"You can adjust the line limit in Settings.",
                                "Line Rate Limit Warning",
                                MessageBoxButton.YesNo,
                                MessageBoxImage.Warning);
                            
                            if (result == MessageBoxResult.No)
                            {
                                return;
                            }
                        });
                    }

                    var timestamp = ParseTimestamp(line);

                    var logEntry = new LogEntry
                    {
                        Content = line,
                        LineNumber = lineNumber,
                        FileId = logFile.Id,
                        Alias = logFile.Alias,
                        Color = logFile.Color,
                        Timestamp = timestamp.HasValue ? timestamp.Value : DateTime.MinValue,
                        HasTimestamp = timestamp.HasValue,
                        HighlightedContent = GenerateHighlightedContent(line)
                    };

                    Application.Current.Dispatcher.Invoke(() => _logEntries.Add(logEntry));
                }

                Application.Current.Dispatcher.Invoke(() => 
                {
                    logFile.LineCount = lineNumber;
                    UpdateStatusBar();
                });
            }
            catch (Exception ex)
            {
                Application.Current.Dispatcher.Invoke(() => 
                    MessageBox.Show($"Error loading file {logFile.Name}: {ex.Message}"));
            }
        }

        private DateTime? ParseTimestamp(string line)
        {
            // Match patterns like "Jul 10 08:17:46" or "Jul  1 08:17:46"
            var regex = new Regex(@"(\w{3})\s+(\d{1,2})\s+(\d{2}):(\d{2}):(\d{2})");
            var match = regex.Match(line);

            if (match.Success)
            {
                var monthMap = new Dictionary<string, int>
                {
                    {"Jan", 1}, {"Feb", 2}, {"Mar", 3}, {"Apr", 4}, {"May", 5}, {"Jun", 6},
                    {"Jul", 7}, {"Aug", 8}, {"Sep", 9}, {"Oct", 10}, {"Nov", 11}, {"Dec", 12}
                };

                var month = match.Groups[1].Value;
                var day = int.Parse(match.Groups[2].Value);
                var hour = int.Parse(match.Groups[3].Value);
                var minute = int.Parse(match.Groups[4].Value);
                var second = int.Parse(match.Groups[5].Value);

                if (monthMap.TryGetValue(month, out int monthNum))
                {
                    var year = DateTime.Now.Year; // Assume current year
                    return new DateTime(year, monthNum, day, hour, minute, second);
                }
            }

            return null;
        }

        private void RefreshLogEntries()
        {
            // Regenerate highlighted content for all entries
            foreach (var entry in _logEntries)
            {
                entry.HighlightedContent = GenerateHighlightedContent(entry.Content);
            }

            ApplyTimeFilter();
        }

        private async Task LoadLogFileAsync(LogFile logFile)
        {
            await Task.Run(() => LoadFileContent(logFile));
        }

        private void UpdateStatusBar()
        {
            var visibleFiles = _logFiles.Count(f => f.IsVisible);
            var totalLines = _logEntries.Count(e => _logFiles.First(f => f.Id == e.FileId).IsVisible);
            
            StatusBarText.Text = $"Files: {_logFiles.Count} | Visible: {visibleFiles} | Lines: {totalLines} | Sorted chronologically";
        }

        private void ToggleVisibility_Click(object sender, RoutedEventArgs e)
        {
            var button = (Button)sender;
            var logFile = (LogFile)button.DataContext;
            logFile.IsVisible = !logFile.IsVisible;
            RefreshLogEntries();
        }

        private void RemoveFile_Click(object sender, RoutedEventArgs e)
        {
            var button = (Button)sender;
            var logFile = (LogFile)button.DataContext;
            
            // Remove log entries for this file
            var entriesToRemove = _logEntries.Where(entry => entry.FileId == logFile.Id).ToList();
            foreach (var entry in entriesToRemove)
            {
                _logEntries.Remove(entry);
            }

            _logFiles.Remove(logFile);
            RefreshLogEntries();
            AutoSaveConfiguration();
        }

        private void AliasTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var textBox = (TextBox)sender;
            var logFile = (LogFile)textBox.DataContext;
            
            // Update alias in log entries
            foreach (var entry in _logEntries.Where(e => e.FileId == logFile.Id))
            {
                entry.Alias = textBox.Text;
            }
            
            AutoSaveConfiguration();
        }

        private void ClearAll_Click(object sender, RoutedEventArgs e)
        {
            _logFiles.Clear();
            _logEntries.Clear();
            UpdateStatusBar();
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyTimeFilter();
        }



        private void SaveLogConfiguration(string filePath)
        {
            try
            {
                var config = new LogViewerConfiguration
                {
                    LogFiles = _logFiles.Select(lf => new LogFileConfig
                    {
                        FilePath = lf.FilePath,
                        Alias = lf.Alias,
                        IsVisible = lf.IsVisible
                    }).ToList(),
                    Keywords = _highlightKeywords.ToList(),
                    MaxLinesPerFile = _maxLinesPerFile,
                    ShowLineRateWarning = _showLineRateWarning
                };

                var doc = new XDocument(
                    new XElement("LogViewerConfiguration",
                        new XElement("Settings",
                            new XAttribute("MaxLinesPerFile", config.MaxLinesPerFile),
                            new XAttribute("ShowLineRateWarning", config.ShowLineRateWarning)
                        ),
                        new XElement("LogFiles",
                            config.LogFiles.Select(lf => new XElement("LogFile",
                                new XAttribute("FilePath", lf.FilePath),
                                new XAttribute("Alias", lf.Alias ?? ""),
                                new XAttribute("IsVisible", lf.IsVisible)
                            ))
                        ),
                        new XElement("Keywords",
                            config.Keywords.Select(k => new XElement("Keyword", k))
                        )
                    )
                );

                doc.Save(filePath);
                MessageBox.Show($"Configuration saved to {filePath}", "Save Successful", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving configuration: {ex.Message}", "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void LoadLogConfiguration(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    MessageBox.Show("Configuration file not found.", "Load Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var doc = XDocument.Load(filePath);
                var settingsElement = doc.Root?.Element("Settings");
                var logFileElements = doc.Root?.Element("LogFiles")?.Elements("LogFile");
                var keywordElements = doc.Root?.Element("Keywords")?.Elements("Keyword");

                if (logFileElements == null)
                {
                    MessageBox.Show("Invalid configuration file format.", "Load Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Load line rate limit settings
                if (settingsElement != null)
                {
                    if (int.TryParse(settingsElement.Attribute("MaxLinesPerFile")?.Value, out int maxLines) && maxLines > 0)
                    {
                        _maxLinesPerFile = maxLines;
                    }
                    if (bool.TryParse(settingsElement.Attribute("ShowLineRateWarning")?.Value, out bool showWarning))
                    {
                        _showLineRateWarning = showWarning;
                    }
                }

                // Load keywords
                _highlightKeywords.Clear();
                if (keywordElements != null)
                {
                    foreach (var keywordElement in keywordElements)
                    {
                        var keyword = keywordElement.Value?.Trim();
                        if (!string.IsNullOrEmpty(keyword))
                        {
                            _highlightKeywords.Add(keyword);
                        }
                    }
                }
                else
                {
                    // Load default keywords if none in XML
                    LoadDefaultKeywords();
                }

                // Clear existing files
                _logFiles.Clear();
                _logEntries.Clear();

                LoadingIndicator.Visibility = Visibility.Visible;

                var colorBrushes = new[]
                {
                    Brushes.LightBlue, Brushes.LightGreen, Brushes.LightPink, Brushes.LightYellow,
                    Brushes.LightCyan, Brushes.LightGray, Brushes.LightSalmon, Brushes.LightSeaGreen,
                    Brushes.LightSkyBlue, Brushes.LightSlateGray, Brushes.LightSteelBlue, Brushes.LightGoldenrodYellow,
                    Brushes.Lavender, Brushes.LemonChiffon, Brushes.PaleGreen, Brushes.PaleTurquoise,
                    Brushes.PaleVioletRed, Brushes.PapayaWhip, Brushes.PeachPuff, Brushes.Peru
                };

                int colorIndex = 0;
                foreach (var element in logFileElements)
                {
                    var filePathAttr = element.Attribute("FilePath")?.Value;
                    var aliasAttr = element.Attribute("Alias")?.Value;
                    var isVisibleAttr = element.Attribute("IsVisible")?.Value;

                    if (string.IsNullOrEmpty(filePathAttr) || !File.Exists(filePathAttr))
                    {
                        MessageBox.Show($"Log file not found: {filePathAttr}", "File Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
                        continue;
                    }

                    var fileName = Path.GetFileName(filePathAttr);
                    var alias = !string.IsNullOrEmpty(aliasAttr) ? aliasAttr : Path.GetFileNameWithoutExtension(filePathAttr);
                    var isVisible = bool.TryParse(isVisibleAttr, out bool visible) ? visible : true;
                    var color = colorBrushes[colorIndex % colorBrushes.Length];

                    var logFile = new LogFile
                    {
                        Id = Guid.NewGuid(),
                        Name = fileName,
                        Alias = alias,
                        FilePath = filePathAttr,
                        Color = color,
                        IsVisible = isVisible,
                        LineCount = 0
                    };

                    _logFiles.Add(logFile);
                    colorIndex++;

                    // Load file content asynchronously
                    await Task.Run(() => LoadFileContent(logFile));
                }

                LoadingIndicator.Visibility = Visibility.Collapsed;
                RefreshLogEntries();
                MessageBox.Show($"Configuration loaded from {filePath}", "Load Successful", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                LoadingIndicator.Visibility = Visibility.Collapsed;
                MessageBox.Show($"Error loading configuration: {ex.Message}", "Load Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void AutoSaveConfiguration()
        {
            try
            {
                var configPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LogViewer");
                Directory.CreateDirectory(configPath);
                var fullPath = Path.Combine(configPath, ConfigFileName);
                SaveLogConfiguration(fullPath);
            }
            catch
            {
                // Silent fail for auto-save
            }
        }

        private void AutoLoadConfiguration()
        {
            try
            {
                var configPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LogViewer");
                var fullPath = Path.Combine(configPath, ConfigFileName);
                if (File.Exists(fullPath))
                {
                    LoadLogConfiguration(fullPath);
                }
            }
            catch
            {
                // Silent fail for auto-load
            }
        }

        private List<TextSegment> GenerateHighlightedContent(string content)
        {
            var segments = new List<TextSegment>();
            
            if (string.IsNullOrEmpty(content) || _highlightKeywords.Count == 0)
            {
                segments.Add(new TextSegment { Text = content, IsHighlighted = false });
                return segments;
            }

            var words = content.Split(new[] { ' ', '\t', '\n', '\r', '.', ',', ';', ':', '!', '?', '(', ')', '[', ']', '{', '}', '<', '>', '"', '\'', '/', '\\', '|', '=', '+', '-', '*', '&', '%', '$', '#', '@', '~', '`' }, StringSplitOptions.None);
            var separators = new List<string>();
            
            // Extract separators to reconstruct the original text
            int currentIndex = 0;
            foreach (var word in words)
            {
                if (currentIndex < content.Length)
                {
                    int wordIndex = content.IndexOf(word, currentIndex);
                    if (wordIndex > currentIndex)
                    {
                        separators.Add(content.Substring(currentIndex, wordIndex - currentIndex));
                    }
                    else
                    {
                        separators.Add("");
                    }
                    currentIndex = wordIndex + word.Length;
                }
                else
                {
                    separators.Add("");
                }
            }
            
            // Add any remaining text as separator
            if (currentIndex < content.Length)
            {
                separators.Add(content.Substring(currentIndex));
            }
            else
            {
                separators.Add("");
            }

            // Build segments
            for (int i = 0; i < words.Length; i++)
            {
                // Add separator before word
                if (i < separators.Count && !string.IsNullOrEmpty(separators[i]))
                {
                    segments.Add(new TextSegment { Text = separators[i], IsHighlighted = false });
                }
                
                // Add word (highlighted if it's a keyword)
                if (!string.IsNullOrEmpty(words[i]))
                {
                    bool isKeyword = _highlightKeywords.Contains(words[i]);
                    segments.Add(new TextSegment { Text = words[i], IsHighlighted = isKeyword });
                }
            }
            
            // Add final separator
            if (separators.Count > words.Length && !string.IsNullOrEmpty(separators[words.Length]))
            {
                segments.Add(new TextSegment { Text = separators[words.Length], IsHighlighted = false });
            }

            return segments;
        }



        private void LoadDefaultKeywords()
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

            _highlightKeywords.Clear();
            foreach (var keyword in defaultKeywords)
            {
                _highlightKeywords.Add(keyword);
            }
        }

        private void LoadKeywords(string filePath = null)
        {
            try
            {
                if (string.IsNullOrEmpty(filePath))
                {
                    LoadDefaultKeywords();
                    RefreshLogEntries();
                    return;
                }

                if (!File.Exists(filePath))
                {
                    MessageBox.Show("Keywords file not found.", "Load Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var content = File.ReadAllText(filePath);
                var keywords = content.Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                                     .Select(k => k.Trim())
                                     .Where(k => !string.IsNullOrEmpty(k));

                _highlightKeywords.Clear();
                foreach (var keyword in keywords)
                {
                    _highlightKeywords.Add(keyword);
                }

                // Refresh the log entries to apply highlighting
                RefreshLogEntries();
                
                MessageBox.Show($"Loaded {_highlightKeywords.Count} keywords from {filePath}", "Keywords Loaded", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading keywords: {ex.Message}", "Load Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }



        private void Settings_Click(object sender, RoutedEventArgs e)
        {
            var settingsWindow = new SettingsWindow(_logFiles, _highlightKeywords, _maxLinesPerFile, _showLineRateWarning)
            {
                Owner = this
            };

            if (settingsWindow.ShowDialog() == true)
            {
                // Update keywords from settings
                _highlightKeywords.Clear();
                foreach (var keyword in settingsWindow.Keywords)
                {
                    _highlightKeywords.Add(keyword);
                }

                // Update line rate limit settings
                _maxLinesPerFile = settingsWindow.MaxLinesPerFile;
                _showLineRateWarning = settingsWindow.ShowLineRateWarning;

                // Refresh log entries to apply new highlighting
                RefreshLogEntries();
                
                // Auto-save configuration with updated keywords and settings
                AutoSaveConfiguration();
            }
        }

        private void ExportAll_Click(object sender, RoutedEventArgs e)
        {
            if (_logEntries.Count == 0)
            {
                MessageBox.Show("No log entries to export.", "Export Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var saveDialog = new SaveFileDialog
            {
                Filter = "Text files (*.txt)|*.txt|Log files (*.log)|*.log|All files (*.*)|*.*",
                DefaultExt = "txt",
                FileName = "exported_logs.txt",
                Title = "Export All Log Entries"
            };

            if (saveDialog.ShowDialog() == true)
            {
                try
                {
                    var visibleEntries = _logEntries
                        .Where(e => _logFiles.First(f => f.Id == e.FileId).IsVisible)
                        .OrderBy(e => e.Timestamp)
                        .ToList();

                    using (var writer = new StreamWriter(saveDialog.FileName))
                    {
                        writer.WriteLine($"# Exported Log Entries - {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                        writer.WriteLine($"# Total entries: {visibleEntries.Count}");
                        writer.WriteLine($"# Files: {string.Join(", ", _logFiles.Where(f => f.IsVisible).Select(f => f.Alias))}");
                        writer.WriteLine();

                        foreach (var entry in visibleEntries)
                        {
                            var timestamp = entry.HasTimestamp ? entry.Timestamp.ToString("yyyy-MM-dd HH:mm:ss") : "";
                            writer.WriteLine($"[{entry.Alias}] {timestamp} {entry.Content}");
                        }
                    }

                    MessageBox.Show($"Exported {visibleEntries.Count} log entries to {saveDialog.FileName}", "Export Successful", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error exporting logs: {ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void ExportKeywords_Click(object sender, RoutedEventArgs e)
        {
            if (_logEntries.Count == 0)
            {
                MessageBox.Show("No log entries to export.", "Export Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_highlightKeywords.Count == 0)
            {
                MessageBox.Show("No keywords defined. Please add keywords in Settings first.", "Export Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var saveDialog = new SaveFileDialog
            {
                Filter = "Text files (*.txt)|*.txt|Log files (*.log)|*.log|All files (*.*)|*.*",
                DefaultExt = "txt",
                FileName = "keyword_filtered_logs.txt",
                Title = "Export Keyword-Filtered Log Entries"
            };

            if (saveDialog.ShowDialog() == true)
            {
                try
                {
                    var visibleEntries = _logEntries
                        .Where(e => _logFiles.First(f => f.Id == e.FileId).IsVisible)
                        .Where(e => ContainsKeywords(e.Content))
                        .OrderBy(e => e.Timestamp)
                        .ToList();

                    using (var writer = new StreamWriter(saveDialog.FileName))
                    {
                        writer.WriteLine($"# Exported Keyword-Filtered Log Entries - {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                        writer.WriteLine($"# Keywords: {string.Join(", ", _highlightKeywords.OrderBy(k => k))}");
                        writer.WriteLine($"# Total filtered entries: {visibleEntries.Count}");
                        writer.WriteLine($"# Files: {string.Join(", ", _logFiles.Where(f => f.IsVisible).Select(f => f.Alias))}");
                        writer.WriteLine();

                        foreach (var entry in visibleEntries)
                        {
                            var timestamp = entry.HasTimestamp ? entry.Timestamp.ToString("yyyy-MM-dd HH:mm:ss") : "";
                            writer.WriteLine($"[{entry.Alias}] {timestamp} {entry.Content}");
                        }
                    }

                    MessageBox.Show($"Exported {visibleEntries.Count} keyword-filtered log entries to {saveDialog.FileName}", "Export Successful", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error exporting keyword-filtered logs: {ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private bool ContainsKeywords(string content)
        {
            if (string.IsNullOrEmpty(content) || _highlightKeywords.Count == 0)
                return false;

            var words = content.Split(new[] { ' ', '\t', '\n', '\r', '.', ',', ';', ':', '!', '?', '(', ')', '[', ']', '{', '}', '<', '>', '"', '\'', '/', '\\', '|', '=', '+', '-', '*', '&', '%', '$', '#', '@', '~', '`' }, StringSplitOptions.RemoveEmptyEntries);
            return words.Any(word => _highlightKeywords.Contains(word));
        }

        private void TimeFilterTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (int.TryParse(TimeFilterTextBox.Text, out int seconds) && seconds >= 0)
            {
                _timeFilterSeconds = seconds;
            }
        }

        private void TimeFilter_Click(object sender, RoutedEventArgs e)
        {
            var selectedEntry = LogEntriesListView.SelectedItem as LogEntry;
            if (selectedEntry != null)
            {
                _timeFilterCenter = selectedEntry.Timestamp;
                ApplyTimeFilter();
            }
            else
            {
                MessageBox.Show("Please select a log entry to center the time filter around.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void ClearTimeFilter_Click(object sender, RoutedEventArgs e)
        {
            _timeFilterCenter = null;
            ApplyTimeFilter();
        }

        private void ApplyTimeFilter()
        {
            _logEntriesView.View.Filter = entry =>
            {
                var logEntry = (LogEntry)entry;
                var logFile = _logFiles.FirstOrDefault(f => f.Id == logEntry.FileId);
                
                // Check if file is visible
                if (logFile?.IsVisible != true)
                    return false;
                
                // Apply search filter
                if (!string.IsNullOrEmpty(SearchTextBox.Text))
                {
                    if (!logEntry.Content.Contains(SearchTextBox.Text, StringComparison.OrdinalIgnoreCase))
                        return false;
                }
                
                // Apply time filter
                if (_timeFilterCenter.HasValue)
                {
                    var timeDiff = Math.Abs((logEntry.Timestamp - _timeFilterCenter.Value).TotalSeconds);
                    return timeDiff <= _timeFilterSeconds;
                }
                
                return true;
            };
            
            _logEntriesView.View.Refresh();
            UpdateStatusBar();
            
            // Auto-scroll to latest entry after filtering
            Dispatcher.BeginInvoke(new Action(() => ScrollToLatestEntry()), System.Windows.Threading.DispatcherPriority.Background);
        }

        private void SetupFileWatcher(string filePath)
        {
            if (_fileWatchers.ContainsKey(filePath))
                return;

            try
            {
                var watcher = new FileSystemWatcher
                {
                    Path = Path.GetDirectoryName(filePath),
                    Filter = Path.GetFileName(filePath),
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
                    EnableRaisingEvents = true
                };

                watcher.Changed += async (sender, e) =>
                {
                    // Debounce file changes (wait 500ms)
                    await Task.Delay(500);
                    
                    await Dispatcher.InvokeAsync(async () =>
                    {
                        await ReloadLogFile(filePath);
                    });
                };

                _fileWatchers[filePath] = watcher;
            }
            catch (Exception ex)
            {
                // Silently handle file watcher setup errors
                System.Diagnostics.Debug.WriteLine($"Failed to setup file watcher for {filePath}: {ex.Message}");
            }
        }

        private async Task ReloadLogFile(string filePath)
        {
            try
            {
                var logFile = _logFiles.FirstOrDefault(f => f.FilePath == filePath);
                if (logFile == null) return;

                // Remove existing entries for this file
                var existingEntries = _logEntries.Where(e => e.FileId == logFile.Id).ToList();
                foreach (var entry in existingEntries)
                {
                    _logEntries.Remove(entry);
                }

                // Reload the file
                await LoadLogFileAsync(logFile);
                
                // Reapply filters
                ApplyTimeFilter();
                
                // Auto-scroll to latest entry after reload
                Dispatcher.BeginInvoke(new Action(() => ScrollToLatestEntry()), System.Windows.Threading.DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to reload log file {filePath}: {ex.Message}");
            }
        }

        private void CleanupFileWatchers()
        {
            foreach (var watcher in _fileWatchers.Values)
            {
                watcher?.Dispose();
            }
            _fileWatchers.Clear();
        }

        private void AutoScroll_Changed(object sender, RoutedEventArgs e)
        {
            _autoScroll = AutoScrollCheckBox.IsChecked == true;
        }

        private void ScrollToLatestEntry()
        {
            if (_autoScroll && LogEntriesListView.Items.Count > 0)
            {
                var lastItem = LogEntriesListView.Items[LogEntriesListView.Items.Count - 1];
                LogEntriesListView.ScrollIntoView(lastItem);
            }
        }

        private void ExportTimeFilter_Click(object sender, RoutedEventArgs e)
        {
            if (!_timeFilterCenter.HasValue)
            {
                MessageBox.Show("No time filter is currently active. Please apply a time filter first.", "No Time Filter", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var saveDialog = new SaveFileDialog
            {
                Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
                DefaultExt = "txt",
                FileName = $"time_filtered_logs_{DateTime.Now:yyyyMMdd_HHmmss}.txt",
                Title = "Export Time Filtered Log Entries"
            };

            if (saveDialog.ShowDialog() == true)
            {
                try
                {
                    var filteredEntries = _logEntries
                        .Where(entry =>
                        {
                            var logFile = _logFiles.FirstOrDefault(f => f.Id == entry.FileId);
                            if (logFile?.IsVisible != true) return false;
                            
                            // Apply search filter
                            if (!string.IsNullOrEmpty(SearchTextBox.Text))
                            {
                                if (!entry.Content.Contains(SearchTextBox.Text, StringComparison.OrdinalIgnoreCase))
                                    return false;
                            }
                            
                            // Apply time filter
                            var timeDiff = Math.Abs((entry.Timestamp - _timeFilterCenter.Value).TotalSeconds);
                            return timeDiff <= _timeFilterSeconds;
                        })
                        .OrderBy(e => e.Timestamp)
                        .ToList();

                    var content = new StringBuilder();
                    content.AppendLine($"# Time Filtered Log Entries - {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                    content.AppendLine($"# Center time: {_timeFilterCenter.Value:yyyy-MM-dd HH:mm:ss}");
                    content.AppendLine($"# Time range: ±{_timeFilterSeconds} seconds");
                    content.AppendLine($"# Total filtered entries: {filteredEntries.Count}");
                    
                    if (!string.IsNullOrEmpty(SearchTextBox.Text))
                    {
                        content.AppendLine($"# Search filter: '{SearchTextBox.Text}'");
                    }
                    
                    var visibleFiles = _logFiles.Where(f => f.IsVisible).Select(f => f.Alias).ToList();
                    content.AppendLine($"# Files: {string.Join(", ", visibleFiles)}");
                    content.AppendLine();

                    foreach (var entry in filteredEntries)
                    {
                        content.AppendLine($"[{entry.Alias}] {entry.Timestamp:yyyy-MM-dd HH:mm:ss} {entry.Content}");
                    }

                    File.WriteAllText(saveDialog.FileName, content.ToString());
                    MessageBox.Show($"Time filtered log entries exported to {saveDialog.FileName}\n\nTotal entries: {filteredEntries.Count}", "Export Successful", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error exporting time filtered entries: {ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            CleanupFileWatchers();
            base.OnClosed(e);
        }
    }

    public class LogFile : INotifyPropertyChanged
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public string FilePath { get; set; }
        public Brush Color { get; set; }
        public int LineCount { get; set; }
        public long FileSize { get; set; }

        public string FileSizeFormatted
        {
            get
            {
                if (FileSize < 1024) return $"{FileSize} B";
                if (FileSize < 1024 * 1024) return $"{FileSize / 1024:F1} KB";
                if (FileSize < 1024 * 1024 * 1024) return $"{FileSize / (1024 * 1024):F1} MB";
                return $"{FileSize / (1024 * 1024 * 1024):F1} GB";
            }
        }

        private string _alias;
        public string Alias
        {
            get => _alias;
            set
            {
                _alias = value;
                OnPropertyChanged(nameof(Alias));
            }
        }

        private bool _isVisible;
        public bool IsVisible
        {
            get => _isVisible;
            set
            {
                _isVisible = value;
                OnPropertyChanged(nameof(IsVisible));
                OnPropertyChanged(nameof(VisibilityButtonText));
            }
        }

        public string VisibilityButtonText => IsVisible ? "Hide" : "Show";

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class LogEntry : INotifyPropertyChanged
    {
        public string Content { get; set; }
        public int LineNumber { get; set; }
        public Guid FileId { get; set; }
        public Brush Color { get; set; }
        public DateTime Timestamp { get; set; }
        public bool HasTimestamp { get; set; }
        public List<TextSegment> HighlightedContent { get; set; } = new List<TextSegment>();

        private string _alias;
        public string Alias
        {
            get => _alias;
            set
            {
                _alias = value;
                OnPropertyChanged(nameof(Alias));
            }
        }

        public string TimestampString => HasTimestamp ? Timestamp.ToString("MMM dd HH:mm:ss") : "";

        public TextBlock FormattedContent
        {
            get
            {
                var textBlock = new TextBlock();
                foreach (var segment in HighlightedContent)
                {
                    var run = new Run(segment.Text);
                    if (segment.IsHighlighted)
                    {
                        run.FontWeight = FontWeights.Bold;
                        run.Foreground = Brushes.Yellow;
                    }
                    textBlock.Inlines.Add(run);
                }
                return textBlock;
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class LogViewerConfiguration
    {
        public List<string> Keywords { get; set; } = new List<string>();
        public List<LogFileConfig> LogFiles { get; set; } = new List<LogFileConfig>();
        public int MaxLinesPerFile { get; set; } = 50000;
        public bool ShowLineRateWarning { get; set; } = true;
    }

    public class LogFileConfig
    {
        public string FilePath { get; set; }
        public string Alias { get; set; }
        public bool IsVisible { get; set; }
    }

    public class TextSegment
    {
        public string Text { get; set; }
        public bool IsHighlighted { get; set; }
    }
}