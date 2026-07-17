using System;
using System.Collections.Generic;

namespace SourceGit.Models
{
    public class SubmoduleUpdateBadge
    {
        public static IReadOnlyList<uint> ColorPalette => PALETTE;

        public string Path { get; }
        public string Name { get; }
        public uint AccentColor { get; }

        public SubmoduleUpdateBadge(string path, uint? accentColor = null)
        {
            Path = NormalizePath(path);
            var separator = Path.LastIndexOf('/');
            Name = separator >= 0 && separator < Path.Length - 1 ? Path[(separator + 1)..] : Path;
            AccentColor = accentColor ?? ResolveAccentColor(Path);
        }

        public static uint ResolveAccentColor(string path)
        {
            var normalized = NormalizePath(path);
            return PALETTE[GetPaletteIndex(normalized)];
        }

        public static uint ResolveAccentColor(string path, IReadOnlyDictionary<string, uint> directSubmoduleColors)
        {
            var normalized = NormalizePath(path);
            while (!string.IsNullOrEmpty(normalized))
            {
                if (directSubmoduleColors != null && directSubmoduleColors.TryGetValue(normalized, out var color))
                    return color;

                var separator = normalized.LastIndexOf('/');
                if (separator < 0)
                    break;

                normalized = normalized.Substring(0, separator);
            }

            return ResolveAccentColor(path);
        }

        public static IReadOnlyDictionary<string, uint> BuildDirectSubmoduleColorMap(IEnumerable<string> paths)
        {
            var normalizedPaths = new HashSet<string>(StringComparer.Ordinal);
            foreach (var path in paths)
                normalizedPaths.Add(NormalizePath(path));

            var directPaths = new List<string>();
            foreach (var path in normalizedPaths)
            {
                if (!HasAncestor(path, normalizedPaths))
                    directPaths.Add(path);
            }

            directPaths.Sort(StringComparer.Ordinal);
            var colors = new Dictionary<string, uint>(directPaths.Count, StringComparer.Ordinal);
            var occupiedSlots = new HashSet<int>();
            foreach (var path in directPaths)
            {
                var slot = GetPaletteIndex(path);
                if (occupiedSlots.Count < PALETTE.Length)
                {
                    while (occupiedSlots.Contains(slot))
                        slot = (slot + 1) % PALETTE.Length;

                    occupiedSlots.Add(slot);
                }

                colors[path] = PALETTE[slot];
            }

            return colors;
        }

        private static string NormalizePath(string path)
        {
            var normalized = (path ?? string.Empty).Replace('\\', '/').Trim('/');
            return string.IsNullOrEmpty(normalized) ? "submodule" : normalized;
        }

        private static int GetPaletteIndex(string path)
        {
            var hash = 2166136261u;
            foreach (var c in path)
            {
                hash ^= char.ToUpperInvariant(c);
                hash *= 16777619u;
            }

            return (int)(hash % PALETTE.Length);
        }

        private static bool HasAncestor(string path, HashSet<string> allPaths)
        {
            var separator = path.LastIndexOf('/');
            while (separator > 0)
            {
                if (allPaths.Contains(path.Substring(0, separator)))
                    return true;

                separator = path.LastIndexOf('/', separator - 1);
            }

            return false;
        }

        private static readonly uint[] PALETTE =
        [
            0xFF0F766E,
            0xFF1D4ED8,
            0xFF6D28D9,
            0xFFA21CAF,
            0xFFBE123C,
            0xFFC2410C,
            0xFF15803D,
            0xFF0E7490,
            0xFF4338CA,
            0xFFBE185D,
            0xFF92400E,
            0xFF4D7C0F,
        ];
    }
}
