using System;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SourceGit.ViewModels
{
    public class PresetBranchFilterDraft : ObservableObject
    {
        public string ExactNames
        {
            get => _exactNames;
            set
            {
                if (SetProperty(ref _exactNames, value ?? string.Empty))
                    NotifyChanges();
            }
        }

        public string ContainsPatterns
        {
            get => _containsPatterns;
            set
            {
                if (SetProperty(ref _containsPatterns, value ?? string.Empty))
                    NotifyChanges();
            }
        }

        public string ExcludeNames
        {
            get => _excludeNames;
            set
            {
                if (SetProperty(ref _excludeNames, value ?? string.Empty))
                    NotifyChanges();
            }
        }

        public string ExactInput
        {
            get => _exactInput;
            set
            {
                if (SetProperty(ref _exactInput, value ?? string.Empty))
                    NotifyChanges();
            }
        }

        public string ContainsInput
        {
            get => _containsInput;
            set
            {
                if (SetProperty(ref _containsInput, value ?? string.Empty))
                    NotifyChanges();
            }
        }

        public string ExcludeInput
        {
            get => _excludeInput;
            set
            {
                if (SetProperty(ref _excludeInput, value ?? string.Empty))
                    NotifyChanges();
            }
        }

        public bool HasChanges => ExactNames != _repo.PresetBranchExactNames ||
            ContainsPatterns != _repo.PresetBranchContainsPatterns || ExcludeNames != _repo.PresetBranchExcludeNames ||
            !string.IsNullOrWhiteSpace(ExactInput) || !string.IsNullOrWhiteSpace(ContainsInput) || !string.IsNullOrWhiteSpace(ExcludeInput);

        public PresetBranchFilterDraft(Repository repo)
        {
            _repo = repo;
            Reset();
        }

        public void Reset()
        {
            _appliedExact = _repo.PresetBranchExactNames;
            _appliedContains = _repo.PresetBranchContainsPatterns;
            _appliedExclude = _repo.PresetBranchExcludeNames;
            ExactNames = _appliedExact;
            ContainsPatterns = _appliedContains;
            ExcludeNames = _appliedExclude;
            ExactInput = ContainsInput = ExcludeInput = string.Empty;
            NotifyChanges();
        }

        public void Apply()
        {
            _applying = true;
            try
            {
                _repo.PresetBranchExactNames = AppendInput(ExactNames, ExactInput);
                _repo.PresetBranchContainsPatterns = AppendInput(ContainsPatterns, ContainsInput);
                _repo.PresetBranchExcludeNames = AppendInput(ExcludeNames, ExcludeInput);
                _repo.ApplyPresetBranchFilter();
                Reset();
            }
            finally { _applying = false; }
        }

        public void SynchronizeAppliedRules()
        {
            if (_applying)
                return;

            // Preserve pending edits when another entry point adds a branch to the active filter.
            ExactNames = Rebase(ExactNames, _appliedExact, _repo.PresetBranchExactNames);
            ContainsPatterns = Rebase(ContainsPatterns, _appliedContains, _repo.PresetBranchContainsPatterns);
            ExcludeNames = Rebase(ExcludeNames, _appliedExclude, _repo.PresetBranchExcludeNames);
            _appliedExact = _repo.PresetBranchExactNames;
            _appliedContains = _repo.PresetBranchContainsPatterns;
            _appliedExclude = _repo.PresetBranchExcludeNames;
            NotifyChanges();
        }

        private static string Rebase(string draft, string previous, string current)
        {
            if (previous == current)
                return draft;
            if (draft == previous)
                return current;

            var oldRules = SplitRules(previous);
            var newRules = SplitRules(current);
            return string.Join('\n', SplitRules(draft).Except(oldRules.Except(newRules, StringComparer.Ordinal), StringComparer.Ordinal)
                .Concat(newRules.Except(oldRules, StringComparer.Ordinal)).Distinct(StringComparer.Ordinal));
        }

        private static string[] SplitRules(string rules) => (rules ?? string.Empty)
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        private static string AppendInput(string rules, string input) => string.Join('\n', SplitRules(rules)
            .Concat((input ?? string.Empty).Split(['\r', '\n', '\t', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Distinct(StringComparer.Ordinal));

        private void NotifyChanges() => OnPropertyChanged(nameof(HasChanges));

        private readonly Repository _repo;
        private string _exactNames = string.Empty;
        private string _containsPatterns = string.Empty;
        private string _excludeNames = string.Empty;
        private string _exactInput = string.Empty;
        private string _containsInput = string.Empty;
        private string _excludeInput = string.Empty;
        private string _appliedExact;
        private string _appliedContains;
        private string _appliedExclude;
        private bool _applying;
    }
}
