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
using System.Diagnostics;
using System.Threading;
using System.Reflection;

namespace LogViewer
{
    // Plugin interface for custom log parsers
    public interface ILogParser
    {
        string Name { get; }
        string Description { get; }
        string[] SupportedExtensions { get; }
        bool CanParse(string filePath);
        IEnumerable<LogEntry> Parse(string filePath, CancellationToken cancellationToken = default);
    }

    // Built-in standard log parser
    public class StandardLogParser : ILogParser
    {
        public string Name => "Standard Log Parser";
        public string Description => "Parses standard text-based log files";
        public string[] SupportedExtensions => new[] { ".log", ".txt", ".out" };

        public bool CanParse(string filePath)
        {
            var ext = Path.GetExtension(filePath).ToLower();
            return SupportedExtensions.Contains(ext);
        }

        public IEnumerable<LogEntry> Parse(string filePath, CancellationToken cancellationToken = default)
        {
            using var reader = new StreamReader(filePath);
            string line;
            int lineNumber = 0;

            while ((line = reader.ReadLine()) != null)
            {
                if (cancellationToken.IsCancellationRequested)
                    yield break;

                if (string.IsNullOrWhiteSpace(line)) continue;

                lineNumber++;
                var timestamp = EnhancedTimestampParser.ParseTimestamp(line);

                yield return new LogEntry
                {
                    Content = line,
                    LineNumber = lineNumber,
                    Timestamp = timestamp ?? DateTime.MinValue,
                    HasTimestamp = timestamp.HasValue
                };
            }
        }
    }

    // Enhanced timestamp parser with multiple format support
    public static class EnhancedTimestampParser
    {
        private static readonly List<TimestampPattern> Patterns = new()
        {
            // ISO 8601 formats
            new TimestampPattern(
                @"(\d{4}-\d{2}-\d{2}[T\s]\d{2}:\d{2}:\d{2}(?:\.\d{3})?(?:Z|[+-]\d{2}:\d{2})?)",
                "yyyy-MM-dd HH:mm:ss", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-dd HH:mm:ss.fff"),
            
            // Standard syslog format
            new TimestampPattern(
                @"(\w{3}\s+\d{1,2}\s+\d{2}:\d{2}:\d{2})",
                "MMM dd HH:mm:ss", "MMM  d HH:mm:ss"),
            
            // US format
            new TimestampPattern(
                @"(\d{1,2}/\d{1,2}/\d{4}\s+\d{1,2}:\d{2}:\d{2}(?:\s?[AP]M)?)",
                "M/d/yyyy h:mm:ss tt", "M/d/yyyy HH:mm:ss", "MM/dd/yyyy HH:mm:ss"),
            
            // European format
            new TimestampPattern(
                @"(\d{1,2}\.\d{1,2}\.\d{4}\s+\d{2}:\d{2}:\d{2})",
                "d.M.yyyy HH:mm:ss", "dd.MM.yyyy HH:mm:ss"),
            
            // Apache log format
            new TimestampPattern(
                @"(\d{2}/\w{3}/\d{4}:\d{2}:\d{2}:\d{2}\s+[+-]\d{4})",
                "dd/MMM/yyyy:HH:mm:ss zzz"),
            
            // IIS log format
            new TimestampPattern(
                @"(\d{4}-\d{2}-\d{2}\s+\d{2}:\d{2}:\d{2})",
                "yyyy-MM-dd HH:mm:ss"),
            
            // Custom application format with milliseconds
            new TimestampPattern(
                @"(\d{4}-\d{2}-\d{2}\s+\d{2}:\d{2}:\d{2}\.\d{3})",
                "yyyy-MM-dd HH:mm:ss.fff")
        };

        public static DateTime? ParseTimestamp(string line)
        {
            if (string.IsNullOrEmpty(line)) return null;

            foreach (var pattern in Patterns)
            {
                var match = pattern.Regex.Match(line);
                if (match.Success)
                {
                    var timestampStr = match.Groups[1].Value;
                    
                    foreach (var format in pattern.Formats)
                    {
                        if (DateTime.TryParseExact(timestampStr, format, CultureInfo.InvariantCulture, 
                            DateTimeStyles.None, out DateTime result))
                        {
                            // If no year specified, assume current year
                            if (result.Year == 1)
                            {
                                result = result.AddYears(DateTime.Now.Year - 1);
                            }
                            return result;
                        }
                    }
                }
            }

            return null;
        }
    }

