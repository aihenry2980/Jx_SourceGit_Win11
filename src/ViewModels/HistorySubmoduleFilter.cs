using System;
using System.Collections.Generic;

using CommunityToolkit.Mvvm.ComponentModel;

namespace SourceGit.ViewModels
{
    public class HistorySubmoduleFilterItem : ObservableObject
    {
        public Models.Submodule Submodule { get; }

        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        public HistorySubmoduleFilterItem(Models.Submodule submodule, bool isSelected)
        {
            Submodule = submodule;
            _isSelected = isSelected;
        }

        private bool _isSelected;
    }

    public class HistorySubmoduleFilter
    {
        public List<HistorySubmoduleFilterItem> Items { get; } = [];

        public HistorySubmoduleFilter(Repository repo)
        {
            var selected = new HashSet<string>(StringComparer.Ordinal);
            foreach (var filter in repo.UIStates.HistoryFilters)
            {
                if (filter.Type == Models.FilterType.Path && filter.Mode == Models.FilterMode.Included)
                    selected.Add(filter.Pattern);
            }

            var submodules = new List<Models.Submodule>(repo.Submodules);
            submodules.Sort((x, y) => string.Compare(x.Path, y.Path, StringComparison.Ordinal));
            foreach (var submodule in submodules)
                Items.Add(new HistorySubmoduleFilterItem(submodule, selected.Contains(submodule.Path)));
        }

        public void SelectAll()
        {
            foreach (var item in Items)
                item.IsSelected = true;
        }

        public void ClearSelection()
        {
            foreach (var item in Items)
                item.IsSelected = false;
        }

        public List<string> GetSelectedPaths()
        {
            var paths = new List<string>();
            foreach (var item in Items)
            {
                if (item.IsSelected)
                    paths.Add(item.Submodule.Path);
            }

            return paths;
        }
    }
}
