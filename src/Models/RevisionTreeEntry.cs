namespace SourceGit.Models
{
    public class RevisionTreeEntry
    {
        public string Mode { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string SHA { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;

        public bool IsSubmodule => Mode == "160000" || Type == "commit";
    }
}