    public class TimestampPattern
    {
        public Regex Regex { get; }
        public string[] Formats { get; }

        public TimestampPattern(string pattern, params string[] formats)
        {
            Regex = new Regex(pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase);
            Formats = formats;
        }
    }

    // Performance monitoring
    public class PerformanceMonitor
    {
        private readonly PerformanceCounter _cpuCounter;
        private readonly PerformanceCounter _memoryCounter;
        private readonly Timer _updateTimer;

        public event Action<PerformanceMetrics> MetricsUpdated;

        public PerformanceMonitor()
        {
            try
            {
                _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
                _memoryCounter = new PerformanceCounter("Memory", "Available MBytes");
                _updateTimer = new Timer(UpdateMetrics, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to initialize performance counters: {ex.Message}");
            }
        }

        private void UpdateMetrics(object state)
        {
            try
            {
                var metrics = new PerformanceMetrics
                {
                    CpuUsage = _cpuCounter?.NextValue() ?? 0,
                    AvailableMemoryMB = _memoryCounter?.NextValue() ?? 0,
                    AppMemoryUsageMB = GC.GetTotalMemory(false) / (1024 * 1024),
                    ThreadCount = Process.GetCurrentProcess().Threads.Count
                };

                MetricsUpdated?.Invoke(metrics);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to update performance metrics: {ex.Message}");
            }
        }

        public void Dispose()
        {
            _updateTimer?.Dispose();
            _cpuCounter?.Dispose();
            _memoryCounter?.Dispose();
        }
    }

    public class PerformanceMetrics
    {
        public float CpuUsage { get; set; }
        public float AvailableMemoryMB { get; set; }
        public long AppMemoryUsageMB { get; set; }
        public int ThreadCount { get; set; }
    }

    // Log analytics
    public class LogAnalytics
    {
        public static LogAnalyticsResult AnalyzeLogEntries(IEnumerable<LogEntry> entries)
        {
            var entriesList = entries.ToList();
            var result = new LogAnalyticsResult();

            // Log level distribution
            result.LogLevelDistribution = entriesList
                .GroupBy(e => ExtractLogLevel(e.Content))
                .ToDictionary(g => g.Key, g => g.Count());

            // Timeline analysis
            var timelineEntries = entriesList
                .Where(e => e.HasTimestamp)
                .OrderBy(e => e.Timestamp)
                .ToList();

            if (timelineEntries.Any())
            {
                result.FirstLogTime = timelineEntries.First().Timestamp;
                result.LastLogTime = timelineEntries.Last().Timestamp;
                result.TimeSpan = result.LastLogTime - result.FirstLogTime;

                // Entries per hour
                result.EntriesPerHour = timelineEntries
                    .GroupBy(e => e.Timestamp.ToString("yyyy-MM-dd HH:00"))
                    .ToDictionary(g => g.Key, g => g.Count());

                // Peak activity detection
                var maxEntriesPerHour = result.EntriesPerHour.Values.Max();
                result.PeakActivityHours = result.EntriesPerHour
                    .Where(kvp => kvp.Value == maxEntriesPerHour)
                    .Select(kvp => kvp.Key)
                    .ToList();
            }

            // Error pattern analysis
            result.ErrorPatterns = AnalyzeErrorPatterns(entriesList);

            // Performance metrics
            result.TotalEntries = entriesList.Count;
            result.AverageLineLength = entriesList.Average(e => e.Content.Length);
            result.UniqueMessages = entriesList.Select(e => e.Content).Distinct().Count();

            return result;
        }

