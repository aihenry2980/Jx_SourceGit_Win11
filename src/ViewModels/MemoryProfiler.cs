using System;
using System.Collections.Generic;
using System.Diagnostics;

using Avalonia.Collections;

using CommunityToolkit.Mvvm.ComponentModel;

namespace SourceGit.ViewModels
{
    public class MemoryProfiler : ObservableObject
    {
        public AvaloniaList<Models.RepositoryMemoryProfile> Repositories { get; } = [];
        public AvaloniaList<Models.SharedMemoryProfile> SharedCaches { get; } = [];

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

        public MemoryProfiler()
        {
            Refresh();
        }

        public void Refresh()
        {
            var previousPath = SelectedRepository?.Path ?? string.Empty;

            Repositories.Clear();
            SharedCaches.Clear();

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
        private string _processPrivateMemory = "0 B";
        private string _managedHeapMemory = "0 B";
        private string _trackedRepositoryMemory = "0 B";
        private string _trackedSharedMemory = "0 B";
        private string _snapshotTime = string.Empty;
    }
}
