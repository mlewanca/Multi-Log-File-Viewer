using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace LogViewer
{
    public class ErrorPatternDetector
    {
        private readonly List<LogEntry> _logEntries;
        private readonly Dictionary<string, ErrorPattern> _patterns;
        
        public ErrorPatternDetector(List<LogEntry> logEntries)
        {
            _logEntries = logEntries;
            _patterns = new Dictionary<string, ErrorPattern>();
        }
        
        public List<ErrorPattern> DetectPatterns()
        {
            // Common error patterns to look for
            var errorPatterns = new List<(string Name, string Pattern)>
            {
                ("Exception", @"Exception|exception"),
                ("Stack Trace", @"at\s+\w+[\w\.<>]+\([^\)]*\)"),
                ("Null Reference", @"NullReferenceException|null reference|Object reference not set"),
                ("File Not Found", @"FileNotFoundException|Could not find file|File not found"),
                ("Connection Error", @"Connection.*failed|Unable to connect|Connection timeout"),
                ("Access Denied", @"Access.*denied|Unauthorized|Permission denied"),
                ("Out of Memory", @"OutOfMemoryException|Insufficient memory"),
                ("SQL Error", @"SQLException|SQL.*error|Database.*error"),
                ("Timeout", @"Timeout|timed out|Request timeout"),
                ("404 Error", @"404|Not Found"),
                ("500 Error", @"500|Internal Server Error"),
                ("Authentication Failed", @"Authentication.*failed|Login failed|Invalid credentials")
            };
            
            foreach (var (name, pattern) in errorPatterns)
            {
                var regex = new Regex(pattern, RegexOptions.IgnoreCase);
                var matches = new List<ErrorOccurrence>();
                
                foreach (var entry in _logEntries)
                {
                    if (regex.IsMatch(entry.Content))
                    {
                        matches.Add(new ErrorOccurrence
                        {
                            Timestamp = entry.Timestamp,
                            Content = entry.Content,
                            FileAlias = entry.Alias,
                            LogLevel = entry.LogLevel
                        });
                    }
                }
                
                if (matches.Count > 0)
                {
                    _patterns[name] = new ErrorPattern
                    {
                        PatternName = name,
                        Pattern = pattern,
                        Occurrences = matches,
                        Count = matches.Count,
                        FirstOccurrence = matches.Min(m => m.Timestamp),
                        LastOccurrence = matches.Max(m => m.Timestamp)
                    };
                }
            }
            
            // Detect custom patterns based on repeated error messages
            DetectCustomPatterns();
            
            return _patterns.Values.OrderByDescending(p => p.Count).ToList();
        }
        
        private void DetectCustomPatterns()
        {
            // Group similar error messages (ERROR level only)
            var errorEntries = _logEntries.Where(e => e.LogLevel == "ERROR").ToList();
            
            if (!errorEntries.Any()) return;
            
            // Extract error messages and normalize them
            var errorGroups = new Dictionary<string, List<ErrorOccurrence>>();
            
            foreach (var entry in errorEntries)
            {
                // Extract the core error message (remove timestamps, IDs, etc.)
                var normalizedMessage = NormalizeErrorMessage(entry.Content);
                
                if (!string.IsNullOrWhiteSpace(normalizedMessage))
                {
                    if (!errorGroups.ContainsKey(normalizedMessage))
                        errorGroups[normalizedMessage] = new List<ErrorOccurrence>();
                    
                    errorGroups[normalizedMessage].Add(new ErrorOccurrence
                    {
                        Timestamp = entry.Timestamp,
                        Content = entry.Content,
                        FileAlias = entry.Alias,
                        LogLevel = entry.LogLevel
                    });
                }
            }
            
            // Add recurring custom patterns
            foreach (var group in errorGroups.Where(g => g.Value.Count >= 3))
            {
                var patternName = $"Custom: {GetShortDescription(group.Key)}";
                if (!_patterns.ContainsKey(patternName))
                {
                    _patterns[patternName] = new ErrorPattern
                    {
                        PatternName = patternName,
                        Pattern = group.Key,
                        Occurrences = group.Value,
                        Count = group.Value.Count,
                        FirstOccurrence = group.Value.Min(m => m.Timestamp),
                        LastOccurrence = group.Value.Max(m => m.Timestamp)
                    };
                }
            }
        }
        
        private string NormalizeErrorMessage(string message)
        {
            // Remove common variable parts
            var normalized = Regex.Replace(message, @"\d{4}-\d{2}-\d{2}\s\d{2}:\d{2}:\d{2}", "");
            normalized = Regex.Replace(normalized, @"\b\d+\b", "#");
            normalized = Regex.Replace(normalized, @"0x[0-9A-Fa-f]+", "0x#");
            normalized = Regex.Replace(normalized, @"[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}", "GUID");
            normalized = Regex.Replace(normalized, @"\s+", " ");
            
            return normalized.Trim();
        }
        
        private string GetShortDescription(string message)
        {
            var words = message.Split(' ').Take(5);
            var description = string.Join(" ", words);
            if (description.Length > 50)
                description = description.Substring(0, 47) + "...";
            return description;
        }
    }
    
    public class ErrorPattern
    {
        public string PatternName { get; set; }
        public string Pattern { get; set; }
        public List<ErrorOccurrence> Occurrences { get; set; }
        public int Count { get; set; }
        public DateTime FirstOccurrence { get; set; }
        public DateTime LastOccurrence { get; set; }
        
        public TimeSpan TimeSpan => LastOccurrence - FirstOccurrence;
        public double AveragePerHour => TimeSpan.TotalHours > 0 ? Count / TimeSpan.TotalHours : Count;
    }
    
    public class ErrorOccurrence
    {
        public DateTime Timestamp { get; set; }
        public string Content { get; set; }
        public string FileAlias { get; set; }
        public string LogLevel { get; set; }
    }
}