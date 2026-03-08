namespace SourceGit.Models
{
    public interface ICommandLogReceiver
    {
        void OnReceiveCommandLog(string line);
        void OnResetCommandLog(string content);
    }

    public interface ICommandLog
    {
        void AppendLine(string line);
    }
}
