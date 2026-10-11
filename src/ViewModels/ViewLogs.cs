using Avalonia.Collections;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Linq;

namespace SourceGit.ViewModels
{
    public class ViewLogs : ObservableObject
    {
        public AvaloniaList<CommandLog> Logs
        {
            get => _repo.Logs;
        }

        public CommandLog SelectedLog
        {
            get => _selectedLog;
            set => SetProperty(ref _selectedLog, value);
        }

        public ViewLogs(Repository repo)
        {
            _repo = repo;
        }

        public void ClearAll()
        {
            foreach (var log in Logs.Where(log => log.IsComplete).ToArray())
                RemoveLog(log);
        }

        public void RemoveLog(CommandLog log)
        {
            if (log == null || !log.IsComplete || !Logs.Contains(log))
                return;

            var wasSelected = SelectedLog == log;
            Logs.Remove(log);
            if (wasSelected)
                SelectedLog = Logs.FirstOrDefault(item => !item.IsComplete) ?? Logs.FirstOrDefault();
        }

        private Repository _repo = null;
        private CommandLog _selectedLog = null;
    }
}
