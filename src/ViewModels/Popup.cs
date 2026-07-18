using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SourceGit.ViewModels
{
    public class Popup : ObservableValidator, Models.ICommandLogReceiver
    {
        public bool InProgress
        {
            get => _inProgress;
            set
            {
                if (SetProperty(ref _inProgress, value))
                    OnPropertyChanged(nameof(IsContentInteractive));
            }
        }

        public string ProgressDescription
        {
            get => _progressDescription;
            set => SetProperty(ref _progressDescription, value);
        }

        [UnconditionalSuppressMessage("AssemblyLoadTrimming", "IL2026:RequiresUnreferencedCode")]
        public bool Check()
        {
            if (HasErrors)
                return false;
            ValidateAllProperties();
            return !HasErrors;
        }

        public void OnReceiveCommandLog(string data)
        {
            var lines = data?.Split(['\r', '\n'], System.StringSplitOptions.RemoveEmptyEntries);
            if (lines is { Length: > 0 })
                ProgressDescription = lines[^1].Trim();
        }

        public void OnResetCommandLog(string content)
        {
            var lines = content?.Split(['\r', '\n'], System.StringSplitOptions.RemoveEmptyEntries);
            if (lines is { Length: > 0 })
                ProgressDescription = lines[^1];
        }

        public void Cleanup()
        {
            _log?.Unsubscribe(this);
        }

        public virtual bool CanStartDirectly()
        {
            return true;
        }

        public virtual bool ShowOptions => true;
        public virtual double PopupWidth => 512;
        public virtual bool AllowCancelWhenRunning => false;
        public virtual bool AllowContentInteractionWhenRunning => false;
        public bool IsContentInteractive => !_inProgress || AllowContentInteractionWhenRunning;

        public virtual Task<bool> Sure()
        {
            return Task.FromResult(false);
        }

        protected void Use(CommandLog log)
        {
            _log = log;
            _log.Subscribe(this);
        }

        private bool _inProgress = false;
        private string _progressDescription = string.Empty;
        private CommandLog _log = null;
    }
}
