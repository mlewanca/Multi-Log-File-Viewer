using System;
using System.Diagnostics;
using System.Timers;

namespace LogViewer
{
    public class PerformanceMonitor : IDisposable
    {
        private Timer _timer;
        private Process _currentProcess;
        private PerformanceCounter _cpuCounter;
        private double _lastCpuUsage;
        private long _lastMemoryUsage;
        
        public event EventHandler<PerformanceMetrics> MetricsUpdated;
        
        public PerformanceMonitor()
        {
            _currentProcess = Process.GetCurrentProcess();
            InitializeCounters();
            
            _timer = new Timer(1000); // Update every second
            _timer.Elapsed += OnTimerElapsed;
        }
        
        private void InitializeCounters()
        {
            try
            {
                // Try to create CPU performance counter
                _cpuCounter = new PerformanceCounter("Process", "% Processor Time", _currentProcess.ProcessName, true);
                _cpuCounter.NextValue(); // First call returns 0
            }
            catch
            {
                // Performance counters might not be available
                _cpuCounter = null;
            }
        }
        
        public void Start()
        {
            _timer.Start();
        }
        
        public void Stop()
        {
            _timer.Stop();
        }
        
        private void OnTimerElapsed(object sender, ElapsedEventArgs e)
        {
            try
            {
                _currentProcess.Refresh();
                
                // Get memory usage
                long memoryUsage = _currentProcess.WorkingSet64;
                
                // Get CPU usage
                double cpuUsage = 0;
                if (_cpuCounter != null)
                {
                    try
                    {
                        cpuUsage = _cpuCounter.NextValue() / Environment.ProcessorCount;
                    }
                    catch
                    {
                        // Fallback calculation
                        cpuUsage = EstimateCpuUsage();
                    }
                }
                else
                {
                    cpuUsage = EstimateCpuUsage();
                }
                
                _lastCpuUsage = cpuUsage;
                _lastMemoryUsage = memoryUsage;
                
                var metrics = new PerformanceMetrics
                {
                    CpuUsagePercent = cpuUsage,
                    MemoryUsageMB = memoryUsage / (1024.0 * 1024.0),
                    ThreadCount = _currentProcess.Threads.Count,
                    HandleCount = _currentProcess.HandleCount
                };
                
                MetricsUpdated?.Invoke(this, metrics);
            }
            catch
            {
                // Ignore errors in performance monitoring
            }
        }
        
        private double EstimateCpuUsage()
        {
            // Simple estimation based on total processor time
            try
            {
                var totalTime = _currentProcess.TotalProcessorTime.TotalMilliseconds;
                System.Threading.Thread.Sleep(100);
                _currentProcess.Refresh();
                var newTotalTime = _currentProcess.TotalProcessorTime.TotalMilliseconds;
                
                var cpuUsage = (newTotalTime - totalTime) / 100.0 / Environment.ProcessorCount * 100.0;
                return Math.Min(100, Math.Max(0, cpuUsage));
            }
            catch
            {
                return 0;
            }
        }
        
        public void Dispose()
        {
            _timer?.Stop();
            _timer?.Dispose();
            _cpuCounter?.Dispose();
        }
    }
    
    public class PerformanceMetrics
    {
        public double CpuUsagePercent { get; set; }
        public double MemoryUsageMB { get; set; }
        public int ThreadCount { get; set; }
        public int HandleCount { get; set; }
        
        public string GetSummary()
        {
            return $"CPU: {CpuUsagePercent:F1}% | Memory: {MemoryUsageMB:F1} MB | Threads: {ThreadCount} | Handles: {HandleCount}";
        }
    }
}