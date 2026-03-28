namespace SourceGit.Models
{
    public class ActiveGitOperationSample
    {
        public string RepositoryName { get; set; } = string.Empty;
        public string RepositoryPath { get; set; } = string.Empty;
        public string OperationName { get; set; } = string.Empty;
        public string CurrentCommand { get; set; } = string.Empty;
        public string DurationText { get; set; } = string.Empty;
        public bool IsBackground { get; set; } = false;
        public string BackgroundText => IsBackground ? "background" : string.Empty;
    }
}
