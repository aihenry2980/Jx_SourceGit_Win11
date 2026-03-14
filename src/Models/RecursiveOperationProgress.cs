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
        public int Skipped { get; set; } = 0;
        public int Failed { get; set; } = 0;
        public string CurrentTarget { get; set; } = string.Empty;
        public RecursiveOperationTargetState CurrentState { get; set; } = RecursiveOperationTargetState.Running;
    }
}
