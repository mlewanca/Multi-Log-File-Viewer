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
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Input;
using System.Xml.Linq;
using System.Xml.Serialization;
using Microsoft.Win32;
using System.Globalization;
using Microsoft.VisualBasic;

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
        
        // New fields for enhanced features
        private HashSet<string> _activeLogLevelFilters = new HashSet<string>();
        private List<string> _recentFiles = new List<string>();
        private const string RECENT_FILES_FILE = "recent_files.txt";
        private const string FILTER_PRESETS_FILE = "filter_presets.xml";
        private const string SETTINGS_FILE = "settings.xml";
        private const int MAX_RECENT_FILES = 10;
        private const int MAX_DISPLAYED_ENTRIES = 10000; // Virtual scrolling limit
        private bool _isDarkTheme = false;
        private List<FilterPreset> _filterPresets = new List<FilterPreset>();
        private bool _autoRefreshEnabled = false;
        private AppSettings _appSettings = new AppSettings();

        public MainWindow()
        {
            InitializeComponent();
            
            // Initialize collections
            _logFiles = new ObservableCollection<LogFile>();
            _logEntries = new ObservableCollection<LogEntry>();
            _logEntriesView = new CollectionViewSource { Source = _logEntries };
            
            LogFilesDataGrid.ItemsSource = _logFiles;
            LogEntriesListView.ItemsSource = _logEntriesView.View;
            
            // Enable sorting by timestamp
            _logEntriesView.SortDescriptions.Add(new SortDescription("Timestamp", ListSortDirection.Ascending));
            
            InitializeKeyboardCommands();
            
            // Initialize log level filters (all enabled by default)
            _activeLogLevelFilters.Add("ERROR");
            _activeLogLevelFilters.Add("WARN");
            _activeLogLevelFilters.Add("INFO");
            _activeLogLevelFilters.Add("DEBUG");
            
            // Load settings and apply theme
            LoadSettings();
            _isDarkTheme = _appSettings.DefaultDarkTheme;
            ApplyTheme();
            ThemeToggleButton.Content = _isDarkTheme ? "☀️" : "🌙";
            
            // Load recent files and filter presets
            LoadRecentFiles();
            LoadFilterPresets();
        }

        private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.O && Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                {
                    // Ctrl+Shift+O: Load Single File
                    LoadSingleFile_Click(sender, new RoutedEventArgs());
                }
                else
                {
                    // Ctrl+O: Load Folder (backward compatibility)
                    LoadFolder_Click(sender, new RoutedEventArgs());
                }
                e.Handled = true;
            }
        }

        private async void LoadSingleFile_Click(object sender, RoutedEventArgs e)
        {
            var fileDialog = new OpenFileDialog
            {
                Title = "Select a log file",
                Filter = "Log files (*.log;*.txt)|*.log;*.txt|All files (*.*)|*.*",
                Multiselect = false
            };

            if (fileDialog.ShowDialog() == true)
            {
                if (_logFiles.Count >= 20)
                {
                    MessageBox.Show("Maximum limit of 20 files reached. Please clear some files first.", 
                        "Maximum Files Reached", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                LoadingIndicator.Visibility = Visibility.Visible;
                await LoadLogFilesAsync(new[] { fileDialog.FileName });
                LoadingIndicator.Visibility = Visibility.Collapsed;
                
                // Add parent directory to recent files
                var parentDir = Path.GetDirectoryName(fileDialog.FileName);
                if (!string.IsNullOrEmpty(parentDir))
                {
                    AddToRecentFiles(parentDir);
                    UpdateRecentFilesDropdown();
                }
            }
        }

        private async void LoadFolder_Click(object sender, RoutedEventArgs e)
        {
            var folderDialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select folder containing log files",
                ShowNewFolderButton = false
            };

            if (folderDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                var patternDialog = new InputBox
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
                    
                    // Add to recent files
                    AddToRecentFiles(folderDialog.SelectedPath);
                    UpdateRecentFilesDropdown();
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
            try
            {
                var visibleFiles = _logFiles.Count(f => f.IsVisible);
                var totalLines = _logEntries.Count(e => _logFiles.First(f => f.Id == e.FileId).IsVisible);
                
                StatusBarText.Text = "Ready";
                
                // Update individual status bar elements if they exist
                if (FilesCountText != null)
                    FilesCountText.Text = $"Files: {_logFiles.Count}";
                if (EntriesCountText != null)
                    EntriesCountText.Text = $"Entries: {_logEntries.Count}";
                
                // Update filter status if the control exists
                if (FilterStatusText != null)
                {
                    var activeFilters = new List<string>();
                    if (!string.IsNullOrEmpty(SearchTextBox?.Text))
                    {
                        activeFilters.Add($"Search: '{SearchTextBox.Text}'");
                    }
                    if (_timeFilterCenter.HasValue)
                    {
                        activeFilters.Add($"Time: ±{_timeFilterSeconds}s");
                    }
                    if (_activeLogLevelFilters != null && _activeLogLevelFilters.Count < 4) // Not all levels active
                    {
                        activeFilters.Add($"Levels: {string.Join(",", _activeLogLevelFilters)}");
                    }
                    
                    FilterStatusText.Text = activeFilters.Count > 0 ? string.Join(", ", activeFilters) : "No filters";
                }
                else
                {
                    // Fallback to original status bar format if new elements don't exist
                    StatusBarText.Text = $"Files: {_logFiles.Count} | Visible: {visibleFiles} | Lines: {totalLines} | Sorted chronologically";
                }
            }
            catch (Exception ex)
            {
                // Ignore status bar update errors and use fallback
                try
                {
                    StatusBarText.Text = "Ready";
                }
                catch
                {
                    // Ignore even fallback errors
                }
            }
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





        private void ExportAll_Click(object sender, RoutedEventArgs e)
        {
            if (_logEntries.Count == 0)
            {
                MessageBox.Show("No log entries to export.", "Export Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var saveDialog = new SaveFileDialog
            {
                Filter = "Text files (*.txt)|*.txt|CSV files (*.csv)|*.csv|JSON files (*.json)|*.json|XML files (*.xml)|*.xml|Log files (*.log)|*.log|All files (*.*)|*.*",
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

                    var fileExtension = Path.GetExtension(saveDialog.FileName).ToLower();
                    
                    switch (fileExtension)
                    {
                        case ".csv":
                            ExportToCsv(saveDialog.FileName, visibleEntries);
                            break;
                        case ".json":
                            ExportToJson(saveDialog.FileName, visibleEntries);
                            break;
                        case ".xml":
                            ExportToXml(saveDialog.FileName, visibleEntries);
                            break;
                        default:
                            ExportToText(saveDialog.FileName, visibleEntries);
                            break;
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
            SaveRecentFiles();
            base.OnClosed(e);
        }
        
        #region Enhanced UI Features
        
        private void InitializeKeyboardCommands()
        {
            // Note: Keyboard bindings are handled in XAML, but we can add command logic here if needed
            // For now, we'll use the existing Click event handlers
        }
        
        private void LogLevelFilter_Click(object sender, RoutedEventArgs e)
        {
            if (sender is ToggleButton button && button.Tag is string logLevel)
            {
                if (button.IsChecked == true)
                {
                    _activeLogLevelFilters.Add(logLevel);
                }
                else
                {
                    _activeLogLevelFilters.Remove(logLevel);
                }
                
                ApplyLogLevelFilter();
                UpdateStatusBar();
            }
        }
        
        private void ApplyLogLevelFilter()
        {
            if (_logEntriesView?.View != null)
            {
                _logEntriesView.View.Filter = entry =>
                {
                    if (entry is LogEntry logEntry)
                    {
                        // Check if the log file is visible
                        var logFile = _logFiles.FirstOrDefault(f => f.Id == logEntry.FileId);
                        if (logFile?.IsVisible != true) return false;
                        
                        // Apply advanced search filter
                        if (!ApplyAdvancedSearchFilter(logEntry))
                            return false;
                        
                        // Apply log level filter
                        var entryLevel = ExtractLogLevel(logEntry.Content);
                        if (!string.IsNullOrEmpty(entryLevel) && !_activeLogLevelFilters.Contains(entryLevel))
                        {
                            return false;
                        }
                        
                        return true;
                    }
                    return false;
                };
            }
        }
        
        private bool ApplyAdvancedSearchFilter(LogEntry logEntry)
        {
            var searchText = SearchTextBox?.Text;
            if (string.IsNullOrEmpty(searchText))
                return true;
                
            try
            {
                var isRegex = RegexSearchCheckBox?.IsChecked == true;
                var isCaseSensitive = CaseSensitiveCheckBox?.IsChecked == true;
                var searchColumn = (SearchColumnComboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All";
                
                var comparisonType = isCaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
                
                // Get the log file for file-based search
                var logFile = _logFiles.FirstOrDefault(f => f.Id == logEntry.FileId);
                
                if (isRegex)
                {
                    var regexOptions = isCaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase;
                    var regex = new Regex(searchText, regexOptions);
                    
                    return searchColumn switch
                    {
                        "Content" => regex.IsMatch(logEntry.Content),
                        "Timestamp" => regex.IsMatch(logEntry.TimestampString),
                        "File" => logFile != null && regex.IsMatch(logFile.Alias ?? logFile.Name),
                        _ => regex.IsMatch(logEntry.Content) || 
                             regex.IsMatch(logEntry.TimestampString) || 
                             (logFile != null && regex.IsMatch(logFile.Alias ?? logFile.Name))
                    };
                }
                else
                {
                    return searchColumn switch
                    {
                        "Content" => logEntry.Content.Contains(searchText, comparisonType),
                        "Timestamp" => logEntry.TimestampString.Contains(searchText, comparisonType),
                        "File" => logFile != null && (logFile.Alias ?? logFile.Name).Contains(searchText, comparisonType),
                        _ => logEntry.Content.Contains(searchText, comparisonType) || 
                             logEntry.TimestampString.Contains(searchText, comparisonType) || 
                             (logFile != null && (logFile.Alias ?? logFile.Name).Contains(searchText, comparisonType))
                    };
                }
            }
            catch (Exception)
            {
                // If regex is invalid, fall back to simple string search
                return logEntry.Content.Contains(searchText, StringComparison.OrdinalIgnoreCase);
            }
        }
        
        private void SearchOptions_Changed(object sender, RoutedEventArgs e)
        {
            ApplyLogLevelFilter();
            UpdateStatusBar();
        }
        
        private string ExtractLogLevel(string content)
        {
            // Simple log level extraction - look for common patterns
            var upperContent = content.ToUpper();
            if (upperContent.Contains("ERROR") || upperContent.Contains("ERR")) return "ERROR";
            if (upperContent.Contains("WARN") || upperContent.Contains("WARNING")) return "WARN";
            if (upperContent.Contains("INFO")) return "INFO";
            if (upperContent.Contains("DEBUG") || upperContent.Contains("DBG")) return "DEBUG";
            return "INFO"; // Default to INFO if no level detected
        }
        

        
        private void LoadRecentFiles()
        {
            try
            {
                if (File.Exists(RECENT_FILES_FILE))
                {
                    var lines = File.ReadAllLines(RECENT_FILES_FILE);
                    _recentFiles = lines.Where(line => !string.IsNullOrWhiteSpace(line) && Directory.Exists(line))
                        .Take(MAX_RECENT_FILES)
                        .ToList();
                }
                
                // Update the dropdown
                UpdateRecentFilesDropdown();
            }
            catch (Exception ex)
            {
                // Ignore recent files loading errors
            }
        }
        
        private void UpdateRecentFilesDropdown()
        {
            try
            {
                RecentFilesComboBox.Items.Clear();
                foreach (var recentFile in _recentFiles)
                {
                    var displayName = Path.GetFileName(recentFile);
                    if (string.IsNullOrEmpty(displayName))
                        displayName = recentFile;
                    
                    RecentFilesComboBox.Items.Add(new ComboBoxItem
                    {
                        Content = displayName,
                        Tag = recentFile,
                        ToolTip = recentFile
                    });
                }
                
                RecentFilesComboBox.IsEnabled = _recentFiles.Count > 0;
            }
            catch (Exception ex)
            {
                // Ignore dropdown update errors
            }
        }
        
        private async void RecentFiles_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ComboBox comboBox && comboBox.SelectedItem is ComboBoxItem item && item.Tag is string folderPath)
            {
                try
                {
                    // Load files from the selected recent folder
                    var patterns = new[] { "*.log", "*.log.*" };
                    var files = new List<string>();
                    
                    foreach (var pattern in patterns)
                    {
                        files.AddRange(Directory.GetFiles(folderPath, pattern));
                    }
                    
                    if (files.Count > 0)
                    {
                        await LoadLogFilesAsync(files.ToArray());
                        AddToRecentFiles(folderPath);
                        UpdateRecentFilesDropdown();
                    }
                    else
                    {
                        MessageBox.Show($"No log files found in {folderPath}", "No Files Found", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error loading recent files: {ex.Message}", "Load Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    comboBox.SelectedIndex = -1; // Reset selection
                }
            }
        }
        
        private void SaveRecentFiles()
        {
            try
            {
                File.WriteAllLines(RECENT_FILES_FILE, _recentFiles.Take(MAX_RECENT_FILES));
            }
            catch (Exception ex)
            {
                // Ignore recent files saving errors
            }
        }
        
        private void AddToRecentFiles(string folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
                return;
                
            _recentFiles.Remove(folderPath); // Remove if already exists
            _recentFiles.Insert(0, folderPath); // Add to beginning
            
            if (_recentFiles.Count > MAX_RECENT_FILES)
            {
                _recentFiles = _recentFiles.Take(MAX_RECENT_FILES).ToList();
            }
        }
        
        private void Window_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                var logFiles = files.Where(f => Path.GetExtension(f).ToLower().Contains("log") || 
                                               f.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                                   .ToArray();
                
                if (logFiles.Length > 0)
                {
                    e.Effects = DragDropEffects.Copy;
                    e.Handled = true;
                    return;
                }
            }
            
            e.Effects = DragDropEffects.None;
            e.Handled = true;
        }
        
        private async void Window_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                var logFiles = files.Where(f => Path.GetExtension(f).ToLower().Contains("log") || 
                                               f.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                                   .ToArray();
                
                if (logFiles.Length > 0)
                {
                    try
                    {
                        if (_logFiles.Count + logFiles.Length > 20)
                        {
                            MessageBox.Show($"Dropping {logFiles.Length} files would exceed the maximum limit of 20 files.", 
                                "Maximum Files Reached", MessageBoxButton.OK, MessageBoxImage.Warning);
                            return;
                        }
                        
                        LoadingIndicator.Visibility = Visibility.Visible;
                        await LoadLogFilesAsync(logFiles);
                        LoadingIndicator.Visibility = Visibility.Collapsed;
                        
                        // Add the parent directories to recent files
                        var directories = logFiles.Select(f => Path.GetDirectoryName(f))
                                                 .Distinct()
                                                 .Where(d => !string.IsNullOrEmpty(d))
                                                 .ToArray();
                        
                        foreach (var dir in directories)
                        {
                            AddToRecentFiles(dir);
                        }
                        UpdateRecentFilesDropdown();
                        
                        StatusBarText.Text = $"Loaded {logFiles.Length} files via drag & drop";
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Error loading dropped files: {ex.Message}", 
                            "Load Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                else
                {
                    MessageBox.Show("No valid log files found in the dropped items.", 
                        "Invalid Files", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }
        
        private void SavePreset_Click(object sender, RoutedEventArgs e)
        {
            var presetName = Microsoft.VisualBasic.Interaction.InputBox(
                "Enter a name for this filter preset:", 
                "Save Filter Preset", 
                $"Preset {_filterPresets.Count + 1}");
                
            if (!string.IsNullOrWhiteSpace(presetName))
            {
                var preset = new FilterPreset
                {
                    Name = presetName,
                    SearchText = SearchTextBox?.Text ?? "",
                    IsRegex = RegexSearchCheckBox?.IsChecked == true,
                    IsCaseSensitive = CaseSensitiveCheckBox?.IsChecked == true,
                    SearchColumn = (SearchColumnComboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All",
                    ActiveLogLevels = new HashSet<string>(_activeLogLevelFilters),
                    TimeFilterSeconds = _timeFilterSeconds
                };
                
                // Remove existing preset with same name
                _filterPresets.RemoveAll(p => p.Name == presetName);
                _filterPresets.Add(preset);
                
                SaveFilterPresets();
                UpdateFilterPresetsDropdown();
                
                StatusBarText.Text = $"Filter preset '{presetName}' saved";
            }
        }
        
        private void FilterPreset_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ComboBox comboBox && comboBox.SelectedItem is ComboBoxItem item && item.Tag is FilterPreset preset)
            {
                try
                {
                    // Apply the preset
                    SearchTextBox.Text = preset.SearchText;
                    RegexSearchCheckBox.IsChecked = preset.IsRegex;
                    CaseSensitiveCheckBox.IsChecked = preset.IsCaseSensitive;
                    
                    // Set search column
                    foreach (ComboBoxItem columnItem in SearchColumnComboBox.Items)
                    {
                        if (columnItem.Content.ToString() == preset.SearchColumn)
                        {
                            SearchColumnComboBox.SelectedItem = columnItem;
                            break;
                        }
                    }
                    
                    // Set log level filters
                    _activeLogLevelFilters = new HashSet<string>(preset.ActiveLogLevels);
                    ErrorFilterButton.IsChecked = _activeLogLevelFilters.Contains("ERROR");
                    WarnFilterButton.IsChecked = _activeLogLevelFilters.Contains("WARN");
                    InfoFilterButton.IsChecked = _activeLogLevelFilters.Contains("INFO");
                    DebugFilterButton.IsChecked = _activeLogLevelFilters.Contains("DEBUG");
                    
                    _timeFilterSeconds = preset.TimeFilterSeconds;
                    TimeFilterTextBox.Text = preset.TimeFilterSeconds.ToString();
                    
                    // Apply filters
                    ApplyLogLevelFilter();
                    UpdateStatusBar();
                    
                    StatusBarText.Text = $"Applied filter preset '{preset.Name}'";
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error applying filter preset: {ex.Message}", "Preset Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    comboBox.SelectedIndex = 0; // Reset to "Presets..."
                }
            }
        }
        
        private void LoadFilterPresets()
        {
            try
            {
                if (File.Exists(FILTER_PRESETS_FILE))
                {
                    var serializer = new XmlSerializer(typeof(List<FilterPreset>));
                    using (var reader = new FileStream(FILTER_PRESETS_FILE, FileMode.Open, FileAccess.Read))
                    {
                        _filterPresets = (List<FilterPreset>)serializer.Deserialize(reader) ?? new List<FilterPreset>();
                    }
                }
                
                UpdateFilterPresetsDropdown();
            }
            catch (Exception ex)
            {
                // Ignore filter presets loading errors
                _filterPresets = new List<FilterPreset>();
            }
        }
        
        private void SaveFilterPresets()
        {
            try
            {
                var serializer = new XmlSerializer(typeof(List<FilterPreset>));
                using (var writer = new FileStream(FILTER_PRESETS_FILE, FileMode.Create, FileAccess.Write))
                {
                    serializer.Serialize(writer, _filterPresets);
                }
            }
            catch (Exception ex)
            {
                // Ignore filter presets saving errors
            }
        }
        
        private void UpdateFilterPresetsDropdown()
        {
            try
            {
                // Clear existing items except the first one
                while (FilterPresetsComboBox.Items.Count > 1)
                {
                    FilterPresetsComboBox.Items.RemoveAt(1);
                }
                
                foreach (var preset in _filterPresets)
                {
                    FilterPresetsComboBox.Items.Add(new ComboBoxItem
                    {
                        Content = preset.Name,
                        Tag = preset,
                        ToolTip = $"Search: '{preset.SearchText}', Levels: {string.Join(",", preset.ActiveLogLevels)}"
                    });
                }
                
                FilterPresetsComboBox.IsEnabled = _filterPresets.Count > 0;
            }
            catch (Exception ex)
            {
                // Ignore dropdown update errors
            }
        }
        
        private void ExportToText(string fileName, List<LogEntry> entries)
        {
            using (var writer = new StreamWriter(fileName))
            {
                writer.WriteLine($"# Exported Log Entries - {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                writer.WriteLine($"# Total entries: {entries.Count}");
                writer.WriteLine($"# Files: {string.Join(", ", _logFiles.Where(f => f.IsVisible).Select(f => f.Alias))}");
                writer.WriteLine();

                foreach (var entry in entries)
                {
                    var timestamp = entry.HasTimestamp ? entry.Timestamp.ToString("yyyy-MM-dd HH:mm:ss") : "";
                    writer.WriteLine($"[{entry.Alias}] {timestamp} {entry.Content}");
                }
            }
        }
        
        private void ExportToCsv(string fileName, List<LogEntry> entries)
        {
            using (var writer = new StreamWriter(fileName))
            {
                // Write CSV header
                writer.WriteLine("Timestamp,File,LineNumber,Content");
                
                foreach (var entry in entries)
                {
                    var timestamp = entry.HasTimestamp ? entry.Timestamp.ToString("yyyy-MM-dd HH:mm:ss") : "";
                    var content = entry.Content.Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
                    writer.WriteLine($"\"{timestamp}\",\"{entry.Alias}\",{entry.LineNumber},\"{content}\"");
                }
            }
        }
        
        private void ExportToJson(string fileName, List<LogEntry> entries)
        {
            var exportData = new
            {
                ExportInfo = new
                {
                    ExportDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    TotalEntries = entries.Count,
                    Files = _logFiles.Where(f => f.IsVisible).Select(f => f.Alias).ToArray()
                },
                LogEntries = entries.Select(entry => new
                {
                    Timestamp = entry.HasTimestamp ? entry.Timestamp.ToString("yyyy-MM-dd HH:mm:ss") : null,
                    File = entry.Alias,
                    LineNumber = entry.LineNumber,
                    Content = entry.Content
                }).ToArray()
            };
            
            var json = System.Text.Json.JsonSerializer.Serialize(exportData, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true
            });
            
            File.WriteAllText(fileName, json);
        }
        
        private void ExportToXml(string fileName, List<LogEntry> entries)
        {
            var doc = new XDocument(
                new XElement("LogExport",
                    new XElement("ExportInfo",
                        new XElement("ExportDate", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")),
                        new XElement("TotalEntries", entries.Count),
                        new XElement("Files", 
                            _logFiles.Where(f => f.IsVisible).Select(f => new XElement("File", f.Alias))
                        )
                    ),
                    new XElement("LogEntries",
                        entries.Select(entry => new XElement("LogEntry",
                            new XElement("Timestamp", entry.HasTimestamp ? entry.Timestamp.ToString("yyyy-MM-dd HH:mm:ss") : ""),
                            new XElement("File", entry.Alias),
                            new XElement("LineNumber", entry.LineNumber),
                            new XElement("Content", entry.Content)
                        ))
                    )
                )
            );
            
            doc.Save(fileName);
        }
        
        private void AutoRefresh_Changed(object sender, RoutedEventArgs e)
        {
            _autoRefreshEnabled = AutoRefreshCheckBox?.IsChecked == true;
            
            if (_autoRefreshEnabled)
            {
                // Enable file watchers for all loaded files
                foreach (var logFile in _logFiles)
                {
                    SetupFileWatcher(logFile.FilePath);
                }
                StatusBarText.Text = "Auto-refresh enabled";
            }
            else
            {
                // Disable all file watchers
                CleanupFileWatchers();
                StatusBarText.Text = "Auto-refresh disabled";
            }
        }
        
        private void OptimizeDisplayForLargeFiles()
        {
            // Implement virtual scrolling by limiting displayed entries
            if (_logEntries.Count > MAX_DISPLAYED_ENTRIES)
            {
                StatusBarText.Text = $"Showing first {MAX_DISPLAYED_ENTRIES} entries (total: {_logEntries.Count}). Use filters to narrow results.";
                
                // Show only the first MAX_DISPLAYED_ENTRIES for performance
                var displayEntries = _logEntries.Take(MAX_DISPLAYED_ENTRIES).ToList();
                StatusBarText.Text = $"Showing {MAX_DISPLAYED_ENTRIES} of {_logEntries.Count} entries (performance mode)";
                
                // Show warning about truncation
                if (_showLineRateWarning)
                {
                    var result = MessageBox.Show(
                        $"Large number of log entries detected ({_logEntries.Count:N0}). " +
                        $"Displaying only the first {MAX_DISPLAYED_ENTRIES:N0} entries for performance. " +
                        $"Use filters to narrow down results.\n\nDo you want to continue?",
                        "Performance Warning",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);
                        
                    if (result == MessageBoxResult.No)
                    {
                        return;
                    }
                }
            }
        }
        
        private void AddBookmark(LogEntry entry)
        {
            // Simple bookmark implementation - could be enhanced with persistent storage
            var bookmarkText = $"[{entry.Alias}] {entry.TimestampString} - {entry.Content.Substring(0, Math.Min(50, entry.Content.Length))}...";
            
            // For now, just copy to clipboard as a simple bookmark
            Clipboard.SetText($"Bookmark: {bookmarkText}");
            StatusBarText.Text = "Bookmark copied to clipboard";
        }
        
        private void AddBookmark_Click(object sender, RoutedEventArgs e)
        {
            if (LogEntriesListView.SelectedItem is LogEntry selectedEntry)
            {
                AddBookmark(selectedEntry);
            }
        }
        
        private void CopyLine_Click(object sender, RoutedEventArgs e)
        {
            if (LogEntriesListView.SelectedItem is LogEntry selectedEntry)
            {
                var lineText = $"[{selectedEntry.Alias}] {selectedEntry.TimestampString} {selectedEntry.Content}";
                Clipboard.SetText(lineText);
                StatusBarText.Text = "Line copied to clipboard";
            }
        }
        
        private void FilterByFile_Click(object sender, RoutedEventArgs e)
        {
            if (LogEntriesListView.SelectedItem is LogEntry selectedEntry)
            {
                var logFile = _logFiles.FirstOrDefault(f => f.Id == selectedEntry.FileId);
                if (logFile != null)
                {
                    SearchTextBox.Text = "";
                    SearchColumnComboBox.SelectedIndex = 3; // File column
                    SearchTextBox.Text = logFile.Alias ?? logFile.Name;
                    ApplyLogLevelFilter();
                    StatusBarText.Text = $"Filtered by file: {logFile.Alias ?? logFile.Name}";
                }
            }
        }
        
        private void FilterByTime_Click(object sender, RoutedEventArgs e)
        {
            if (LogEntriesListView.SelectedItem is LogEntry selectedEntry && selectedEntry.HasTimestamp)
            {
                _timeFilterCenter = selectedEntry.Timestamp;
                TimeFilterTextBox.Text = "30";
                _timeFilterSeconds = 30;
                ApplyTimeFilter();
                StatusBarText.Text = $"Time filter centered on: {selectedEntry.TimestampString}";
            }
        }
        
        private void Settings_Click(object sender, RoutedEventArgs e)
        {
            // Show settings menu options
            var settingsOptions = "Settings Options:\n\n" +
                                "1. Theme Settings (Default theme preference)\n" +
                                "2. Keyword Highlighting Settings\n" +
                                "3. Performance Settings\n\n" +
                                "Choose an option (1-3) or Cancel:";
            
            var input = Microsoft.VisualBasic.Interaction.InputBox(settingsOptions, "Settings", "1");
            
            if (string.IsNullOrEmpty(input)) return;
            
            switch (input.Trim())
            {
                case "1":
                    ShowThemeSettings();
                    break;
                case "2":
                    ShowKeywordSettings();
                    break;
                case "3":
                    ShowPerformanceSettings();
                    break;
                default:
                    MessageBox.Show("Invalid option. Please choose 1, 2, or 3.", "Settings", MessageBoxButton.OK, MessageBoxImage.Information);
                    break;
            }
        }
        
        private void ShowThemeSettings()
        {
            var currentTheme = _appSettings.DefaultDarkTheme ? "Dark" : "Light";
            var message = $"Current default theme: {currentTheme}\n\nChoose default theme for application startup:";
            
            var result = MessageBox.Show(message + "\n\nClick 'Yes' for Dark theme, 'No' for Light theme, 'Cancel' to keep current setting.", 
                                       "Theme Settings", 
                                       MessageBoxButton.YesNoCancel, 
                                       MessageBoxImage.Question);
            
            if (result != MessageBoxResult.Cancel)
            {
                var oldDarkTheme = _isDarkTheme;
                var newDefaultDarkTheme = (result == MessageBoxResult.Yes);
                
                _appSettings.DefaultDarkTheme = newDefaultDarkTheme;
                _isDarkTheme = newDefaultDarkTheme;
                
                // Apply theme if it changed
                if (oldDarkTheme != _isDarkTheme)
                {
                    ApplyTheme();
                    ThemeToggleButton.Content = _isDarkTheme ? "☀️" : "🌙";
                }
                
                // Save settings
                SaveSettings();
                
                var themeName = _appSettings.DefaultDarkTheme ? "dark" : "light";
                StatusBarText.Text = $"Default theme set to {themeName} and applied";
            }
        }
        
        private void ShowKeywordSettings()
        {
            var currentKeywords = string.Join(", ", _highlightKeywords);
            var message = $"Current highlight keywords: {currentKeywords}\n\nEnter new keywords (comma-separated):";
            
            var input = Microsoft.VisualBasic.Interaction.InputBox(message, "Keyword Settings", currentKeywords);
            
            if (!string.IsNullOrEmpty(input))
            {
                _highlightKeywords.Clear();
                var keywords = input.Split(',').Select(k => k.Trim()).Where(k => !string.IsNullOrEmpty(k));
                foreach (var keyword in keywords)
                {
                    _highlightKeywords.Add(keyword);
                }
                
                // Refresh log entries to apply new highlighting
                RefreshLogEntries();
                
                StatusBarText.Text = $"Updated highlight keywords: {string.Join(", ", _highlightKeywords)}";
            }
        }
        
        private void ShowPerformanceSettings()
        {
            var message = $"Current performance settings:\n\n" +
                         $"Max displayed entries: {_appSettings.MaxDisplayedEntries}\n" +
                         $"Auto-refresh enabled: {_appSettings.AutoRefreshEnabled}\n" +
                         $"Auto-scroll enabled: {_appSettings.AutoScrollEnabled}\n\n" +
                         $"Enter new max displayed entries (current: {_appSettings.MaxDisplayedEntries}):";
            
            var input = Microsoft.VisualBasic.Interaction.InputBox(message, "Performance Settings", _appSettings.MaxDisplayedEntries.ToString());
            
            if (!string.IsNullOrEmpty(input) && int.TryParse(input, out int maxEntries) && maxEntries > 0)
            {
                _appSettings.MaxDisplayedEntries = maxEntries;
                SaveSettings();
                
                StatusBarText.Text = $"Max displayed entries set to {maxEntries}";
            }
        }
        
        private void ThemeToggle_Click(object sender, RoutedEventArgs e)
        {
            _isDarkTheme = !_isDarkTheme;
            _appSettings.DefaultDarkTheme = _isDarkTheme; // Update setting
            ApplyTheme();
            
            // Update button icon
            ThemeToggleButton.Content = _isDarkTheme ? "☀️" : "🌙";
            
            // Save the theme preference
            SaveSettings();
            
            StatusBarText.Text = $"Switched to {(_isDarkTheme ? "dark" : "light")} theme";
        }
        
        private void LoadSettings()
        {
            try
            {
                if (File.Exists(SETTINGS_FILE))
                {
                    var doc = XDocument.Load(SETTINGS_FILE);
                    var root = doc.Root;
                    
                    if (root != null)
                    {
                        _appSettings.DefaultDarkTheme = bool.Parse(root.Element("DefaultDarkTheme")?.Value ?? "false");
                        _appSettings.AutoRefreshEnabled = bool.Parse(root.Element("AutoRefreshEnabled")?.Value ?? "false");
                        _appSettings.AutoScrollEnabled = bool.Parse(root.Element("AutoScrollEnabled")?.Value ?? "true");
                        _appSettings.MaxDisplayedEntries = int.Parse(root.Element("MaxDisplayedEntries")?.Value ?? "10000");
                    }
                }
            }
            catch (Exception ex)
            {
                // If loading fails, use defaults
                _appSettings = new AppSettings();
            }
        }
        
        private void SaveSettings()
        {
            try
            {
                var doc = new XDocument(
                    new XElement("Settings",
                        new XElement("DefaultDarkTheme", _appSettings.DefaultDarkTheme),
                        new XElement("AutoRefreshEnabled", _appSettings.AutoRefreshEnabled),
                        new XElement("AutoScrollEnabled", _appSettings.AutoScrollEnabled),
                        new XElement("MaxDisplayedEntries", _appSettings.MaxDisplayedEntries)
                    )
                );
                
                doc.Save(SETTINGS_FILE);
            }
            catch (Exception ex)
            {
                // Ignore save errors
            }
        }
        
        private void ApplyTheme()
        {
            try
            {
                if (_isDarkTheme)
                {
                    // Dark theme
                    this.Background = new SolidColorBrush(Color.FromRgb(32, 32, 32));
                    this.Foreground = new SolidColorBrush(Colors.White);
                    
                    // Update ListView background
                    LogEntriesListView.Background = new SolidColorBrush(Color.FromRgb(24, 24, 24));
                    LogEntriesListView.Foreground = new SolidColorBrush(Colors.LightGreen);
                    
                    // Update DataGrid background and styling
                    LogFilesDataGrid.Background = new SolidColorBrush(Color.FromRgb(40, 40, 40));
                    LogFilesDataGrid.Foreground = new SolidColorBrush(Colors.White);
                    
                    // Update DataGrid row background
                    LogFilesDataGrid.RowBackground = new SolidColorBrush(Color.FromRgb(45, 45, 45));
                    LogFilesDataGrid.AlternatingRowBackground = new SolidColorBrush(Color.FromRgb(50, 50, 50));
                    
                    // Update DataGrid header styling
                    var headerStyle = new Style(typeof(DataGridColumnHeader));
                    headerStyle.Setters.Add(new Setter(DataGridColumnHeader.BackgroundProperty, new SolidColorBrush(Color.FromRgb(60, 60, 60))));
                    headerStyle.Setters.Add(new Setter(DataGridColumnHeader.ForegroundProperty, new SolidColorBrush(Colors.White)));
                    headerStyle.Setters.Add(new Setter(DataGridColumnHeader.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(80, 80, 80))));
                    LogFilesDataGrid.ColumnHeaderStyle = headerStyle;
                    
                    // Update DataGrid cell styling
                    var cellStyle = new Style(typeof(DataGridCell));
                    cellStyle.Setters.Add(new Setter(DataGridCell.BackgroundProperty, new SolidColorBrush(Colors.Transparent)));
                    cellStyle.Setters.Add(new Setter(DataGridCell.ForegroundProperty, new SolidColorBrush(Colors.White)));
                    cellStyle.Setters.Add(new Setter(DataGridCell.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(60, 60, 60))));
                    LogFilesDataGrid.CellStyle = cellStyle;
                    
                    // Update status bar
                    var statusBar = this.FindName("StatusBar") as System.Windows.Controls.Primitives.StatusBar;
                    if (statusBar != null)
                    {
                        statusBar.Background = new SolidColorBrush(Color.FromRgb(48, 48, 48));
                        statusBar.Foreground = new SolidColorBrush(Colors.White);
                    }
                }
                else
                {
                    // Light theme
                    this.Background = new SolidColorBrush(Colors.White);
                    this.Foreground = new SolidColorBrush(Colors.Black);
                    
                    // Update ListView background
                    LogEntriesListView.Background = new SolidColorBrush(Colors.White);
                    LogEntriesListView.Foreground = new SolidColorBrush(Colors.Black);
                    
                    // Update DataGrid background and styling
                    LogFilesDataGrid.Background = new SolidColorBrush(Colors.White);
                    LogFilesDataGrid.Foreground = new SolidColorBrush(Colors.Black);
                    
                    // Update DataGrid row background
                    LogFilesDataGrid.RowBackground = new SolidColorBrush(Colors.White);
                    LogFilesDataGrid.AlternatingRowBackground = new SolidColorBrush(Color.FromRgb(248, 248, 248));
                    
                    // Update DataGrid header styling
                    var headerStyle = new Style(typeof(DataGridColumnHeader));
                    headerStyle.Setters.Add(new Setter(DataGridColumnHeader.BackgroundProperty, new SolidColorBrush(Color.FromRgb(230, 230, 230))));
                    headerStyle.Setters.Add(new Setter(DataGridColumnHeader.ForegroundProperty, new SolidColorBrush(Colors.Black)));
                    headerStyle.Setters.Add(new Setter(DataGridColumnHeader.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(200, 200, 200))));
                    LogFilesDataGrid.ColumnHeaderStyle = headerStyle;
                    
                    // Update DataGrid cell styling
                    var cellStyle = new Style(typeof(DataGridCell));
                    cellStyle.Setters.Add(new Setter(DataGridCell.BackgroundProperty, new SolidColorBrush(Colors.Transparent)));
                    cellStyle.Setters.Add(new Setter(DataGridCell.ForegroundProperty, new SolidColorBrush(Colors.Black)));
                    cellStyle.Setters.Add(new Setter(DataGridCell.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(220, 220, 220))));
                    LogFilesDataGrid.CellStyle = cellStyle;
                    
                    // Update status bar
                    var statusBar = this.FindName("StatusBar") as System.Windows.Controls.Primitives.StatusBar;
                    if (statusBar != null)
                    {
                        statusBar.Background = new SolidColorBrush(Color.FromRgb(240, 240, 240));
                        statusBar.Foreground = new SolidColorBrush(Colors.Black);
                    }
                }
            }
            catch (Exception ex)
            {
                // Ignore theme application errors
            }
        }
        
        #endregion
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
    
    [Serializable]
    public class FilterPreset
    {
        public string Name { get; set; }
        public string SearchText { get; set; }
        public bool IsRegex { get; set; }
        public bool IsCaseSensitive { get; set; }
        public string SearchColumn { get; set; }
        public HashSet<string> ActiveLogLevels { get; set; } = new HashSet<string>();
        public int TimeFilterSeconds { get; set; } = 30;
    }

    public class FilterSettings
    {
        public string Name { get; set; }
        public string SearchText { get; set; }
        public bool IsRegex { get; set; }
        public bool IsCaseSensitive { get; set; }
        public string SelectedColumn { get; set; }
        public HashSet<string> ActiveLogLevels { get; set; } = new HashSet<string>();
        public bool HasTimeFilter { get; set; }
        public int TimeFilterSeconds { get; set; }
    }
    
    public class AppSettings
    {
        public bool DefaultDarkTheme { get; set; } = false;
        public bool AutoRefreshEnabled { get; set; } = false;
        public bool AutoScrollEnabled { get; set; } = true;
        public int MaxDisplayedEntries { get; set; } = 10000;
    }
}