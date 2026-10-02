using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace SourceGit.Models
{
    public sealed class CrashReport
    {
        public string LogPath { get; private init; }
        public string Summary { get; private init; }

        public static string GetDirectory(string cacheDirectory) => Path.Combine(
            string.IsNullOrWhiteSpace(cacheDirectory) ? Path.Combine(Path.GetTempPath(), "SourceGit") : cacheDirectory,
            "crashes");

        public static CrashReport Save(Exception exception, string cacheDirectory, bool fatal)
        {
            var directory = GetDirectory(cacheDirectory);
            Directory.CreateDirectory(directory);
            var now = DateTimeOffset.UtcNow;
            var fileName = $"{now.ToString("yyyy-MM-dd_HH-mm-ss-fff", CultureInfo.InvariantCulture)}-{Guid.NewGuid():N}.log";
            var logPath = Path.Combine(directory, fileName);
            var root = exception.GetBaseException();
            var frames = new StackTrace(root, true).GetFrames() ?? [];
            var frame = frames.FirstOrDefault(f => f.GetMethod()?.DeclaringType?.FullName?.StartsWith("SourceGit.", StringComparison.Ordinal) == true)
                ?? frames.FirstOrDefault();
            var method = frame?.GetMethod() ?? root.TargetSite;
            var location = method == null ? "stack unavailable" : $"{method.DeclaringType?.FullName}.{method.Name}";
            if (frame?.GetFileLineNumber() > 0)
                location += $" ({Path.GetFileName(frame.GetFileName())}:{frame.GetFileLineNumber()})";

            var assembly = typeof(CrashReport).Assembly;
            var version = assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(a => a.Key == "FriendlyVersion")?.Value ?? assembly.GetName().Version?.ToString();
            var summary = string.Join(Environment.NewLine,
                $"JxSourceGit {version}; {OneLine(RuntimeInformation.OSDescription)}; {RuntimeInformation.FrameworkDescription}; {RuntimeInformation.ProcessArchitecture}; {now:yyyy-MM-dd HH:mm:ss} UTC.",
                $"Exception: {root.GetType().FullName}: {OneLine(root.Message)}; location: {location}.",
                $"Full log: {logPath}");
            var report = new CrashReport { LogPath = logPath, Summary = summary };
            var log = new StringBuilder(summary).AppendLine().AppendLine()
                .AppendLine($"Fatal: {fatal}")
                .AppendLine($"Thread: {Environment.CurrentManagedThreadId}")
                .AppendLine($"Thread Name: {Thread.CurrentThread.Name ?? "Unnamed"}")
                .AppendLine($"Source: {exception.Source}");
            try
            {
                using var process = Process.GetCurrentProcess();
                log.AppendLine($"App Start Time: {process.StartTime:O}")
                    .AppendLine($"Memory Usage: {process.PrivateMemorySize64 / 1024 / 1024} MB");
            }
            catch (Exception)
            {
                // Process metrics are optional; retain the exception if they are unavailable.
            }
            log.AppendLine().AppendLine(exception.ToString());
            File.WriteAllText(logPath, log.ToString(), Encoding.UTF8);
            if (fatal)
            {
                File.WriteAllText(Path.Combine(directory, "last-crash-summary.txt"), summary, Encoding.UTF8);
                File.WriteAllText(Path.Combine(directory, "pending-crash-summary.txt"), summary, Encoding.UTF8);
            }
            return report;
        }

        public static string ReadPending(string cacheDirectory)
        {
            foreach (var directory in GetDirectories(cacheDirectory))
            {
                var summary = ReadPendingDirectory(directory);
                if (summary != null)
                    return summary;
            }
            return null;
        }

        public static string GetPendingDirectory(string cacheDirectory, string summary)
        {
            return GetDirectories(cacheDirectory).FirstOrDefault(d => ReadPendingDirectory(d) == summary)
                ?? GetDirectory(cacheDirectory);
        }

        public static void Acknowledge(string cacheDirectory, string summary)
        {
            foreach (var directory in GetDirectories(cacheDirectory))
            {
                try
                {
                    var path = Path.Combine(directory, "pending-crash-summary.txt");
                    if (File.Exists(path) && File.ReadAllText(path) == summary)
                        File.Delete(path);
                }
                catch (Exception)
                {
                    // Keep the pending report if its acknowledgement cannot be persisted.
                }
            }
        }

        private static string[] GetDirectories(string cacheDirectory) =>
            new[] { GetDirectory(cacheDirectory), GetDirectory(null) }.Distinct().ToArray();

        private static string ReadPendingDirectory(string directory)
        {
            try
            {
                var path = Path.Combine(directory, "pending-crash-summary.txt");
                return File.Exists(path) ? File.ReadAllText(path) : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string OneLine(string text)
        {
            var line = (text ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
            return line.Length > 250 ? line[..250] + "..." : line;
        }
    }
}
