namespace SourceGit.Models
{
    public class MemorySliceLegendItem(string name, long bytes, string color, string details)
    {
        public string Name { get; } = name;
        public long Bytes { get; } = bytes;
        public string FormattedBytes => MemoryProfileFormatter.Format(Bytes);
        public string Color { get; } = color;
        public string Details { get; } = details;
    }
}
