using System.Collections.Generic;

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
        public int Depth { get; set; } = 0;
        public double Indent => Depth * 18;
        public List<SubmoduleCommitFlowNode> Children { get; } = [];

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
                {
                    OnPropertyChanged(nameof(HasPushRemote));
                    OnPropertyChanged(nameof(PushRemote));
                    OnPropertyChanged(nameof(PushRemoteBranch));
                    OnPropertyChanged(nameof(PushTargetDescription));
                }
            }
        }

        public bool HasPushRemote => TrySplitUpstream(_upstream, out _, out _);
        public string PushRemote => TrySplitUpstream(_upstream, out var remote, out _) ? remote : string.Empty;
        public string PushRemoteBranch => TrySplitUpstream(_upstream, out _, out var branch) ? branch : string.Empty;
        public string PushTargetDescription => HasPushRemote ? $"Push to {Upstream}" : "No upstream remote";

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

        private static bool TrySplitUpstream(string upstream, out string remote, out string branch)
        {
            remote = string.Empty;
            branch = string.Empty;

            if (string.IsNullOrWhiteSpace(upstream))
                return false;

            var idx = upstream.IndexOf('/');
            if (idx <= 0 || idx == upstream.Length - 1)
                return false;

            remote = upstream.Substring(0, idx);
            branch = upstream.Substring(idx + 1);
            return true;
        }

        private string _branch = "--";
        private string _head = string.Empty;
        private string _upstream = string.Empty;
        private int _changeCount = 0;
        private int _fileChangeCount = 0;
        private int _submodulePointerChangeCount = 0;
        private SubmoduleCommitFlowNodeState _state = SubmoduleCommitFlowNodeState.Scanning;
    }
}
