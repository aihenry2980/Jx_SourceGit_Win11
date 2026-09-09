using System.Text.Json.Serialization;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SourceGit.ViewModels
{
    public class LayoutInfo : ObservableObject
    {
        public double LauncherWidth
        {
            get;
            set;
        } = 1280;

        public double LauncherHeight
        {
            get;
            set;
        } = 720;

        public int LauncherPositionX
        {
            get;
            set;
        } = int.MinValue;

        public int LauncherPositionY
        {
            get;
            set;
        } = int.MinValue;

        public WindowState LauncherWindowState
        {
            get;
            set;
        } = WindowState.Normal;

        public double ToolbarRecursiveOperationWindowWidth
        {
            get;
            set;
        } = 1040;

        public double ToolbarRecursiveOperationWindowHeight
        {
            get;
            set;
        } = 520;

        public double RecursiveLocalChangeDiffWindowWidth
        {
            get;
            set;
        } = 0;

        public double RecursiveLocalChangeDiffWindowHeight
        {
            get;
            set;
        } = 760;

        public int RecursiveLocalChangeDiffWindowPositionX
        {
            get;
            set;
        } = int.MinValue;

        public int RecursiveLocalChangeDiffWindowPositionY
        {
            get;
            set;
        } = int.MinValue;

        public double SubmoduleFileChangeDiffWindowWidth
        {
            get;
            set;
        } = 1200;

        public double SubmoduleFileChangeDiffWindowHeight
        {
            get;
            set;
        } = 760;

        public int SubmoduleFileChangeDiffWindowPositionX
        {
            get;
            set;
        } = int.MinValue;

        public int SubmoduleFileChangeDiffWindowPositionY
        {
            get;
            set;
        } = int.MinValue;

        public bool RepositorySidebarCollapsed
        {
            get => _repositorySidebarCollapsed;
            set
            {
                if (SetProperty(ref _repositorySidebarCollapsed, value))
                {
                    OnPropertyChanged(nameof(RepositorySidebarMinWidth));
                    OnPropertyChanged(nameof(RepositorySidebarDisplayWidth));
                }
            }
        }

        public GridLength RepositorySidebarWidth
        {
            get;
            set;
        } = new GridLength(250, GridUnitType.Pixel);

        [JsonIgnore]
        public double RepositorySidebarMinWidth
        {
            get => _repositorySidebarCollapsed ? 48 : 200;
        }

        [JsonIgnore]
        public GridLength RepositorySidebarDisplayWidth
        {
            get => _repositorySidebarCollapsed ? new GridLength(48, GridUnitType.Pixel) : RepositorySidebarWidth;
            set
            {
                if (!_repositorySidebarCollapsed)
                {
                    RepositorySidebarWidth = value;
                    OnPropertyChanged();
                }
            }
        }

        public GridLength WorkingCopyLeftWidth
        {
            get => _workingCopyLeftWidth;
            set => SetProperty(ref _workingCopyLeftWidth, value);
        }

        public GridLength StashesLeftWidth
        {
            get => _stashesLeftWidth;
            set => SetProperty(ref _stashesLeftWidth, value);
        }

        public GridLength CommitDetailChangesLeftWidth
        {
            get => _commitDetailChangesLeftWidth;
            set => SetProperty(ref _commitDetailChangesLeftWidth, value);
        }

        public GridLength CommitDetailFilesLeftWidth
        {
            get => _commitDetailFilesLeftWidth;
            set => SetProperty(ref _commitDetailFilesLeftWidth, value);
        }

        public DataGridLength AuthorColumnWidth
        {
            get => _authorColumnWidth;
            set => SetProperty(ref _authorColumnWidth, new DataGridLength(value.Value, DataGridLengthUnitType.Pixel, 0, value.DisplayValue));
        }

        public DataGridLength SHAColumnWidth
        {
            get => _shaColumnWidth;
            set => SetProperty(ref _shaColumnWidth, new DataGridLength(value.Value, DataGridLengthUnitType.Pixel, 0, value.DisplayValue));
        }

        public DataGridLength DateTimeColumnWidth
        {
            get => _dateTimeColumnWidth;
            set => SetProperty(ref _dateTimeColumnWidth, new DataGridLength(value.Value, DataGridLengthUnitType.Pixel, 0, value.DisplayValue));
        }

        public int HistoryColumnLayoutVersion
        {
            get;
            set;
        } = 0;

        internal void Normalize()
        {
            if (HistoryColumnLayoutVersion < 1)
            {
                if (System.Math.Abs(_authorColumnWidth.Value - 120) < 0.01)
                    _authorColumnWidth = new DataGridLength(240, DataGridLengthUnitType.Pixel, 0, 240);

                HistoryColumnLayoutVersion = 1;
            }
        }

        private bool _repositorySidebarCollapsed = false;
        private GridLength _workingCopyLeftWidth = new GridLength(300, GridUnitType.Pixel);
        private GridLength _stashesLeftWidth = new GridLength(300, GridUnitType.Pixel);
        private GridLength _commitDetailChangesLeftWidth = new GridLength(256, GridUnitType.Pixel);
        private GridLength _commitDetailFilesLeftWidth = new GridLength(256, GridUnitType.Pixel);
        private DataGridLength _authorColumnWidth = new DataGridLength(240, DataGridLengthUnitType.Pixel, 0, 240);
        private DataGridLength _shaColumnWidth = new DataGridLength(72, DataGridLengthUnitType.Pixel, 0, 72);
        private DataGridLength _dateTimeColumnWidth = new DataGridLength(136, DataGridLengthUnitType.Pixel, 0, 136);
    }
}
