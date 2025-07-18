using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;

namespace LogViewer
{
    public partial class PluginsWindow : Window
    {
        private PluginManager _pluginManager;
        private List<PluginInfo> _pluginInfos;

        public PluginsWindow(PluginManager pluginManager)
        {
            InitializeComponent();
            _pluginManager = pluginManager;
            LoadPlugins();
        }

        private void LoadPlugins()
        {
            _pluginInfos = _pluginManager.GetAllParsers()
                .Select(parser => new PluginInfo
                {
                    Name = parser.Name,
                    Description = parser.Description,
                    SupportedExtensionsString = string.Join(", ", parser.SupportedExtensions),
                    Type = parser.GetType().Assembly.GetName().Name == "log_viewer" ? "Built-in" : "External"
                })
                .ToList();

            PluginsDataGrid.ItemsSource = _pluginInfos;
        }

        private void OpenPluginsFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var pluginPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plugins");
                Directory.CreateDirectory(pluginPath); // Create if it doesn't exist
                Process.Start("explorer.exe", pluginPath);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to open plugins folder: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void RefreshPlugins_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Note: In a real implementation, you'd reload the plugin manager
                // For now, we'll just refresh the display
                LoadPlugins();
                MessageBox.Show("Plugins refreshed successfully!", "Success", 
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to refresh plugins: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CreateSamplePlugin_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var sampleCode = @"using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using LogViewer;

public class SampleLogParser : ILogParser
{
    public string Name => ""Sample Log Parser"";
    public string Description => ""A sample parser for demonstration purposes"";
    public string[] SupportedExtensions => new[] { "".sample"", "".custom"" };

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
            
            // Custom parsing logic here
            // For example, parse timestamps, extract log levels, etc.
            
            yield return new LogEntry
            {
                Content = line,
                LineNumber = lineNumber,
                Timestamp = DateTime.Now, // Replace with actual timestamp parsing
                HasTimestamp = true
            };
        }
    }
}";

                var pluginPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plugins");
                Directory.CreateDirectory(pluginPath);
                var sampleFile = Path.Combine(pluginPath, "SamplePlugin.cs");
                
                File.WriteAllText(sampleFile, sampleCode);
                
                MessageBox.Show($"Sample plugin code created at:\n{sampleFile}\n\n" +
                    "To use this plugin:\n" +
                    "1. Create a new Class Library project\n" +
                    "2. Reference the main LogViewer assembly\n" +
                    "3. Implement the ILogParser interface\n" +
                    "4. Build the project and copy the DLL to the Plugins folder", 
                    "Sample Plugin Created", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to create sample plugin: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public class PluginInfo
        {
            public string Name { get; set; }
            public string Description { get; set; }
            public string SupportedExtensionsString { get; set; }
            public string Type { get; set; }
        }
    }
}