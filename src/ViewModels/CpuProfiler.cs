using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;

namespace SourceGit.ViewModels
{
    public class CpuProfiler : ObservableObject, IDisposable
    {
        public ObservableCollection<double> CpuHistory { get; } = [];
        public ObservableCollection<Models.CpuThreadSample> TopThreads { get; } = [];
        public ObservableCollection<Models.ActiveGitOperationSample> ActiveOperations { get; } = [];

        public List<ISeries> CpuSeries
        {
            get => _cpuSeries;
            private set => SetProperty(ref _cpuSeries, value);
        }

        public Axis[] CpuXAxes
        {
            get => _cpuXAxes;
            private set => SetProperty(ref _cpuXAxes, value);
        }

        public Axis[] CpuYAxes
        {
            get => _cpuYAxes;
            private set => SetProperty(ref _cpuYAxes, value);
        }

        public string ProcessCpuPercent
        {
            get => _processCpuPercent;
            private set => SetProperty(ref _processCpuPercent, value);
        }

        public string HottestThread
        {
            get => _hottestThread;
            private set => SetProperty(ref _hottestThread, value);
        }

        public string SampleInterval
        {
            get => _sampleInterval;
            private set => SetProperty(ref _sampleInterval, value);
        }

        public string LastSampleTime
        {
            get => _lastSampleTime;
            private set => SetProperty(ref _lastSampleTime, value);
        }

        public bool HasActiveOperations => ActiveOperations.Count > 0;

        public string Note => "Per-thread CPU is shown as one-core percent. A single thread near 100% means it is saturating one CPU core.";

        public CpuProfiler()
        {
            CpuSeries =
            [
                new LineSeries<double>
                {
                    Values = CpuHistory,
                    GeometrySize = 0,
                    Fill = null,
                    Stroke = new SolidColorPaint(new SKColor(0x0F, 0x8C, 0xFF), 2),
                    LineSmoothness = 0.25,
                },
            ];

            CpuXAxes =
            [
                new Axis
                {
                    IsVisible = false,
                    MinLimit = 0,
                    MaxLimit = 59,
                },
            ];

            CpuYAxes =
            [
                new Axis
                {
                    Name = "CPU %",
                    MinLimit = 0,
                    MaxLimit = 100,
                    MinStep = 10,
                },
            ];

            ProcessCpuPercent = "0.0%";
            HottestThread = "Waiting for samples...";
            SampleInterval = "500 ms";
            LastSampleTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            CaptureBaseline();
            _ = Task.Run(() => SampleLoopAsync(_cts.Token));
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _cts.Cancel();
            _cts.Dispose();
        }

        private readonly CancellationTokenSource _cts = new();
        private readonly Dictionary<int, TimeSpan> _previousThreadCpuTimes = [];
        private DateTime _previousSampleAt = DateTime.UtcNow;
        private TimeSpan _previousProcessCpuTime = TimeSpan.Zero;
        private bool _disposed = false;
        private List<ISeries> _cpuSeries = [];
        private Axis[] _cpuXAxes = [];
        private Axis[] _cpuYAxes = [];
        private string _processCpuPercent = "0.0%";
        private string _hottestThread = string.Empty;
        private string _sampleInterval = string.Empty;
        private string _lastSampleTime = string.Empty;

        private void CaptureBaseline()
        {
            using var process = Process.GetCurrentProcess();
            _previousSampleAt = DateTime.UtcNow;
            _previousProcessCpuTime = process.TotalProcessorTime;
            _previousThreadCpuTimes.Clear();

            foreach (ProcessThread thread in process.Threads)
            {
                try
                {
                    _previousThreadCpuTimes[thread.Id] = thread.TotalProcessorTime;
                }
                catch
                {
                    // Ignore threads that disappear mid-snapshot.
                }
            }
        }

