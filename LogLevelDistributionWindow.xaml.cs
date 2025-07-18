using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace LogViewer
{
    public partial class LogLevelDistributionWindow : Window
    {
        private Dictionary<string, int> _distribution;

        public LogLevelDistributionWindow(Dictionary<string, int> distribution)
        {
            InitializeComponent();
            _distribution = distribution;
            LoadDistribution();
        }

        private void LoadDistribution()
        {
            var totalEntries = _distribution.Values.Sum();
            var chartData = _distribution
                .OrderByDescending(kvp => kvp.Value)
                .Select(kvp => new LogLevelDistributionItem
                {
                    Level = kvp.Key,
                    Count = kvp.Value,
                    Percentage = totalEntries > 0 ? (double)kvp.Value / totalEntries * 100 : 0,
                    PercentageString = totalEntries > 0 ? $"{(double)kvp.Value / totalEntries * 100:F1}%" : "0%"
                })
                .ToList();

            ChartItemsControl.ItemsSource = chartData;
            SummaryText.Text = $"Total Entries: {totalEntries:N0}";
        }

        public class LogLevelDistributionItem
        {
            public string Level { get; set; }
            public int Count { get; set; }
            public double Percentage { get; set; }
            public string PercentageString { get; set; }
        }
    }
}