        private static string ExtractLogLevel(string content)
        {
            var upperContent = content.ToUpper();
            if (upperContent.Contains("FATAL") || upperContent.Contains("CRITICAL")) return "FATAL";
            if (upperContent.Contains("ERROR") || upperContent.Contains("ERR")) return "ERROR";
            if (upperContent.Contains("WARN") || upperContent.Contains("WARNING")) return "WARN";
            if (upperContent.Contains("INFO")) return "INFO";
            if (upperContent.Contains("DEBUG") || upperContent.Contains("DBG")) return "DEBUG";
            if (upperContent.Contains("TRACE")) return "TRACE";
            return "UNKNOWN";
        }

        private static List<ErrorPattern> AnalyzeErrorPatterns(List<LogEntry> entries)
        {
            var errorEntries = entries.Where(e => 
                e.Content.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                e.Content.Contains("exception", StringComparison.OrdinalIgnoreCase) ||
                e.Content.Contains("fail", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var patterns = new List<ErrorPattern>();

            // Group by similar error messages
            var errorGroups = errorEntries
                .GroupBy(e => ExtractErrorSignature(e.Content))
                .Where(g => g.Count() > 1)
                .OrderByDescending(g => g.Count())
                .Take(10);

            foreach (var group in errorGroups)
            {
                patterns.Add(new ErrorPattern
                {
                    Pattern = group.Key,
                    Count = group.Count(),
                    FirstOccurrence = group.Min(e => e.Timestamp),
                    LastOccurrence = group.Max(e => e.Timestamp),
                    ExampleMessage = group.First().Content
                });
            }

            return patterns;
        }

        private static string ExtractErrorSignature(string content)
        {
            // Extract error signature by removing variable parts
            var signature = content;
            
            // Remove timestamps
            signature = Regex.Replace(signature, @"\d{4}-\d{2}-\d{2}[\sT]\d{2}:\d{2}:\d{2}", "[TIMESTAMP]");
            signature = Regex.Replace(signature, @"\w{3}\s+\d{1,2}\s+\d{2}:\d{2}:\d{2}", "[TIMESTAMP]");
            
            // Remove numbers that might be variable
            signature = Regex.Replace(signature, @"\b\d+\b", "[NUM]");
            
            // Remove GUIDs
            signature = Regex.Replace(signature, @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}", "[GUID]");
            
            // Remove IP addresses
            signature = Regex.Replace(signature, @"\b(?:\d{1,3}\.){3}\d{1,3}\b", "[IP]");
            
            // Remove file paths
            signature = Regex.Replace(signature, @"[A-Za-z]:\\[^\s]+", "[PATH]");
            
            return signature.Trim();
        }
    }

    public class LogAnalyticsResult
    {
        public Dictionary<string, int> LogLevelDistribution { get; set; } = new();
        public DateTime FirstLogTime { get; set; }
        public DateTime LastLogTime { get; set; }
        public TimeSpan TimeSpan { get; set; }
        public Dictionary<string, int> EntriesPerHour { get; set; } = new();
        public List<string> PeakActivityHours { get; set; } = new();
        public List<ErrorPattern> ErrorPatterns { get; set; } = new();
        public int TotalEntries { get; set; }
        public double AverageLineLength { get; set; }
        public int UniqueMessages { get; set; }
    }

    public class ErrorPattern
    {
        public string Pattern { get; set; }
        public int Count { get; set; }
        public DateTime FirstOccurrence { get; set; }
        public DateTime LastOccurrence { get; set; }
        public string ExampleMessage { get; set; }
    }

    // Plugin manager
    public class PluginManager
    {
        private readonly List<ILogParser> _parsers = new();

        public PluginManager()
        {
            // Load built-in parsers
            _parsers.Add(new StandardLogParser());
            
            // Load external plugins
            LoadExternalPlugins();
        }

        private void LoadExternalPlugins()
        {
            try
            {
                var pluginPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plugins");
                if (!Directory.Exists(pluginPath)) return;

                var pluginFiles = Directory.GetFiles(pluginPath, "*.dll");
                foreach (var file in pluginFiles)
                {
                    try
                    {
                        var assembly = Assembly.LoadFrom(file);
                        var parserTypes = assembly.GetTypes()
                            .Where(t => typeof(ILogParser).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);

                        foreach (var type in parserTypes)
                        {
                            var parser = (ILogParser)Activator.CreateInstance(type);
                            _parsers.Add(parser);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Failed to load plugin {file}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to load plugins: {ex.Message}");
            }
        }

        public ILogParser GetParser(string filePath)
        {
            return _parsers.FirstOrDefault(p => p.CanParse(filePath)) ?? _parsers.First();
        }

        public IEnumerable<ILogParser> GetAllParsers() => _parsers;
    }

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
        
        // Enhanced fields
        private HashSet<string> _activeLogLevelFilters = new HashSet<string>();
        private List<string> _recentFiles = new List<string>();
        private const string RECENT_FILES_FILE = "recent_files.txt";
        private const string FILTER_PRESETS_FILE = "filter_presets.xml";
        private const string SETTINGS_FILE = "settings.xml";
        private const int MAX_RECENT_FILES = 10;
        private const int MAX_DISPLAYED_ENTRIES = 10000;
        private bool _isDarkTheme = false;
        private List<FilterPreset> _filterPresets = new List<FilterPreset>();
        private bool _autoRefreshEnabled = false;
        private AppSettings _appSettings = new AppSettings();

        // New enhancement fields
        private PerformanceMonitor _performanceMonitor;
        private PluginManager _pluginManager;
        private LogAnalyticsResult _currentAnalytics;
        private CancellationTokenSource _loadingCancellationToken;

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
            InitializeEnhancements();
            
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

        private void InitializeEnhancements()
        {
            // Initialize performance monitoring
            _performanceMonitor = new PerformanceMonitor();
            _performanceMonitor.MetricsUpdated += OnPerformanceMetricsUpdated;
            
            // Initialize plugin manager
            _pluginManager = new PluginManager();
            
            // Initialize cancellation token
            _loadingCancellationToken = new CancellationTokenSource();
        }

        private void OnPerformanceMetricsUpdated(PerformanceMetrics metrics)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    var memoryText = $"Memory: {metrics.AppMemoryUsageMB:N0} MB";
                    var cpuText = $"CPU: {metrics.CpuUsage:F1}%";
                    var entriesText = $"Entries: {_logEntries.Count:N0}";
                    
                    StatusBarText.Text = $"Ready | {memoryText} | {cpuText} | {entriesText}";
                    
                    // Update performance warning if needed
                    if (metrics.AppMemoryUsageMB > 1000) // 1GB threshold
                    {
                        StatusBarText.Foreground = Brushes.Orange;
                    }
                    else if (metrics.AppMemoryUsageMB > 2000) // 2GB threshold
                    {
                        StatusBarText.Foreground = Brushes.Red;
                    }
                    else
                    {
                        StatusBarText.Foreground = _isDarkTheme ? Brushes.White : Brushes.Black;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error updating performance metrics: {ex.Message}");
                }
            }));
        }

