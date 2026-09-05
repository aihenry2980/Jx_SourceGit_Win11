using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

using Avalonia.Collections;
using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;

namespace SourceGit.ViewModels
{
    public class RepositoryPerformanceCheck
    {
        public string Name { get; init; } = string.Empty;
        public string Duration { get; init; } = string.Empty;
        public string Detail { get; init; } = string.Empty;
        public string Recommendation { get; init; } = string.Empty;
        public string SeverityColor { get; init; } = "#FF10893E";
    }

    public partial class RepositoryPerformanceDiagnostics : ObservableObject
    {
        public string RepositoryName { get; }
        public string RepositoryPath { get; }
        public string GitDirectory { get; }
        public AvaloniaList<RepositoryPerformanceCheck> Checks { get; } = [];

        public string Summary
        {
            get => _summary;
            private set => SetProperty(ref _summary, value);
        }

        public string LastRun
        {
            get => _lastRun;
            private set => SetProperty(ref _lastRun, value);
        }

        public bool IsRunning
        {
            get => _isRunning;
            private set => SetProperty(ref _isRunning, value);
        }

        public RepositoryPerformanceDiagnostics(Repository repository)
        {
            RepositoryName = Path.GetFileName(repository.FullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            RepositoryPath = repository.FullPath;
            GitDirectory = repository.GitDir;
            _ = RunAsync();
        }

        public async Task RunAsync()
        {
            if (IsRunning)
                return;

            IsRunning = true;
            Checks.Clear();
            Summary = "Measuring repository operations...";

            var checks = new List<RepositoryPerformanceCheck>();
            try
            {
                checks.Add(await ProbeAsync(
                    "Tracked working-tree status",
                    "status --porcelain=v2 -z -uno --ignore-submodules=all",
                    output => $"{CountRecords(output)} tracked change record(s)",
                    "A slow result usually points to a large working tree, filesystem latency, or hooks.").ConfigureAwait(false));

                checks.Add(await ProbeAsync(
                    "Untracked-file scan",
                    "status --porcelain=v2 -z -uall --ignore-submodules=all",
                    output => $"{CountRecords(output)} total change record(s)",
                    "If this is much slower, add generated folders to .gitignore or use local ignore rules.").ConfigureAwait(false));

                checks.Add(await ProbeAsync(
                    "History load (1,000 commits)",
                    "log -1000 --format=%H",
                    output => $"{CountLines(output)} commit(s) returned",
                    "A slow result can improve after git gc, commit-graph maintenance, or reducing visible history.").ConfigureAwait(false));

                checks.Add(await ProbeAsync(
                    "Reference enumeration",
                    "for-each-ref --format=%(refname)",
                    output => $"{CountLines(output)} branch/tag reference(s)",
                    "Prune obsolete remote branches if this repository has accumulated many stale refs.").ConfigureAwait(false));

                checks.Add(await ProbeAsync(
                    "Object database inspection",
                    "count-objects -vH",
                    output => DescribeObjectDatabase(output),
                    "Large loose-object counts benefit from repository maintenance; packed object size reflects repository scale.").ConfigureAwait(false));
            }
            finally
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    foreach (var check in checks)
                        Checks.Add(check);

                    var slow = 0;
                    foreach (var check in checks)
                    {
                        if (check.SeverityColor != "#FF10893E")
                            slow++;
                    }

                    Summary = slow == 0
                        ? "No measured Git operation is slow."
                        : $"{slow} operation(s) need attention. Check the recommendations below.";
                    LastRun = $"Last run: {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
                    IsRunning = false;
                });
            }
        }

        private async Task<RepositoryPerformanceCheck> ProbeAsync(string name, string args, Func<string, string> describe, string recommendation)
        {
            var stopwatch = Stopwatch.StartNew();
            var result = await new Commands.RepositoryPerformanceProbe(RepositoryPath, args).GetResultAsync().ConfigureAwait(false);
            stopwatch.Stop();

            if (!result.IsSuccess)
            {
                var error = string.IsNullOrWhiteSpace(result.StdErr) ? "Git command failed." : result.StdErr.Trim();
                return new RepositoryPerformanceCheck
                {
                    Name = name,
                    Duration = "Failed",
                    Detail = error,
                    Recommendation = "Open command logs to inspect the Git error before drawing performance conclusions.",
                    SeverityColor = "#FFD13438",
                };
            }

            return new RepositoryPerformanceCheck
            {
                Name = name,
                Duration = FormatDuration(stopwatch.Elapsed),
                Detail = describe(result.StdOut),
                Recommendation = recommendation,
                SeverityColor = GetSeverityColor(stopwatch.Elapsed),
            };
        }

        private static int CountRecords(string output)
        {
            return output.Split('\0', StringSplitOptions.RemoveEmptyEntries).Length;
        }

        private static int CountLines(string output)
        {
            return output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Length;
        }

        private static string DescribeObjectDatabase(string output)
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            {
                var split = line.Split(':', 2, StringSplitOptions.TrimEntries);
                if (split.Length == 2)
                    values[split[0]] = split[1];
            }

            values.TryGetValue("count", out var loose);
            values.TryGetValue("size-pack", out var packed);
            values.TryGetValue("in-pack", out var packedObjects);
            return $"{loose ?? "0"} loose object(s), {packedObjects ?? "0"} packed object(s), pack size {packed ?? "0"}";
        }

        private static string FormatDuration(TimeSpan duration)
        {
            return duration.TotalSeconds >= 1
                ? $"{duration.TotalSeconds:F2} s"
                : $"{duration.TotalMilliseconds:F0} ms";
        }

        private static string GetSeverityColor(TimeSpan duration)
        {
            if (duration.TotalMilliseconds >= 1500)
                return "#FFD13438";

            if (duration.TotalMilliseconds >= 500)
                return "#FFF0A000";

            return "#FF10893E";
        }

        private string _summary = string.Empty;
        private string _lastRun = string.Empty;
        private bool _isRunning = false;
    }
}
