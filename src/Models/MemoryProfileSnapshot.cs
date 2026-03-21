using System;
using System.Collections.Generic;

using Avalonia.Media.Imaging;

namespace SourceGit.Models
{
    public class MemoryProfileComponent
    {
        public string Name { get; }
        public long Bytes { get; }
        public string Details { get; }
        public string FormattedBytes => MemoryProfileFormatter.Format(Bytes);

        public MemoryProfileComponent(string name, long bytes, string details)
        {
            Name = name ?? string.Empty;
            Bytes = Math.Max(0, bytes);
            Details = details ?? string.Empty;
        }
    }

    public class SharedMemoryProfile
    {
        public string Name { get; }
        public long Bytes { get; }
        public string Details { get; }
        public string Notes { get; }
        public string FormattedBytes => MemoryProfileFormatter.Format(Bytes);

        public SharedMemoryProfile(string name, long bytes, string details, string notes = "")
        {
            Name = name ?? string.Empty;
            Bytes = Math.Max(0, bytes);
            Details = details ?? string.Empty;
            Notes = notes ?? string.Empty;
        }
    }

    public class RepositoryMemoryProfile
    {
        public string Name { get; }
        public string Path { get; }
        public string CountsSummary { get; }
        public string Notes { get; }
        public IReadOnlyList<MemoryProfileComponent> Components { get; }
        public long EstimatedBytes { get; }
        public string EstimatedBytesText => MemoryProfileFormatter.Format(EstimatedBytes);
        public string TopSuspect => LargestComponent?.Name ?? "No active cache";
        public string TopSuspectDetail => LargestComponent != null ? $"{LargestComponent.Name} ({LargestComponent.FormattedBytes})" : "No active cache";

        private MemoryProfileComponent LargestComponent { get; }

        public RepositoryMemoryProfile(
            string name,
            string path,
            string countsSummary,
            string notes,
            IReadOnlyList<MemoryProfileComponent> components)
        {
            Name = string.IsNullOrWhiteSpace(name) ? path ?? string.Empty : name;
            Path = path ?? string.Empty;
            CountsSummary = countsSummary ?? string.Empty;
            Notes = notes ?? string.Empty;
            Components = components ?? Array.Empty<MemoryProfileComponent>();

            long total = 0;
            MemoryProfileComponent largest = null;
            foreach (var component in Components)
            {
                if (component == null)
                    continue;

                total += component.Bytes;
                if (largest == null || component.Bytes > largest.Bytes)
                    largest = component;
            }

            EstimatedBytes = Math.Max(0, total);
            LargestComponent = largest;
        }
    }

    public static class MemoryProfileFormatter
    {
        public static string Format(long bytes)
        {
            if (bytes <= 0)
                return "0 B";

            string[] units = ["B", "KB", "MB", "GB", "TB"];
            double value = bytes;
            var unit = 0;
            while (value >= 1024 && unit < units.Length - 1)
            {
                value /= 1024;
                unit++;
            }

            return unit == 0 ? $"{value:0} {units[unit]}" : $"{value:0.0} {units[unit]}";
        }
    }

    internal static class MemoryProfileEstimator
    {
        public static long EstimateString(string value)
        {
            if (string.IsNullOrEmpty(value))
                return 0;

            return 24 + value.Length * 2L;
        }

        public static long EstimateBitmap(Bitmap bitmap)
        {
            if (bitmap == null)
                return 0;

            var pixels = (long)bitmap.PixelSize.Width * bitmap.PixelSize.Height;
            return pixels * 4 + 256;
        }

        public static long EstimateListReferences<T>(ICollection<T> values)
        {
            if (values == null || values.Count == 0)
                return 0;

            return 32 + values.Count * 8L;
        }
    }
}
