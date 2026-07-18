using System;
using System.Collections.Generic;
using System.Linq;

namespace SourceGit.ViewModels
{
    public class RebaseBaseBranchChoice
    {
        public Models.Branch Branch { get; }

        public string DisplayName => IsNone ? "-none-" : Branch.FriendlyName;

        public bool IsNone => Branch == null;

        public RebaseBaseBranchChoice(Models.Branch branch)
        {
            Branch = branch;
        }
    }

    public class SelectRebaseBaseBranch : Popup
    {
        public string CurrentBranchName { get; }

        public bool IsCurrentBranchMissing { get; }

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                {
                    OnPropertyChanged(nameof(HasSearchText));
                    RefreshVisibleBranches();
                }
            }
        }

        public bool HasSearchText => !string.IsNullOrWhiteSpace(_searchText);

        public List<RebaseBaseBranchChoice> SuggestedBranches { get; }

        public List<RebaseBaseBranchChoice> VisibleBranches
        {
            get => _visibleBranches;
            private set => SetProperty(ref _visibleBranches, value);
        }

        public override bool ShowOptions => false;

        public override double PopupWidth => 480;

        public SelectRebaseBaseBranch(Repository repo, string configuredBranchName)
        {
            _repo = repo;
            CurrentBranchName = configuredBranchName;
            var current = repo.GetRebaseBaseBranch();
            IsCurrentBranchMissing = current == null;
            _branches = BuildBranchList(repo);
            SuggestedBranches = [_noneChoice, .. _branches.Take(9).Select(x => new RebaseBaseBranchChoice(x))];
            RefreshVisibleBranches();
        }

        public void Select(RebaseBaseBranchChoice choice)
        {
            if (choice == null)
                return;

            if (choice.IsNone)
                _repo.ClearRebaseBaseBranch();
            else
                _repo.SetRebaseBaseBranch(choice.Branch);

            _repo.ClosePopup();
        }

        public void ConfirmSearchText()
        {
            var input = _searchText?.Trim();
            if (string.IsNullOrEmpty(input))
                return;

            if (input.Equals("-none-", StringComparison.OrdinalIgnoreCase) ||
                input.Equals("none", StringComparison.OrdinalIgnoreCase))
            {
                Select(_noneChoice);
                return;
            }

            var branch = _branches.Find(x => x.FriendlyName.Equals(input, StringComparison.OrdinalIgnoreCase));
            branch ??= _branches.Find(x => x.FullName.Equals(input, StringComparison.OrdinalIgnoreCase));
            branch ??= _branches.Find(x => x.Name.Equals(input, StringComparison.OrdinalIgnoreCase));
            if (branch == null)
            {
                _repo.SendNotification($"Branch '{input}' doesn't exist.", true);
                return;
            }

            Select(new RebaseBaseBranchChoice(branch));
        }

        public void Cancel()
        {
            _repo.ClosePopup();
        }

        private void RefreshVisibleBranches()
        {
            if (string.IsNullOrWhiteSpace(_searchText))
            {
                VisibleBranches = [];
                return;
            }

            var filter = _searchText.Trim();
            var visible = _branches
                .Where(x => x.FriendlyName.Contains(filter, StringComparison.OrdinalIgnoreCase))
                .Take(100)
                .Select(x => new RebaseBaseBranchChoice(x))
                .ToList();
            if ("-none-".Contains(filter, StringComparison.OrdinalIgnoreCase))
                visible.Insert(0, _noneChoice);
            VisibleBranches = visible;
        }

        private static List<Models.Branch> BuildBranchList(Repository repo)
        {
            var source = repo.Branches.Where(x => !x.IsDetachedHead).ToList();
            var result = new List<Models.Branch>(source.Count);
            var added = new HashSet<string>(StringComparer.Ordinal);

            foreach (var preferredName in new[] { "develop", "main", "master" })
            {
                var preferred = source
                    .Where(x => x.Name.Equals(preferredName, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(x => x.IsLocal)
                    .ThenByDescending(x => string.Equals(x.Remote, repo.Settings?.DefaultRemote, StringComparison.Ordinal))
                    .ThenBy(x => x.FriendlyName, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();
                AddBranch(preferred);
            }

            AddBranch(source.Find(x => x.IsCurrent));
            foreach (var branch in source
                         .OrderByDescending(x => x.IsLocal)
                         .ThenBy(x => x.FriendlyName, StringComparer.OrdinalIgnoreCase))
            {
                AddBranch(branch);
            }

            return result;

            void AddBranch(Models.Branch branch)
            {
                if (branch != null && added.Add(branch.FullName))
                    result.Add(branch);
            }
        }

        private readonly Repository _repo;
        private readonly List<Models.Branch> _branches;
        private readonly RebaseBaseBranchChoice _noneChoice = new(null);
        private string _searchText = string.Empty;
        private List<RebaseBaseBranchChoice> _visibleBranches = [];
    }
}
