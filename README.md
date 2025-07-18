# Multi-Log File Viewer v0.4.0

A powerful WPF-based log file viewer application with advanced analytics and plugin support for monitoring and analyzing multiple log files simultaneously.

## 🆕 New in Version 0.4.0

### **Enhanced Features:**
- **📊 Advanced Analytics**: Comprehensive log analysis with distribution charts, timeline views, and error pattern recognition
- **🔌 Plugin Architecture**: Extensible parser system supporting custom log formats
- **⚡ Performance Monitoring**: Real-time CPU and memory usage tracking
- **🎯 Enhanced Timestamp Parsing**: Support for multiple timestamp formats (ISO 8601, syslog, Apache, IIS, etc.)
- **🚀 Improved Performance**: Async loading with cancellation support and background processing

### **New Windows & Features:**
- **Analytics Window**: Log level distribution, timeline analysis, peak activity detection
- **Plugin Manager**: Load and manage custom log parsers
- **Distribution Charts**: Visual representation of log level frequencies
- **Enhanced Status Bar**: Real-time performance metrics and detailed status information

### **Technical Improvements:**
- **Multi-format timestamp parsing** with automatic year inference
- **Plugin system** for custom log format parsers
- **Performance counters** for system monitoring
- **Cancellation tokens** for all async operations
- **Better error handling** with graceful degradation

## Features

### **Core Functionality:**
- Multi-file support (up to 20 files)
- Color-coded files for easy differentiation
- Chronological sorting of log entries across all files
- Real-time monitoring with auto-reload
- Advanced search functionality (regex, case-sensitive, column-specific)
- Keyword highlighting (bold/yellow)
- Time-based filtering with precision controls
- File management (show/hide/remove)

### **Advanced Analytics:**
- **Log Level Distribution**: Visual charts showing ERROR, WARN, INFO, DEBUG frequencies
- **Timeline Analysis**: Hour-by-hour activity visualization
- **Error Pattern Recognition**: Automatic detection of recurring error patterns
- **Peak Activity Detection**: Identify high-traffic periods
- **Performance Metrics**: Real-time CPU and memory monitoring

### **Data Management:**
- Settings management with XML persistence
- Filter presets for saved search configurations
- Recent files dropdown with folder history
- Export capabilities:
  - Export All (chronologically sorted)
  - Export Keywords (filtered by keywords)
  - Time-filtered export
  - Multiple formats: TXT, CSV, JSON, XML

### **User Experience:**
- Auto-scroll functionality with visual indicators
- Dark/Light theme toggle with persistence
- Drag & drop file support
- Context menus with bookmarking
- Keyboard shortcuts for power users
- Professional toolbar with logical grouping

### **Performance & Scalability:**
- Line rate limit configuration for large files
- Virtual scrolling for handling thousands of entries
- Memory-efficient streaming with background loading
- File system watchers for real-time updates
- Cancellation support for long operations

## Requirements

- .NET 6.0 or later
- Windows operating system
- 4GB RAM recommended for large log files

## Installation

1. Clone the repository:
```bash
git clone https://github.com/mlewanca/Multi-Log-File-Viewer.git