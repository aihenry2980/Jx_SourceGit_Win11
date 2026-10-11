using System.Diagnostics;
using System.Reflection;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;

using SourceGit.Models;
using Finder = SourceGit.ViewModels.FindContainingBranches;

internal static partial class Program
{
    private static void TestContainingBranches()
    {
        var path = NewRepository("containing branches");
        Git(path, "commit", "--allow-empty", "-m", "base");
        Git(path, "branch", "older");
        Git(path, "commit", "--allow-empty", "-m", "detached commit to find");
        var sha = Git(path, "rev-parse", "HEAD");
        Git(path, "branch", "feature");
        var longName = "feature-long/customer-navigation-with-a-very-long-branch-name-to-check-result-layout-and-trimming";
        Git(path, "branch", longName);
        Git(path, "remote", "add", "origin", path);
        Git(path, "update-ref", "refs/remotes/origin/feature", sha);
        Git(path, "symbolic-ref", "refs/remotes/origin/HEAD", "refs/remotes/origin/feature");
        Git(path, "tag", "release-tag", sha);
        Git(path, "commit", "--allow-empty", "-m", "later commit on main");
        Git(path, "checkout", "--detach", sha);

        var repo = Open(path);
        SourceGit.Views.FindContainingBranches window = null;
        try
        {
            repo.PresetBranchExactNames = "older";
            repo.PresetBranchContainsPatterns = "release/";
            repo.PresetBranchExcludeNames = "feature\nblocked";
            repo.ApplyPresetBranchFilter();
            var commit = Wait(new SourceGit.Commands.QuerySingleCommit(path, sha).GetResultAsync());
            Check(repo.CurrentBranch.IsDetachedHead, "Containing branch search works from detached HEAD");
            var histories = new SourceGit.Views.Histories { DataContext = repo.Histories };
            var menu = (ContextMenu)Invoke(histories, "CreateContextMenuForSingleCommit", repo, commit, true, null);
            var find = menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, SourceGit.App.Text("FindContainingBranches.Title")));
            Check(find.Icon != null && find.IsEnabled, "SHA context menu exposes Find containing branches");
            find.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            window = _lifetime.Windows.OfType<SourceGit.Views.FindContainingBranches>().Single();
            var vm = (Finder)window.DataContext;
            WaitUntil(() => !vm.IsSearching && vm.VisibleResults.Count > 0, "Branch search did not finish.");
            Check(vm.SHA == sha, "Context action searches the clicked SHA, not a different selected commit");
            Check(vm.VisibleResults.Select(result => result.Name).SequenceEqual(new[] { "feature", longName, "main", "origin/feature" }),
                "Search includes tip branches and descendant branches, excluding tags, symbolic HEAD and unrelated histories");
            Check(vm.VisibleResults.Count(result => result.IsLocal) == 3 && vm.VisibleResults.Count(result => !result.IsLocal) == 1,
                "Result rows distinguish local and remote-tracking branches");
            Check(repo.PresetBranchExactNames == "older" && repo.PresetBranchExcludeNames == "feature\nblocked",
                "Searching alone never changes existing filters");
            Check(vm.VisibleResults.All(result => ((ISolidColorBrush)result.Color).Color.ToUInt32() == repo.GetEffectiveBranchDisplayColor(result.Branch)),
                "Result accents use the same branch colors as the graph");

