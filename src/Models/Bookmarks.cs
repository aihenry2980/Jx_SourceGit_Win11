namespace SourceGit.Models
{
    public static class Bookmarks
    {
        public static readonly Avalonia.Media.IBrush[] Brushes = [
            null,
            Avalonia.Media.Brushes.Red,
            Avalonia.Media.Brushes.Orange,
            Avalonia.Media.Brushes.Gold,
            Avalonia.Media.Brushes.ForestGreen,
            Avalonia.Media.Brushes.DarkCyan,
            Avalonia.Media.Brushes.DeepSkyBlue,
            Avalonia.Media.Brushes.Purple,
            Avalonia.Media.Brushes.HotPink,
            Avalonia.Media.Brushes.Crimson,
            Avalonia.Media.Brushes.Coral,
            Avalonia.Media.Brushes.DarkKhaki,
            Avalonia.Media.Brushes.YellowGreen,
            Avalonia.Media.Brushes.SeaGreen,
            Avalonia.Media.Brushes.DodgerBlue,
            Avalonia.Media.Brushes.SlateBlue,
            Avalonia.Media.Brushes.MediumOrchid,
            Avalonia.Media.Brushes.Sienna,
            Avalonia.Media.Brushes.IndianRed,
            Avalonia.Media.Brushes.Teal,
        ];

        public static Avalonia.Media.IBrush Get(int i)
        {
            return (i >= 0 && i < Brushes.Length) ? Brushes[i] : null;
        }
    }
}
