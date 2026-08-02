namespace SourceGit.Models
{
    public enum ChangeViewMode
    {
        List,
        Grid,
        Tree,
    }

    public enum ChangeState
    {
        None,
        Modified,
        TypeChanged,
        Added,
        Deleted,
        Renamed,
        Copied,
        Untracked,
        Conflicted,
    }

    public enum ConflictReason
    {
        None,
        BothDeleted,
        AddedByUs,
        DeletedByThem,
        AddedByThem,
        DeletedByUs,
        BothAdded,
        BothModified,
    }

    public class ChangeDataForAmend
    {
        public string FileMode { get; set; } = "";
        public string ObjectHash { get; set; } = "";
        public string ParentSHA { get; set; } = "";
    }

    public class Change : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
    {
        public ChangeState Index { get; set; } = ChangeState.None;
        public ChangeState WorkTree { get; set; } = ChangeState.None;
        public string Path { get; set; } = "";
        public string OriginalPath { get; set; } = "";
        public ChangeDataForAmend DataForAmend { get; set; } = null;
        public ConflictReason ConflictReason { get; set; } = ConflictReason.None;
        public bool IsSubmodulePointerChange { get; set; } = false;
        public string IndexSubmodulePointerOldSHA { get; set; } = string.Empty;
        public string IndexSubmodulePointerNewSHA { get; set; } = string.Empty;
        public string WorkTreeSubmodulePointerOldSHA { get; set; } = string.Empty;
        public string WorkTreeSubmodulePointerNewSHA { get; set; } = string.Empty;
        public string AddedLines { get; set; } = string.Empty;
        public string DeletedLines { get; set; } = string.Empty;
        public bool IsCommitFlowIncluded
        {
            get => _isCommitFlowIncluded;
            set => SetProperty(ref _isCommitFlowIncluded, value);
        }

        public bool IsConflicted => WorkTree == ChangeState.Conflicted;
        public bool HasLineStats => !string.IsNullOrEmpty(AddedLines) || !string.IsNullOrEmpty(DeletedLines);
        public string ConflictMarker => CONFLICT_MARKERS[(int)ConflictReason];
        public string ConflictDesc => CONFLICT_DESCS[(int)ConflictReason];
        public string IndexSubmodulePointerText => BuildSubmodulePointerText(IndexSubmodulePointerOldSHA, IndexSubmodulePointerNewSHA);
        public string WorkTreeSubmodulePointerText => BuildSubmodulePointerText(WorkTreeSubmodulePointerOldSHA, WorkTreeSubmodulePointerNewSHA);

        public string WorkTreeDesc => TYPE_DESCS[(int)WorkTree];
        public string IndexDesc => TYPE_DESCS[(int)Index];

        public void Set(ChangeState index, ChangeState workTree = ChangeState.None)
        {
            Index = index;
            WorkTree = workTree;

            if (index == ChangeState.Renamed || index == ChangeState.Copied || workTree == ChangeState.Renamed)
            {
                var parts = Path.Split('\t', 2);
                if (parts.Length < 2)
                    parts = Path.Split(" -> ", 2);
                if (parts.Length == 2)
                {
                    OriginalPath = parts[0];
                    Path = parts[1];
                }
            }

            if (Path[0] == '"')
                Path = Path.Substring(1, Path.Length - 2);

            if (!string.IsNullOrEmpty(OriginalPath) && OriginalPath[0] == '"')
                OriginalPath = OriginalPath.Substring(1, OriginalPath.Length - 2);
        }

        private static readonly string[] TYPE_DESCS =
        [
            "Unknown",
            "Modified",
            "Type Changed",
            "Added",
            "Deleted",
            "Renamed",
            "Copied",
            "Untracked",
            "Conflict"
        ];
        private static readonly string[] CONFLICT_MARKERS =
        [
            string.Empty,
            "DD",
            "AU",
            "UD",
            "UA",
            "DU",
            "AA",
            "UU"
        ];
        private static readonly string[] CONFLICT_DESCS =
        [
            string.Empty,
            "Both deleted",
            "Added by us",
            "Deleted by them",
            "Added by them",
            "Deleted by us",
            "Both added",
            "Both modified"
        ];

        private static string BuildSubmodulePointerText(string oldSHA, string newSHA)
        {
            if (string.IsNullOrEmpty(oldSHA) || string.IsNullOrEmpty(newSHA))
                return string.Empty;

            var oldDisplay = oldSHA.Length > 10 ? oldSHA.Substring(0, 10) : oldSHA;
            var newDisplay = newSHA.Length > 10 ? newSHA.Substring(0, 10) : newSHA;
            return $"SHA {oldDisplay} -> {newDisplay}";
        }

        private bool _isCommitFlowIncluded = true;
    }
}
