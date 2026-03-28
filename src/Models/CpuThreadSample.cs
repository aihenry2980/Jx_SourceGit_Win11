namespace SourceGit.Models
{
    public class CpuThreadSample
    {
        public int ThreadId { get; set; }
        public double CpuPercent { get; set; }
        public string CpuPercentText => $"{CpuPercent:F1}%";
        public string State { get; set; } = string.Empty;
        public string Note { get; set; } = string.Empty;
    }
}