        private async Task SampleLoopAsync(CancellationToken token)
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(500));
            while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
            {
                try
                {
                    await CaptureSampleAsync().ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    // Keep the sampler alive; this tool is only for diagnostics.
                }
            }
        }

        private async Task CaptureSampleAsync()
        {
            using var process = Process.GetCurrentProcess();
            var now = DateTime.UtcNow;
            var elapsed = now - _previousSampleAt;
            if (elapsed.TotalMilliseconds <= 1)
                return;

            var processCpuTime = process.TotalProcessorTime;
            var processCpuPercent = (processCpuTime - _previousProcessCpuTime).TotalMilliseconds /
                (elapsed.TotalMilliseconds * Environment.ProcessorCount) * 100.0;
            processCpuPercent = Math.Clamp(processCpuPercent, 0, 100);

            var threadRows = new List<Models.CpuThreadSample>();
            var latestThreadCpuTimes = new Dictionary<int, TimeSpan>();

            foreach (ProcessThread thread in process.Threads)
            {
                try
                {
                    var totalCpu = thread.TotalProcessorTime;
                    latestThreadCpuTimes[thread.Id] = totalCpu;

                    _previousThreadCpuTimes.TryGetValue(thread.Id, out var previousCpu);
                    var cpuPercent = (totalCpu - previousCpu).TotalMilliseconds / elapsed.TotalMilliseconds * 100.0;
                    if (cpuPercent < 0.1)
                        continue;

                    var state = thread.ThreadState.ToString();
                    if (thread.ThreadState == System.Diagnostics.ThreadState.Wait && thread.WaitReason != ThreadWaitReason.Unknown)
                        state = $"{state} ({thread.WaitReason})";

                    threadRows.Add(new Models.CpuThreadSample
                    {
                        ThreadId = thread.Id,
                        CpuPercent = Math.Round(cpuPercent, 1),
                        State = state,
                        Note = cpuPercent >= 90 ? "Saturating one core" : string.Empty,
                    });
                }
                catch
                {
                    // Ignore threads that disappear or deny access while enumerating.
                }
            }

            threadRows.Sort((l, r) => r.CpuPercent.CompareTo(l.CpuPercent));
            if (threadRows.Count > 10)
                threadRows.RemoveRange(10, threadRows.Count - 10);

            var activeOperations = await Dispatcher.UIThread.InvokeAsync(CollectActiveOperationsSnapshot);

            _previousSampleAt = now;
            _previousProcessCpuTime = processCpuTime;
            _previousThreadCpuTimes.Clear();
            foreach (var kv in latestThreadCpuTimes)
                _previousThreadCpuTimes[kv.Key] = kv.Value;

            Dispatcher.UIThread.Post(() =>
            {
                while (CpuHistory.Count >= 60)
                    CpuHistory.RemoveAt(0);
                CpuHistory.Add(Math.Round(processCpuPercent, 1));

                TopThreads.Clear();
                foreach (var row in threadRows)
                    TopThreads.Add(row);

                ActiveOperations.Clear();
                foreach (var op in activeOperations)
                    ActiveOperations.Add(op);
                OnPropertyChanged(nameof(HasActiveOperations));

                ProcessCpuPercent = $"{processCpuPercent:F1}%";
                HottestThread = threadRows.Count > 0
                    ? $"TID {threadRows[0].ThreadId} at {threadRows[0].CpuPercent:F1}%"
                    : "No hot thread at the moment";
                LastSampleTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            });
        }

        private static List<Models.ActiveGitOperationSample> CollectActiveOperationsSnapshot()
        {
            var result = new List<Models.ActiveGitOperationSample>();
            var launcher = App.GetLauncher();
            if (launcher == null)
                return result;

            foreach (var page in launcher.Pages)
            {
                if (page?.Data is not Repository repo)
                    continue;

                foreach (var log in repo.Logs)
                {
                    if (log == null || log.IsComplete)
                        continue;

                    result.Add(new Models.ActiveGitOperationSample
                    {
                        RepositoryName = page.Node?.Name ?? repo.FullPath,
                        RepositoryPath = repo.FullPath,
                        OperationName = log.Name,
                        CurrentCommand = string.IsNullOrWhiteSpace(log.LatestCommand) ? "(waiting for command output)" : log.LatestCommand,
                        DurationText = FormatDuration(DateTime.Now - log.StartTime),
                        IsBackground = repo.IsAutoFetching || repo.IsQuickFetching || repo.IsQuickPulling,
                    });
                }
            }

            result.Sort((l, r) => string.CompareOrdinal(l.RepositoryName, r.RepositoryName));
            return result;
        }

        private static string FormatDuration(TimeSpan elapsed)
        {
            if (elapsed.TotalHours >= 1)
                return $"{(int)elapsed.TotalHours}:{elapsed.Minutes:00}:{elapsed.Seconds:00}";
            if (elapsed.TotalMinutes >= 1)
                return $"{elapsed.Minutes:00}:{elapsed.Seconds:00}";

            return $"{elapsed.Seconds}.{elapsed.Milliseconds / 100:D1}s";
        }
    }
}
