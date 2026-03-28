using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SourceGit.ViewModels
{
    public class FilterModeInGraph : ObservableObject
    {
        public event Action<uint> BranchColorChanged;

        public bool IsFiltered
        {
            get => _mode == Models.FilterMode.Included;
            set => SetFilterMode(value ? Models.FilterMode.Included : Models.FilterMode.None);
        }

        public bool IsExcluded
        {
            get => _mode == Models.FilterMode.Excluded;
            set => SetFilterMode(value ? Models.FilterMode.Excluded : Models.FilterMode.None);
        }

        public bool IsBranchTarget
        {
            get => _target is Models.Branch;
        }

        public IReadOnlyList<PresetBranchColorOption> BranchColorOptions
        {
            get => Repository.BranchFilterColorOptions;
        }

        public PresetBranchColorOption SelectedBranchColor
        {
            get => _selectedBranchColor;
            set
            {
                if (SetProperty(ref _selectedBranchColor, value) &&
                    value != null &&
                    _target is Models.Branch branch)
                {
                    _repo.SetBranchDisplayColor(branch, value.Color);
                    BranchColorChanged?.Invoke(value.Color);
                }
            }
        }

        public FilterModeInGraph(Repository repo, object target, uint initialBranchColor = 0)
        {
            _repo = repo;
            _target = target;

            if (_target is Models.Branch b)
            {
                _mode = _repo.UIStates.GetHistoryFilterMode(b.FullName);
                var selectedColor = initialBranchColor != 0 ? initialBranchColor : _repo.GetEffectiveBranchDisplayColor(b);
                _selectedBranchColor = FindBranchColorOption(selectedColor);
            }
            else if (_target is Models.Tag t)
            {
                _mode = _repo.UIStates.GetHistoryFilterMode(t.Name);
            }
        }

        private void SetFilterMode(Models.FilterMode mode)
        {
            if (_mode != mode)
            {
                _mode = mode;

                if (_target is Models.Branch branch)
                {
                    if (_mode == Models.FilterMode.Included)
                    {
                        var color = _selectedBranchColor?.Color ?? Preferences.PRESET_BRANCH_EXACT_DEFAULT_COLOR;
                        _repo.SetBranchFilterColor(branch, color);
                        BranchColorChanged?.Invoke(color);
                    }
                    else
                    {
                        _repo.SetBranchFilterMode(branch, _mode, false, true);
                        var color = _selectedBranchColor?.Color ?? Preferences.PRESET_BRANCH_EXACT_DEFAULT_COLOR;
                        BranchColorChanged?.Invoke(color);
                    }
                }
                else if (_target is Models.Tag tag)
                {
                    _repo.SetTagFilterMode(tag, _mode);
                }

                OnPropertyChanged(nameof(IsFiltered));
                OnPropertyChanged(nameof(IsExcluded));
            }
        }

        private static PresetBranchColorOption FindBranchColorOption(uint color)
        {
            foreach (var option in Repository.BranchFilterColorOptions)
            {
                if (option.Color == color)
                    return option;
            }

            return Repository.BranchFilterColorOptions[0];
        }

        private Repository _repo = null;
        private object _target = null;
        private Models.FilterMode _mode = Models.FilterMode.None;
        private PresetBranchColorOption _selectedBranchColor = null;
    }
}
