# Multi-Log File Viewer v0.5.0

A powerful WPF-based log file viewer application with advanced features for monitoring and analyzing multiple log files simultaneously.

## What's New in v0.5.0

### Major Features Added:
- **Timeline Analysis**: Hour-by-hour activity visualization with export capability
- **Error Pattern Recognition**: Automatic detection of recurring error patterns
- **Peak Activity Detection**: Identify high-traffic periods in your logs
- **Performance Metrics**: Real-time CPU and memory monitoring in status bar
- **Tabbed Settings Window**: Reorganized settings into intuitive tabs (General, Display, Keywords, etc.)
- **24-Hour Time Filter**: Now default on startup, showing only the last 24 hours of logs
- **Native WPF Dialogs**: Replaced Windows Forms InputBox with WPF InputDialog

### Improvements:
- Enhanced error handling with logging to file
- Better null reference protection
- Extracted magic numbers to named constants
- Added LogLevel extraction and filtering
- Improved status bar with performance metrics
- Better handling of future timestamps in 24-hour filter mode

## Features

- **Multi-file support** (up to 20 files)
- **Color-coded files** for easy differentiation
- **Chronological sorting** of log entries across all files
- **Real-time monitoring** with auto-reload
- **Advanced search functionality** (regex, case-sensitive, column-specific)
- **Keyword highlighting** (bold/yellow)
- **Time-based filtering** with precision controls
- **File management** (show/hide/remove)
- **Timeline Analysis**: Hour-by-hour activity visualization
- **Error Pattern Recognition**: Automatic detection of recurring error patterns
- **Peak Activity Detection**: Identify high-traffic periods
- **Performance Metrics**: Real-time CPU and memory monitoring
- **Settings management** with XML persistence
- **Export capabilities**:
  - Export All (chronologically sorted)
  - Export Keywords (filtered by keywords)
  - Time-filtered export
  - Timeline analysis export (CSV)
  - Error patterns export (CSV)
- **Auto-scroll functionality**
- **Line rate limit configuration**
- **Recurring lines analysis**
- **Dark/Light theme toggle**
- **Log level filtering** (ERROR, WARN, INFO, DEBUG)

## Requirements

- .NET 6.0 or later
- Windows operating system

## Installation

1. Clone the repository:
```bash
git clone https://github.com/your-username/multi-log-viewer.git
```

2. Build the solution:
```bash
dotnet build
```

3. Run the application:
```bash
dotnet run
```

## Usage

1. Load log files:
   - Click "Load Files" to select individual files
   - Or use the folder browser to load multiple files with specific patterns

2. Configure settings:
   - Click "Settings" to access configuration options
   - Configure keywords, file aliases, and line rate limits
   - Analyze recurring lines in log files

3. Filter and analyze:
   - Use the search box to filter log entries
   - Apply time-based filters
   - Use the auto-scroll checkbox to follow new entries

4. Export data:
   - Export all visible log entries
   - Export keyword-filtered entries
   - Export time-filtered entries

## License

MIT License - feel free to use and modify as needed
