namespace SourceGit.Models
{
    public enum RecursiveOperationTargetState
    {
        Running,
        Succeeded,
        Skipped,
        Failed,
    }

    public class RecursiveOperationProgress
    {
        public int Total { get; set; } = 0;
        public int Succeeded { get; set; } = 0;
        public int SkippedByUser { get; set; } = 0;
        public int SkippedAutomatically { get; set; } = 0;
        public int SkippedNotInitialized { get; set; } = 0;
        public int Failed { get; set; } = 0;
        public string CurrentTarget { get; set; } = string.Empty;
        public string CurrentRepositoryPath { get; set; } = string.Empty;
        public string CurrentBeforeRevision { get; set; } = string.Empty;
        public string CurrentAfterRevision { get; set; } = string.Empty;
        public RecursiveOperationTargetState CurrentState { get; set; } = RecursiveOperationTargetState.Running;
    }
}
