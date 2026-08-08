using System.Collections.Generic;
using System.Linq;

using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SourceGit.ViewModels
{
    public enum SubmoduleCommitFlowNodeState
    {
        Scanning,
        Clean,
        HasChildChanges,
        HasChanges,
        HasSubmodulePointerChanges,
        HasMixedChanges,
        Done,
        Error,
    }

    public class SubmoduleCommitFlowNode : ObservableObject
    {
        public string Name { get; set; } = string.Empty;
        public string DisplayPath { get; set; } = string.Empty;
        public string RepoPath { get; set; } = string.Empty;
        public string ParentDisplayPath { get; set; } = string.Empty;
        public string SubmodulePathInParent { get; set; } = string.Empty;
        public string DisplayName => DisplayPath == "root" ? "root" : SubmodulePathInParent;
        public int Depth { get; set; } = 0;
        public double Indent => Depth * 18;
        public List<SubmoduleCommitFlowHierarchyDot> HierarchyDots => Enumerable
            .Range(0, Depth)
            .Select(i => new SubmoduleCommitFlowHierarchyDot(i))
            .ToList();
        public List<SubmoduleCommitFlowNode> Children { get; } = [];

        public bool IsSelectedInCommitFlow
        {
            get => _isSelectedInCommitFlow;
            set
            {
                if (SetProperty(ref _isSelectedInCommitFlow, value))
                {
                    OnPropertyChanged(nameof(SelectionArrow));
                    OnPropertyChanged(nameof(SelectionBorderBrush));
                    OnPropertyChanged(nameof(SelectionBorderThickness));
                }
            }
        }

        public string SelectionArrow => _isSelectedInCommitFlow ? ">" : string.Empty;
        public IBrush SelectionBorderBrush => _isSelectedInCommitFlow ? BorderBrush : Brushes.Transparent;
        public Avalonia.Thickness SelectionBorderThickness => _isSelectedInCommitFlow ? new Avalonia.Thickness(1.5) : new Avalonia.Thickness(0);

        public string Branch
        {
            get => _branch;
            set => SetProperty(ref _branch, value);
        }

        public string Head
        {
            get => _head;
            set
            {
                if (SetProperty(ref _head, value))
                    OnPropertyChanged(nameof(HeadShort));
            }
        }

        public string HeadShort => string.IsNullOrEmpty(_head) ? "--" : (_head.Length > 8 ? _head.Substring(0, 8) : _head);

        public string Upstream
        {
            get => _upstream;
            set
            {
                if (SetProperty(ref _upstream, value))
                    NotifyPushTargetChanged();
            }
        }

        public string PushRemote
        {
            get => _pushRemote;
            set
            {
                if (SetProperty(ref _pushRemote, value))
                    NotifyPushTargetChanged();
            }
        }

        public string PushRemoteBranch
        {
            get => _pushRemoteBranch;
            set
            {
                if (SetProperty(ref _pushRemoteBranch, value))
                    NotifyPushTargetChanged();
            }
        }

        public bool SetPushTracking
        {
            get => _setPushTracking;
            set => SetProperty(ref _setPushTracking, value);
        }

        public bool HasPushRemote => !string.IsNullOrWhiteSpace(PushRemote) && !string.IsNullOrWhiteSpace(PushRemoteBranch);
        public string PushTargetDescription => HasPushRemote
            ? SetPushTracking ? $"Push to {PushRemote}/{PushRemoteBranch} and set upstream" : $"Push to {PushRemote}/{PushRemoteBranch}"
            : "No remote server is available";

        public int ChangeCount
        {
            get => _changeCount;
            set
            {
                if (SetProperty(ref _changeCount, value))
                    OnPropertyChanged(nameof(StatusText));
            }
        }

        public int FileChangeCount
        {
            get => _fileChangeCount;
            set
            {
                if (SetProperty(ref _fileChangeCount, value))
                    OnPropertyChanged(nameof(StatusText));
            }
        }

        public int SubmodulePointerChangeCount
        {
            get => _submodulePointerChangeCount;
            set
            {
                if (SetProperty(ref _submodulePointerChangeCount, value))
                    OnPropertyChanged(nameof(StatusText));
            }
        }

        public SubmoduleCommitFlowNodeState State
        {
            get => _state;
            set
            {
                if (SetProperty(ref _state, value))
                {
                    OnPropertyChanged(nameof(StatusText));
                    OnPropertyChanged(nameof(StatusForeground));
                    OnPropertyChanged(nameof(StatusBackground));
                    OnPropertyChanged(nameof(BorderBrush));
                    OnPropertyChanged(nameof(SelectionBorderBrush));
                }
            }
        }

        public string StatusText => State switch
        {
            SubmoduleCommitFlowNodeState.Scanning => "scanning",
            SubmoduleCommitFlowNodeState.Clean => "clean",
            SubmoduleCommitFlowNodeState.HasChildChanges => "child changes",
            SubmoduleCommitFlowNodeState.HasChanges => $"{ChangeCount} changes",
            SubmoduleCommitFlowNodeState.HasSubmodulePointerChanges => $"{ChangeCount} SPP changes",
            SubmoduleCommitFlowNodeState.HasMixedChanges => $"{FileChangeCount} file + {SubmodulePointerChangeCount} SPP",
            SubmoduleCommitFlowNodeState.Done => "done",
            SubmoduleCommitFlowNodeState.Error => "error",
            _ => string.Empty,
        };

        public IBrush StatusForeground => State switch
        {
            SubmoduleCommitFlowNodeState.Scanning => Brushes.Gray,
            SubmoduleCommitFlowNodeState.Clean => Brushes.Gray,
            SubmoduleCommitFlowNodeState.HasChildChanges => Brushes.DarkCyan,
            SubmoduleCommitFlowNodeState.HasChanges => Brushes.DarkOrange,
            SubmoduleCommitFlowNodeState.HasSubmodulePointerChanges => Brushes.RoyalBlue,
            SubmoduleCommitFlowNodeState.HasMixedChanges => Brushes.DarkMagenta,
            SubmoduleCommitFlowNodeState.Done => Brushes.ForestGreen,
            SubmoduleCommitFlowNodeState.Error => Brushes.Red,
            _ => Brushes.Gray,
        };

        public IBrush StatusBackground => State switch
        {
            SubmoduleCommitFlowNodeState.Scanning => new SolidColorBrush(Color.FromArgb(18, 120, 120, 120)),
            SubmoduleCommitFlowNodeState.Clean => new SolidColorBrush(Color.FromArgb(18, 120, 120, 120)),
            SubmoduleCommitFlowNodeState.HasChildChanges => new SolidColorBrush(Color.FromArgb(36, 0, 139, 139)),
            SubmoduleCommitFlowNodeState.HasChanges => new SolidColorBrush(Color.FromArgb(34, 255, 152, 0)),
            SubmoduleCommitFlowNodeState.HasSubmodulePointerChanges => new SolidColorBrush(Color.FromArgb(34, 30, 144, 255)),
            SubmoduleCommitFlowNodeState.HasMixedChanges => new SolidColorBrush(Color.FromArgb(34, 186, 85, 211)),
            SubmoduleCommitFlowNodeState.Done => new SolidColorBrush(Color.FromArgb(34, 34, 139, 34)),
            SubmoduleCommitFlowNodeState.Error => new SolidColorBrush(Color.FromArgb(34, 255, 0, 0)),
            _ => Brushes.Transparent,
        };

        public IBrush BorderBrush => State switch
        {
            SubmoduleCommitFlowNodeState.Scanning => Brushes.Gray,
            SubmoduleCommitFlowNodeState.HasChildChanges => Brushes.DarkCyan,
            SubmoduleCommitFlowNodeState.HasChanges => Brushes.DarkOrange,
            SubmoduleCommitFlowNodeState.HasSubmodulePointerChanges => Brushes.RoyalBlue,
            SubmoduleCommitFlowNodeState.HasMixedChanges => Brushes.DarkMagenta,
            SubmoduleCommitFlowNodeState.Done => Brushes.ForestGreen,
            SubmoduleCommitFlowNodeState.Error => Brushes.Red,
            _ => Brushes.Transparent,
        };

        private void NotifyPushTargetChanged()
        {
            OnPropertyChanged(nameof(HasPushRemote));
            OnPropertyChanged(nameof(PushRemote));
            OnPropertyChanged(nameof(PushRemoteBranch));
            OnPropertyChanged(nameof(PushTargetDescription));
        }

        private string _branch = "--";
        private string _head = string.Empty;
        private string _upstream = string.Empty;
        private string _pushRemote = string.Empty;
        private string _pushRemoteBranch = string.Empty;
        private bool _setPushTracking = false;
        private bool _isSelectedInCommitFlow = false;
        private int _changeCount = 0;
        private int _fileChangeCount = 0;
        private int _submodulePointerChangeCount = 0;
        private SubmoduleCommitFlowNodeState _state = SubmoduleCommitFlowNodeState.Scanning;
    }

    public class SubmoduleCommitFlowHierarchyDot
    {
        public SubmoduleCommitFlowHierarchyDot(int index)
        {
            Index = index;
        }

        public int Index { get; }
    }
}