        private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.O && Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                {
                    LoadSingleFile_Click(sender, new RoutedEventArgs());
                }
                else
                {
                    LoadFolder_Click(sender, new RoutedEventArgs());
                }
                e.Handled = true;
            }
            else if (e.Key == System.Windows.Input.Key.A && Keyboard.Modifiers == ModifierKeys.Control)
            {
                // Ctrl+A: Show analytics
                ShowAnalytics_Click(sender, new RoutedEventArgs());
                e.Handled = true;
            }
            else if (e.Key == System.Windows.Input.Key.P && Keyboard.Modifiers == ModifierKeys.Control)
            {
                // Ctrl+P: Show plugins
                ShowPlugins_Click(sender, new RoutedEventArgs());
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
                _loadingCancellationToken = new CancellationTokenSource();
                
                try
                {
                    await LoadLogFilesAsync(new[] { fileDialog.FileName }, _loadingCancellationToken.Token);
                }
                catch (OperationCanceledException)
                {
                    StatusBarText.Text = "Loading cancelled";
                }
                finally
                {
                    LoadingIndicator.Visibility = Visibility.Collapsed;
                }
                
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
                    _loadingCancellationToken = new CancellationTokenSource();
                    
                    try
                    {
                        await LoadLogFilesAsync(files.ToArray(), _loadingCancellationToken.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        StatusBarText.Text = "Loading cancelled";
                    }
                    finally
                    {
                        LoadingIndicator.Visibility = Visibility.Collapsed;
                    }
                    
                    AddToRecentFiles(folderDialog.SelectedPath);
                    UpdateRecentFilesDropdown();
                }
            }
        }