            vm.SearchText = "ORIGIN/";
            Check(vm.VisibleResults is [{ Name: "origin/feature" }], "Result search is case-insensitive and preserves remote identity");
            vm.SearchText = "no-match";
            Check(vm.IsEmpty, "Filtering result rows exposes a no-match state");
            vm.SearchText = string.Empty;
            PumpFor(100);
            var row = window.GetVisualDescendants().OfType<Grid>().First(control => control.DataContext is SourceGit.ViewModels.ContainingBranch { Name: "origin/feature" } && control.MinHeight == 32);
            Invoke(window, "OnBranchContextRequested", row, new ContextRequestedEventArgs());
            var addMenu = (ContextMenu)typeof(SourceGit.Views.FindContainingBranches).GetField("_branchContextMenu", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(window);
            Check(addMenu.IsOpen && Equals(((MenuItem)addMenu.Items[0]).Header, SourceGit.App.Text("FindContainingBranches.AddToFilter")),
                "Right-clicking a result opens Add to filter for that branch");
            ((MenuItem)addMenu.Items[0]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            addMenu.Close();
            Check(repo.PresetBranchExactNames.Split('\n').SequenceEqual(new[] { "older", "feature" }) &&
                repo.PresetBranchContainsPatterns == "release/" && repo.PresetBranchExcludeNames == "blocked",
                "Adding a branch appends its name, preserves unrelated rules and removes only its conflicting exclusion");
            Check(repo.UIStates.HistoryFilters.Any(filter => filter.Pattern == "refs/remotes/origin/feature" && filter.Mode == FilterMode.Included),
                "Added remote branch is actually included in the graph, not just listed in the textbox");
            Check(repo.IsPresetBranchFilterEditorExpanded, "Adding a result keeps the filter editor expanded");
            Check(vm.VisibleResults.Where(result => result.Branch.Name == "feature").All(result =>
                ((ISolidColorBrush)result.Color).Color.ToUInt32() == repo.GetEffectiveBranchDisplayColor(result.Branch)),
                "Result colors update when adding a name assigns graph colors to local and remote branches");
            vm.AddToFilter(vm.VisibleResults.Single(result => result.Name == "feature"));
            Check(repo.PresetBranchExactNames.Split('\n').Count(name => name == "feature") == 1, "Local and remote results do not duplicate an existing filter name");
            Check(repo.CurrentBranch.IsDetachedHead && repo.CurrentBranch.Head == sha, "Adding to a filter never checks out or creates a branch");

            foreach (var width in new[] { 680, 420 })
            {
                window.Width = width;
                PumpFor(100);
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                using var frame = window.CaptureRenderedFrame();
                Check(frame != null, "Containing branch results render at width " + width);
                frame.Save(Path.Combine(_root, "containing-branches-" + width + ".png"));
            }
            SourceGit.App.SetLocale("zh_CN");
            Application.Current.RequestedThemeVariant = ThemeVariant.Dark;
            Wait(vm.SearchAsync());
            PumpFor(100);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            using (var frame = window.CaptureRenderedFrame())
            {
                Check(frame != null && vm.Status.Contains('4'), "Chinese dark-theme search results render with localized status");
                frame.Save(Path.Combine(_root, "containing-branches-dark-zh.png"));
            }
            SourceGit.App.SetLocale("en_US");
            Application.Current.RequestedThemeVariant = ThemeVariant.Light;

            var tree = Git(path, "rev-parse", "HEAD^{tree}");
            var unreferenced = Git(path, "commit-tree", tree, "-m", "unreferenced root");
            using var empty = new Finder(repo, new Commit { SHA = unreferenced, Subject = "unreferenced" });
            Wait(empty.SearchAsync());
            Check(empty.IsEmpty && empty.Status.Contains("No known branch"), "Valid but unreferenced SHA reports no containing branches");
            using var invalid = new Finder(repo, new Commit { SHA = new string('f', 40), Subject = "invalid object" });
            Wait(invalid.SearchAsync());
            Check(!invalid.IsSearching && invalid.Status.Contains("Search failed:"), "Git query failure is shown instead of being mistaken for no branches");
            Check(Wait(new SourceGit.Commands.QueryRefsContainsCommit(path, new string('f', 40)).GetResultAsync()).Count == 0,
                "Existing non-strict relation queries retain their failure behavior");
            var stale = vm.VisibleResults.Single(result => result.Name == longName);
            repo.Branches.Remove(stale.Branch);
            Check(!vm.AddToFilter(stale), "A stale result cannot silently add a branch no longer known to the repository");
            repo.Branches.Add(stale.Branch);
            window.Close();
            Check(!vm.AddToFilter(stale), "Closed search windows cannot mutate repository filters");
        }
        finally
        {
            window?.Close();
            repo.Close();
            SourceGit.App.SetLocale("en_US");
            Application.Current.RequestedThemeVariant = ThemeVariant.Light;
        }
        TestContainingBranchCancellation(path);
    }

