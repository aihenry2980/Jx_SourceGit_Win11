using System;

namespace SourceGit.Models
{
    public class SubmoduleUpdateBadge
    {
        public string Path { get; }
        public string Name { get; }
        public uint AccentColor { get; }

        public SubmoduleUpdateBadge(string path)
        {
            Path = NormalizePath(path);
            var separator = Path.LastIndexOf('/');
            Name = separator >= 0 && separator < Path.Length - 1 ? Path[(separator + 1)..] : Path;
            AccentColor = ResolveAccentColor(Path);
        }

        public static uint ResolveAccentColor(string path)
        {
            var normalized = NormalizePath(path);
            return PALETTE[GetPaletteIndex(normalized)];
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
        ];
    }
}
