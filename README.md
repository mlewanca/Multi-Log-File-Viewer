# Multi-Log File Viewer

A powerful WPF-based log file viewer application with advanced features for monitoring and analyzing multiple log files simultaneously.

## Features

- Multi-file support (up to 20 files)
- Color-coded files for easy differentiation
- Chronological sorting of log entries
- Real-time monitoring with auto-reload
- Advanced search functionality
- Keyword highlighting (bold/yellow)
- Time-based filtering
- File management (show/hide/remove)
- Settings management with XML persistence
- Export capabilities:
  - Export All (chronologically sorted)
  - Export Keywords (filtered by keywords)
  - Time-filtered export
- Auto-scroll functionality
- Line rate limit configuration
- Recurring lines analysis

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
