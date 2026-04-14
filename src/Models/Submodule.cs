namespace SourceGit.Models
{
    public enum SubmoduleStatus
    {
        Unknown = 0,
        Normal,
        NotInited,
        RevisionChanged,
        Unmerged,
        Modified,
        SubmoduleChanged,
    }

    public class Submodule
    {
        public string Path { get; set; } = string.Empty;
        public string SHA { get; set; } = string.Empty;
        public string URL { get; set; } = string.Empty;
        public string Branch { get; set; } = string.Empty;
        public SubmoduleStatus Status { get; set; } = SubmoduleStatus.Unknown;
        public bool HasFileChanges { get; set; } = false;
        public bool HasSubmoduleChanges { get; set; } = false;
        public bool IsDirty => Status > SubmoduleStatus.NotInited || HasFileChanges || HasSubmoduleChanges;
        public bool HasStatusBadge => true;
        public bool IsInitializedClean => Status == SubmoduleStatus.Normal && !HasFileChanges && !HasSubmoduleChanges;
        public bool IsStatusUnknown => Status == SubmoduleStatus.Unknown;
        public bool IsNotInitialized => Status == SubmoduleStatus.NotInited;
        public bool HasUnavailableStatusBadge => Status == SubmoduleStatus.NotInited;
        public bool HasFileChangeStatusBadge => HasFileChanges || Status == SubmoduleStatus.Modified;
        public bool HasSubmoduleChangeStatusBadge => HasSubmoduleChanges || Status is SubmoduleStatus.RevisionChanged or SubmoduleStatus.SubmoduleChanged;
        public bool HasWarningStatusBadge => HasFileChangeStatusBadge || HasSubmoduleChangeStatusBadge;
        public bool HasErrorStatusBadge => Status == SubmoduleStatus.Unmerged;
        public string StatusBadgeText => Status switch
        {
            SubmoduleStatus.Unknown => "?",
            SubmoduleStatus.Normal => "✓",
            SubmoduleStatus.NotInited => "!",
            SubmoduleStatus.Modified => "m",
            SubmoduleStatus.RevisionChanged => "sm",
            SubmoduleStatus.SubmoduleChanged => "sm",
            SubmoduleStatus.Unmerged => "u",
            _ => string.Empty,
        };
        public string FileChangeStatusBadgeText => "m";
        public string SubmoduleChangeStatusBadgeText => "sm";
        public string FileChangeStatusBadgeToolTip => $"Submodule `{Path}` has file changes.";
        public string SubmoduleChangeStatusBadgeToolTip => $"Submodule `{Path}` has nested submodule changes.";
        public string StatusTooltipBadgeText => Status switch
        {
            SubmoduleStatus.Unknown => "not refreshed",
            SubmoduleStatus.Normal => "clean",
            SubmoduleStatus.NotInited => "not initialized",
            SubmoduleStatus.Modified => "modified",
            SubmoduleStatus.RevisionChanged => "submodule modified",
            SubmoduleStatus.SubmoduleChanged => "submodule modified",
            SubmoduleStatus.Unmerged => "unmerged",
            _ => string.Empty,
        };
        public string StatusBadgeToolTip => Status switch
        {
            SubmoduleStatus.Unknown => $"Submodule `{Path}` status has not been refreshed.",
            SubmoduleStatus.Normal => $"Submodule `{Path}` is initialized and clean.",
            SubmoduleStatus.NotInited => $"Submodule `{Path}` is not initialized.",
            SubmoduleStatus.Modified => $"Submodule `{Path}` has file changes.",
            SubmoduleStatus.RevisionChanged => $"Submodule `{Path}` is checked out at a different revision than the super-project pointer.",
            SubmoduleStatus.SubmoduleChanged => $"Submodule `{Path}` has nested submodule changes.",
            SubmoduleStatus.Unmerged => $"Submodule `{Path}` has unresolved merge conflicts.",
            _ => string.Empty,
        };
    }
}