        private async Task LoadLogFilesAsync(string[] filePaths, CancellationToken cancellationToken = default)
        {
            var colorBrushes = new[]
            {
                Brushes.LightBlue, Brushes.LightGreen, Brushes.LightPink, Brushes.LightYellow,
                Brushes.LightCyan, Brushes.LightGray, Brushes.LightSalmon, Brushes.LightSeaGreen,
                Brushes.LightSkyBlue, Brushes.LightSlateGray, Brushes.LightSteelBlue, Brushes.LightGoldenrodYellow,
                Brushes.Lavender, Brushes.LemonChiffon, Brushes.PaleGreen, Brushes.PaleTurquoise,
                Brushes.PaleVioletRed, Brushes.PapayaWhip, Brushes.PeachPuff, Brushes.Peru
            };

            var tasks = new List<Task>();

            foreach (var filePath in filePaths)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

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

                // Load file content asynchronously with enhanced parsing
                var task = Task.Run(async () => await LoadFileContentEnhanced(logFile, cancellationToken), cancellationToken);
                tasks.Add(task);
                
                // Set up file watcher for auto-reload
                SetupFileWatcher(filePath);
            }

            // Wait for all files to load
            try
            {
                await Task.WhenAll(tasks);
            }
            catch (OperationCanceledException)
            {
                // Handle cancellation gracefully
                return;
            }

            // Generate analytics
            _currentAnalytics = LogAnalytics.AnalyzeLogEntries(_logEntries);

            RefreshLogEntries();
            AutoSaveConfiguration();
        }

        private async Task LoadFileContentEnhanced(LogFile logFile, CancellationToken cancellationToken = default)
        {
            try
            {
                // Get file size
                var fileInfo = new FileInfo(logFile.FilePath);
                logFile.FileSize = fileInfo.Length;

                // Get appropriate parser
                var parser = _pluginManager.GetParser(logFile.FilePath);
                
                var entries = parser.Parse(logFile.FilePath, cancellationToken);
                int lineCount = 0;
                bool showedWarning = false;

                foreach (var entry in entries)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    lineCount++;
                    
                    // Check line rate limit
                    if (lineCount > _maxLinesPerFile && _showLineRateWarning && !showedWarning)
                    {
                        showedWarning = true;
                        var continueLoading = await Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            var result = MessageBox.Show(
                                $"File '{logFile.Name}' has more than {_maxLinesPerFile} lines.\n\n" +
                                $"Loading large files may impact performance. Continue loading this file?\n\n" +
                                $"You can adjust the line limit in Settings.",
                                "Line Rate Limit Warning",
                                MessageBoxButton.YesNo,
                                MessageBoxImage.Warning);
                            
                            return result == MessageBoxResult.Yes;
                        });
                        
                        if (!continueLoading)
                            break;
                    }

                    // Set additional properties
                    entry.FileId = logFile.Id;
                    entry.Alias = logFile.Alias;
                    entry.Color = logFile.Color;
                    entry.HighlightedContent = GenerateHighlightedContent(entry.Content);

