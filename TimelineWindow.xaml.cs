using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.Win32;
using System.IO;
using System.Text;

namespace LogViewer
{
    public partial class TimelineWindow : Window
    {
        private List<LogEntry> _logEntries;
        private Dictionary<int, int> _hourlyActivity;
        private int _maxActivity;
        
        public TimelineWindow(List<LogEntry> logEntries)
        {
            InitializeComponent();
            _logEntries = logEntries;
            AnalyzeTimeline();
            DrawTimeline();
        }
        
        private void AnalyzeTimeline()
        {
            _hourlyActivity = new Dictionary<int, int>();
            
            // Initialize all 24 hours
            for (int i = 0; i < 24; i++)
            {
                _hourlyActivity[i] = 0;
            }
            
            // Count entries per hour
            foreach (var entry in _logEntries.Where(e => e.HasTimestamp))
            {
                int hour = entry.Timestamp.Hour;
                _hourlyActivity[hour]++;
            }
            
            _maxActivity = _hourlyActivity.Values.DefaultIfEmpty(0).Max();
            
            // Update status
            int totalEntries = _logEntries.Count(e => e.HasTimestamp);
            TotalEntriesText.Text = $"Total Entries: {totalEntries}";
            
            if (_maxActivity > 0)
            {
                var peakHour = _hourlyActivity.OrderByDescending(kvp => kvp.Value).First();
                PeakHourText.Text = $"Peak Hour: {peakHour.Key:00}:00-{peakHour.Key:00}:59 ({peakHour.Value} entries)";
            }
        }
        
        private void DrawTimeline()
        {
            HourLabelsCanvas.Children.Clear();
            ChartCanvas.Children.Clear();
            
            if (_maxActivity == 0)
            {
                StatusText.Text = "No timestamped entries to display";
                return;
            }
            
            double barWidth = 30;
            double spacing = 10;
            double maxBarHeight = 350;
            double chartWidth = 24 * (barWidth + spacing);
            
            TimelineGrid.Width = chartWidth + 50;
            ChartCanvas.Width = chartWidth;
            
            // Draw hour labels and bars
            for (int hour = 0; hour < 24; hour++)
            {
                double x = hour * (barWidth + spacing) + 20;
                
                // Hour label
                TextBlock label = new TextBlock
                {
                    Text = $"{hour:00}",
                    FontSize = 10,
                    Width = barWidth,
                    TextAlignment = TextAlignment.Center
                };
                Canvas.SetLeft(label, x);
                Canvas.SetTop(label, 5);
                HourLabelsCanvas.Children.Add(label);
                
                // Activity bar
                int activity = _hourlyActivity[hour];
                if (activity > 0)
                {
                    double barHeight = (double)activity / _maxActivity * maxBarHeight;
                    
                    Rectangle bar = new Rectangle
                    {
                        Width = barWidth,
                        Height = barHeight,
                        Fill = GetBarColor(activity, _maxActivity),
                        Stroke = Brushes.DarkGray,
                        StrokeThickness = 1,
                        ToolTip = $"{hour:00}:00-{hour:00}:59\nEntries: {activity}"
                    };
                    
                    Canvas.SetLeft(bar, x);
                    Canvas.SetTop(bar, maxBarHeight - barHeight + 20);
                    ChartCanvas.Children.Add(bar);
                    
                    // Value label on top of bar
                    if (activity > 0)
                    {
                        TextBlock valueLabel = new TextBlock
                        {
                            Text = activity.ToString(),
                            FontSize = 10,
                            Width = barWidth,
                            TextAlignment = TextAlignment.Center
                        };
                        Canvas.SetLeft(valueLabel, x);
                        Canvas.SetTop(valueLabel, maxBarHeight - barHeight + 5);
                        ChartCanvas.Children.Add(valueLabel);
                    }
                }
            }
            
            // Draw grid lines
            for (int i = 0; i <= 5; i++)
            {
                double y = i * (maxBarHeight / 5) + 20;
                Line gridLine = new Line
                {
                    X1 = 0,
                    X2 = chartWidth,
                    Y1 = y,
                    Y2 = y,
                    Stroke = Brushes.LightGray,
                    StrokeThickness = 0.5,
                    StrokeDashArray = new DoubleCollection { 2, 2 }
                };
                ChartCanvas.Children.Add(gridLine);
                
                // Grid value label
                int value = _maxActivity - (i * _maxActivity / 5);
                TextBlock gridLabel = new TextBlock
                {
                    Text = value.ToString(),
                    FontSize = 10
                };
                Canvas.SetLeft(gridLabel, 0);
                Canvas.SetTop(gridLabel, y - 7);
                ChartCanvas.Children.Add(gridLabel);
            }
            
            StatusText.Text = "Timeline analysis complete";
        }
        
        private Brush GetBarColor(int activity, int maxActivity)
        {
            double ratio = (double)activity / maxActivity;
            
            if (ratio > 0.8)
                return Brushes.Red;
            else if (ratio > 0.6)
                return Brushes.Orange;
            else if (ratio > 0.4)
                return Brushes.Yellow;
            else if (ratio > 0.2)
                return Brushes.LightGreen;
            else
                return Brushes.LightBlue;
        }
        
        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            AnalyzeTimeline();
            DrawTimeline();
        }
        
        private void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            var saveDialog = new SaveFileDialog
            {
                Filter = "CSV files (*.csv)|*.csv|Text files (*.txt)|*.txt",
                DefaultExt = "csv",
                FileName = $"timeline_analysis_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
            };
            
            if (saveDialog.ShowDialog() == true)
            {
                try
                {
                    var content = new StringBuilder();
                    content.AppendLine("Hour,Count,Percentage");
                    
                    int total = _hourlyActivity.Values.Sum();
                    for (int hour = 0; hour < 24; hour++)
                    {
                        int count = _hourlyActivity[hour];
                        double percentage = total > 0 ? (double)count / total * 100 : 0;
                        content.AppendLine($"{hour:00}:00-{hour:00}:59,{count},{percentage:F2}%");
                    }
                    
                    File.WriteAllText(saveDialog.FileName, content.ToString());
                    MessageBox.Show($"Timeline analysis exported to {saveDialog.FileName}", "Export Successful", 
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error exporting timeline: {ex.Message}", "Export Error", 
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }
}