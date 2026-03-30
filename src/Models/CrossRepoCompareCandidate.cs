namespace SourceGit.Models
{
    public class CrossRepoCompareCandidate
    {
        public string DisplayName { get; set; } = string.Empty;
        public string BranchName { get; set; } = string.Empty;
        public string RepositoryPath { get; set; } = string.Empty;
        public string RemoteKey { get; set; } = string.Empty;
        public string MenuText => $"{DisplayName} [{BranchName}]";
    }
}
