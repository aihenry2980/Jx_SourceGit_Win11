namespace SourceGit.Models
{
    public enum DecoratorType
    {
        None,
        CurrentBranchHead,
        LocalBranchHead,
        CurrentCommitHead,
        ParentRepository,
        RemoteBranchHead,
        SuperProjectPointer,
        Tag,
    }

    public class Decorator
    {
        public DecoratorType Type { get; set; } = DecoratorType.None;
        public string Name { get; set; } = "";
        public uint Color { get; set; } = 0;
        public bool IsBranchFoldable { get; set; } = false;
        public bool IsBranchFolded { get; set; } = false;
        public bool IsTag => Type == DecoratorType.Tag;
    }
}
