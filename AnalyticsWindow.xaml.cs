using System;
using System.Linq;
using System.Windows;
using System.Collections.Generic;

namespace LogViewer
{
    public partial class AnalyticsWindow : Window
    {
        private LogAnalyticsResult _analytics;

        public AnalyticsWindow(LogAnalyticsResult analytics)
        {
            InitializeComponent();
            _analytics = analytics;
            LoadAnalytics();
        }

        private void LoadAnalytics()
        {
            // Load overview statistics
            TotalEntriesText.Text = _analytics.TotalEntries.ToString("N0");
            TimeSpanText.Text = _analytics.TimeSpan.TotalDays > 1 
                ? $"{_analytics.TimeSpan.TotalDays:F1} days" 
                : $"{_analytics.TimeSpan.TotalHours:F1} hours";
            AvgLineLengthText.Text = $"{_analytics.AverageLineLength:F1} chars";
            UniqueMessagesText.Text = _analytics.UniqueMessages.ToString("N0");

            // Load time range
            if (_analytics.FirstLogTime != default && _analytics.LastLogTime != default)
            {
                FirstLogTimeText.Text = _analytics.FirstLogTime.ToString("yyyy-MM-dd HH:mm:ss");
                LastLogTimeText.Text = _analytics.LastLogTime.ToString("yyyy-MM-dd HH:mm:ss");
            }

            // Load peak activity
            if (_analytics.PeakActivityHours.Any())
            {
                PeakActivityText.Text = string.Join(", ", _analytics.PeakActivityHours);
            }

            // Load log level distribution
            LoadLogLevelDistribution();

            // Load timeline
            LoadTimeline();

            // Load error patterns
            LoadErrorPatterns();
        }

        private void LoadLogLevelDistribution()
        {
            var totalEntries = _analytics.LogLevelDistribution.Values.Sum();
            var chartData = _analytics.LogLevelDistribution
                .OrderByDescending(kvp => kvp.Value)
                .Select(kvp => new LogLevelChartItem
                {
                    Level = kvp.Key,
                    Count = kvp.Value,
                    Percentage = totalEntries > 0 ? (double)kvp.Value / totalEntries * 100 : 0
                })
                .ToList();

            LogLevelChart.ItemsSource = chartData;
        }

        private void LoadTimeline()
        {
            var maxCount = _analytics.EntriesPerHour.Values.DefaultIfEmpty(0).Max();
            var timelineData = _analytics.EntriesPerHour
                .OrderBy(kvp => kvp.Key)
                .Select(kvp => new TimelineChartItem
                {
                    Hour = kvp.Key,
                    Count = kvp.Value,
                    Percentage = maxCount > 0 ? (double)kvp.Value / maxCount * 100 : 0
                })
                .ToList();

            TimelineChart.ItemsSource = timelineData;
        }

        private void LoadErrorPatterns()
        {
            ErrorPatternsGrid.ItemsSource = _analytics.ErrorPatterns.OrderByDescending(p => p.Count);
        }

        public class LogLevelChartItem
        {
            public string Level { get; set; }
            public int Count { get; set; }
            public double Percentage { get; set; }
        }

        public class TimelineChartItem
        {
            public string Hour { get; set; }
            public int Count { get; set; }
            public double Percentage { get; set; }
        }
    }
}