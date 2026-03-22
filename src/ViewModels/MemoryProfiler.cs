using System;
using System.Collections.Generic;
using System.Diagnostics;

using Avalonia.Collections;

using CommunityToolkit.Mvvm.ComponentModel;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;

namespace SourceGit.ViewModels
{
    public class MemoryProfiler : ObservableObject
    {
        public AvaloniaList<Models.RepositoryMemoryProfile> Repositories { get; } = [];
        public AvaloniaList<Models.SharedMemoryProfile> SharedCaches { get; } = [];
        public AvaloniaList<Models.MemorySliceLegendItem> ProcessMemoryLegend { get; } = [];

        public List<ISeries> ProcessMemorySeries
        {
            get => _processMemorySeries;
            private set => SetProperty(ref _processMemorySeries, value);
        }

        public Models.RepositoryMemoryProfile SelectedRepository
        {
            get => _selectedRepository;
            set
            {
                if (SetProperty(ref _selectedRepository, value))
                    OnPropertyChanged(nameof(HasSelectedRepository));
            }
        }

        public bool HasSelectedRepository => SelectedRepository != null;

        public string ProcessPrivateMemory
        {
            get => _processPrivateMemory;
            private set => SetProperty(ref _processPrivateMemory, value);
        }

        public string ManagedHeapMemory
        {
            get => _managedHeapMemory;
            private set => SetProperty(ref _managedHeapMemory, value);
        }

        public string TrackedRepositoryMemory
        {
            get => _trackedRepositoryMemory;
            private set => SetProperty(ref _trackedRepositoryMemory, value);
        }

        public string TrackedSharedMemory
        {
            get => _trackedSharedMemory;
            private set => SetProperty(ref _trackedSharedMemory, value);
        }

        public string SnapshotTime
        {
            get => _snapshotTime;
            private set => SetProperty(ref _snapshotTime, value);
        }

        public string EstimateNote => "These numbers are estimates for SourceGit-owned state. The process total also includes CLR/runtime/native memory that cannot be assigned exactly per repository.";

        public string ProcessMemoryChartNote => "Pie chart explains the process private total only. Managed heap overlaps these slices and is shown separately as a reference.";

        public MemoryProfiler()
        {
            Refresh();
        }

        public void Refresh()
        {
            var previousPath = SelectedRepository?.Path ?? string.Empty;

            Repositories.Clear();
            SharedCaches.Clear();
            ProcessMemoryLegend.Clear();

            var launcher = App.GetLauncher();
            var snapshots = new List<Models.RepositoryMemoryProfile>();
            if (launcher != null)
            {
                foreach (var page in launcher.Pages)
                {
                    if (page?.Data is Repository repo)
                        snapshots.Add(repo.BuildMemoryProfile());
                }
            }

            snapshots.Sort((l, r) => r.EstimatedBytes.CompareTo(l.EstimatedBytes));
            foreach (var snapshot in snapshots)
                Repositories.Add(snapshot);

            SharedCaches.Add(Models.AvatarManager.Instance.BuildMemoryProfile());

            long repoBytes = 0;
            foreach (var repo in Repositories)
                repoBytes += repo.EstimatedBytes;

            long sharedBytes = 0;
            foreach (var cache in SharedCaches)
                sharedBytes += cache.Bytes;

            var process = Process.GetCurrentProcess();
            ProcessPrivateMemory = Models.MemoryProfileFormatter.Format(process.PrivateMemorySize64);
            ManagedHeapMemory = Models.MemoryProfileFormatter.Format(GC.GetTotalMemory(false));
            TrackedRepositoryMemory = Models.MemoryProfileFormatter.Format(repoBytes);
            TrackedSharedMemory = Models.MemoryProfileFormatter.Format(sharedBytes);
            SnapshotTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            BuildProcessMemoryChart(process.PrivateMemorySize64, repoBytes, sharedBytes);

            Models.RepositoryMemoryProfile selected = null;
            if (!string.IsNullOrEmpty(previousPath))
            {
                foreach (var repo in Repositories)
                {
                    if (repo.Path.Equals(previousPath, StringComparison.Ordinal))
                    {
                        selected = repo;
                        break;
                    }
                }
            }

            SelectedRepository = selected ?? (Repositories.Count > 0 ? Repositories[0] : null);
        }

        public void CollectGarbageAndRefresh()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Refresh();
        }

        private Models.RepositoryMemoryProfile _selectedRepository = null;
        private List<ISeries> _processMemorySeries = [];
        private string _processPrivateMemory = "0 B";
        private string _managedHeapMemory = "0 B";
        private string _trackedRepositoryMemory = "0 B";
        private string _trackedSharedMemory = "0 B";
        private string _snapshotTime = string.Empty;

        private void BuildProcessMemoryChart(long processPrivateBytes, long repoBytes, long sharedBytes)
        {
            var unattributedBytes = Math.Max(0, processPrivateBytes - repoBytes - sharedBytes);
            var total = Math.Max(1, processPrivateBytes);

            ProcessMemoryLegend.Add(new Models.MemorySliceLegendItem(
                "Tracked repos",
                repoBytes,
                "#FF1D6FDD",
                "SourceGit repo-owned estimates"));
            ProcessMemoryLegend.Add(new Models.MemorySliceLegendItem(
                "Shared caches",
                sharedBytes,
                "#FF10893E",
                "Shared app caches like avatars"));
            ProcessMemoryLegend.Add(new Models.MemorySliceLegendItem(
                "Unattributed/runtime",
                unattributedBytes,
                "#FF7C3AED",
                "CLR, native UI/graphics, runtime, stacks, and other unassigned memory"));

            ProcessMemorySeries =
            [
                CreateProcessMemorySlice("Tracked repos", repoBytes, total, new SKColor(0x1D, 0x6F, 0xDD)),
                CreateProcessMemorySlice("Shared caches", sharedBytes, total, new SKColor(0x10, 0x89, 0x3E)),
                CreateProcessMemorySlice("Unattributed/runtime", unattributedBytes, total, new SKColor(0x7C, 0x3A, 0xED)),
            ];
        }

        private static ISeries CreateProcessMemorySlice(string name, long bytes, long total, SKColor color)
        {
            return new PieSeries<long>
            {
                Name = name,
                Values = [Math.Max(0, bytes)],
                Fill = new SolidColorPaint(color),
                Stroke = new SolidColorPaint(new SKColor(255, 255, 255, 120)) { StrokeThickness = 1 },
                Pushout = 0,
                InnerRadius = 24,
                HoverPushout = 6,
                MaxRadialColumnWidth = 28,
                DataLabelsPosition = LiveChartsCore.Measure.PolarLabelsPosition.Middle,
                DataLabelsPaint = new SolidColorPaint(new SKColor(255, 255, 255)),
                DataLabelsSize = 10,
                DataLabelsFormatter = point =>
                {
                    if (bytes <= 0)
                        return string.Empty;

                    var pct = bytes / (double)total;
                    return pct >= 0.06 ? $"{pct:P0}" : string.Empty;
                },
            };
        }
    }
}
