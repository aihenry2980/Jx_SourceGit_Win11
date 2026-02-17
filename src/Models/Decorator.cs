namespace SourceGit.Models
{
    public enum DecoratorType
    {
        None,
        CurrentBranchHead,
        LocalBranchHead,
        CurrentCommitHead,
        RemoteBranchHead,
        SuperProjectPointer,
        Tag,
    }

    public class Decorator
    {
        public DecoratorType Type { get; set; } = DecoratorType.None;
        public string Name { get; set; } = "";
        public uint Color { get; set; } = 0;
        public bool IsTag => Type == DecoratorType.Tag;
    }
}