                    Application.Current.Dispatcher.Invoke(() => _logEntries.Add(entry));
                }

                Application.Current.Dispatcher.Invoke(() => 
                {
                    logFile.LineCount = lineCount;
                    UpdateStatusBar();
                });
            }
            catch (OperationCanceledException)
            {
                // Handle cancellation gracefully
                throw;
            }
            catch (Exception ex)
            {
                Application.Current.Dispatcher.Invoke(() => 
                    MessageBox.Show($"Error loading file {logFile.Name}: {ex.Message}"));
            }
        }

        private void ShowAnalytics_Click(object sender, RoutedEventArgs e)
        {
            if (_currentAnalytics == null || _logEntries.Count == 0)
            {
                MessageBox.Show("No log data available for analysis. Please load some log files first.", 
                    "No Data", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var analyticsWindow = new AnalyticsWindow(_currentAnalytics);
            analyticsWindow.ShowDialog();
        }

        private void ShowPlugins_Click(object sender, RoutedEventArgs e)
        {
            var pluginsWindow = new PluginsWindow(_pluginManager);
            pluginsWindow.ShowDialog();
        }

        private void ShowLogLevelDistribution_Click(object sender, RoutedEventArgs e)
        {
            if (_currentAnalytics?.LogLevelDistribution == null || !_currentAnalytics.LogLevelDistribution.Any())
            {
                MessageBox.Show("No log level data available for analysis.", 
                    "No Data", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var distributionWindow = new LogLevelDistributionWindow(_currentAnalytics.LogLevelDistribution);
            distributionWindow.ShowDialog();
        }

        // Continue with existing methods...
        private DateTime? ParseTimestamp(string line)
        {
            return EnhancedTimestampParser.ParseTimestamp(line);
        }

        private void RefreshLogEntries()
        {
            // Regenerate highlighted content for all entries
            foreach (var entry in _logEntries)
            {
                entry.HighlightedContent = GenerateHighlightedContent(entry.Content);
            }

            // Regenerate analytics
            if (_logEntries.Any())
            {
                _currentAnalytics = LogAnalytics.AnalyzeLogEntries(_logEntries);
            }

            ApplyTimeFilter();
        }

        // Add method to cancel loading
        private void CancelLoading_Click(object sender, RoutedEventArgs e)
        {
            _loadingCancellationToken?.Cancel();
            StatusBarText.Text = "Cancelling loading...";
        }

        protected override void OnClosed(EventArgs e)
        {
            // Cleanup resources
            _loadingCancellationToken?.Cancel();
            _performanceMonitor?.Dispose();
            CleanupFileWatchers();
            SaveRecentFiles();
            base.OnClosed(e);
        }

        // ... [Rest of the existing methods remain the same] ...
        
        private async Task LoadLogFileAsync(LogFile logFile)
        {
            await LoadFileContentEnhanced(logFile, CancellationToken.None);
        }

        private void UpdateStatusBar()
        {
            try
            {
                var visibleFiles = _logFiles.Count(f => f.IsVisible);
                var totalLines = _logEntries.Count(e => _logFiles.First(f => f.Id == e.FileId).IsVisible);
                
                StatusBarText.Text = "Ready";
                
                if (FilesCountText != null)
                    FilesCountText.Text = $"Files: {_logFiles.Count}";
                if (EntriesCountText != null)
                    EntriesCountText.Text = $"Entries: {_logEntries.Count}";
                
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
                    if (_activeLogLevelFilters != null && _activeLogLevelFilters.Count < 4)
                    {
                        activeFilters.Add($"Levels: {string.Join(",", _activeLogLevelFilters)}");
                    }
                    
                    FilterStatusText.Text = activeFilters.Count > 0 ? string.Join(", ", activeFilters) : "No filters";
                }
                else
                {
                    StatusBarText.Text = $"Files: {_logFiles.Count} | Visible: {visibleFiles} | Lines: {totalLines} | Sorted chronologically";
                }
            }
            catch (Exception ex)
            {
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
            _currentAnalytics = null;
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
                    LoadDefaultKeywords();
                }

                _logFiles.Clear();
                _logEntries.Clear();

                LoadingIndicator.Visibility = Visibility.Visible;
                _loadingCancellationToken = new CancellationTokenSource();

                var colorBrushes = new[]
                {
                    Brushes.LightBlue, Brushes.LightGreen, Brushes.LightPink, Brushes.LightYellow,
                    Brushes.LightCyan, Brushes.LightGray, Brushes.LightSalmon, Brushes.LightSeaGreen,
                    Brushes.LightSkyBlue, Brushes.LightSlateGray, Brushes.LightSteelBlue, Brushes.LightGoldenrodYellow,
                    Brushes.Lavender, Brushes.LemonChiffon, Brushes.PaleGreen, Brushes.PaleTurquoise,
                    Brushes.PaleVioletRed, Brushes.PapayaWhip, Brushes.PeachPuff, Brushes.Peru
                };

                int colorIndex = 0;
                var loadTasks = new List<Task>();

                foreach (var element in logFileElements)
                {
                    if (_loadingCancellationToken.Token.IsCancellationRequested)
                        break;

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

                    var task = Task.Run(async () => await LoadFileContentEnhanced(logFile, _loadingCancellationToken.Token));
                    loadTasks.Add(task);
                }

                await Task.WhenAll(loadTasks);

                LoadingIndicator.Visibility = Visibility.Collapsed;
                RefreshLogEntries();
                MessageBox.Show($"Configuration loaded from {filePath}", "Load Successful", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (OperationCanceledException)
            {
                LoadingIndicator.Visibility = Visibility.Collapsed;
                MessageBox.Show("Loading cancelled.", "Load Cancelled", MessageBoxButton.OK, MessageBoxImage.Information);
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
            
            if (currentIndex < content.Length)
            {
                separators.Add(content.Substring(currentIndex));
            }
            else
            {
                separators.Add("");
            }

            for (int i = 0; i < words.Length; i++)
            {
                if (i < separators.Count && !string.IsNullOrEmpty(separators[i]))
                {
                    segments.Add(new TextSegment { Text = separators[i], IsHighlighted = false });
                }
                
                if (!string.IsNullOrEmpty(words[i]))
                {
                    bool isKeyword = _highlightKeywords.Contains(words[i]);
                    segments.Add(new TextSegment { Text = words[i], IsHighlighted = isKeyword });
                }
            }
            
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
                
                if (logFile?.IsVisible != true)
                    return false;
                
                if (!string.IsNullOrEmpty(SearchTextBox.Text))
                {
                    if (!logEntry.Content.Contains(SearchTextBox.Text, StringComparison.OrdinalIgnoreCase))
                        return false;
                }
                
                if (_timeFilterCenter.HasValue)
                {
                    var timeDiff = Math.Abs((logEntry.Timestamp - _timeFilterCenter.Value).TotalSeconds);
                    return timeDiff <= _timeFilterSeconds;
                }
                
                return true;
            };
            
            _logEntriesView.View.Refresh();
            UpdateStatusBar();
            
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
                System.Diagnostics.Debug.WriteLine($"Failed to setup file watcher for {filePath}: {ex.Message}");
            }
        }

        private async Task ReloadLogFile(string filePath)
        {
            try
            {
                var logFile = _logFiles.FirstOrDefault(f => f.FilePath == filePath);
                if (logFile == null) return;

                var existingEntries = _logEntries.Where(e => e.FileId == logFile.Id).ToList();
                foreach (var entry in existingEntries)
                {
                    _logEntries.Remove(entry);
                }

                await LoadFileContentEnhanced(logFile, CancellationToken.None);
                
                ApplyTimeFilter();
                
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
                            
                            if (!string.IsNullOrEmpty(SearchTextBox.Text))
                            {
                                if (!entry.Content.Contains(SearchTextBox.Text, StringComparison.OrdinalIgnoreCase))
                                    return false;
                            }
                            
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
                    
                    if (!string.IsNullOrEmpty(SearchText