    private static void RunContainingBranchesChild(string gate)
    {
        File.WriteAllText(Path.Combine(gate, "pid"), Environment.ProcessId.ToString());
        File.WriteAllText(Path.Combine(gate, "ready"), string.Empty);
        var timer = Stopwatch.StartNew();
        while (!File.Exists(Path.Combine(gate, "release")) && timer.Elapsed < TimeSpan.FromSeconds(60))
            Thread.Sleep(20);
    }

    private static void TestContainingBranchCancellation(string path)
    {
        var git = SourceGit.Native.OS.GitExecutable;
        var gate = Path.Combine(_root, "branch query gate");
        Directory.CreateDirectory(gate);
        Environment.SetEnvironmentVariable("SOURCEGIT_TEST_CONTAINS_GATE", gate);
        try
        {
            SourceGit.Native.OS.GitExecutable = Path.ChangeExtension(typeof(Program).Assembly.Location, ".exe");
            foreach (var timeout in new[] { false, true })
            {
                File.Delete(Path.Combine(gate, "ready"));
                var repo = new SourceGit.ViewModels.Repository(false, path, Path.Combine(path, ".git"));
                using var vm = new Finder(repo, new Commit { SHA = new string('a', 40) });
                var operation = vm.SearchAsync();
                Check(vm.IsSearching, "Branch query displays its loading state immediately");
                WaitUntil(() => File.Exists(Path.Combine(gate, "ready")), "Controlled query did not start.");
                using var child = Process.GetProcessById(int.Parse(File.ReadAllText(Path.Combine(gate, "pid"))));
                if (timeout)
                    ((CancellationTokenSource)typeof(Finder).GetField("_cancellation", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(vm)).CancelAfter(50);
                else
                {
                    var elapsed = Stopwatch.StartNew();
                    vm.Cancel();
                    Check(elapsed.Elapsed < TimeSpan.FromMilliseconds(500), "Canceling branch search never terminates Git on the UI thread");
                }
                Wait(operation);
                WaitUntil(() => child.HasExited, "Canceled branch query left Git running.");
                Check(!vm.IsSearching && vm.Status.Contains(timeout ? "timed out" : "canceled"), "Query cancellation and timeout have distinct final states");
                Check(child.HasExited, "Canceled or timed-out query releases its Git process");
            }
            File.Delete(Path.Combine(gate, "ready"));
            var closedRepo = new SourceGit.ViewModels.Repository(false, path, Path.Combine(path, ".git"));
            using var closedVm = new Finder(closedRepo, new Commit { SHA = new string('a', 40) });
            var pending = closedVm.SearchAsync();
            var window = new SourceGit.Views.FindContainingBranches { DataContext = closedVm };
            window.Show();
            WaitUntil(() => File.Exists(Path.Combine(gate, "ready")), "Query window child did not start.");
            using var pendingChild = Process.GetProcessById(int.Parse(File.ReadAllText(Path.Combine(gate, "pid"))));
            var closedAt = Stopwatch.StartNew();
            window.Close();
            Check(closedAt.Elapsed < TimeSpan.FromMilliseconds(500), "Closing a busy query window does not block on terminating Git");
            Wait(pending);
            WaitUntil(() => pendingChild.HasExited, "Closing the query window left Git running.");
            Check(!closedVm.IsSearching && pendingChild.HasExited, "Closing the query window cancels and releases its Git process");
        }
        finally
        {
            File.WriteAllText(Path.Combine(gate, "release"), string.Empty);
            SourceGit.Native.OS.GitExecutable = git;
            Environment.SetEnvironmentVariable("SOURCEGIT_TEST_CONTAINS_GATE", null);
        }
    }
}
