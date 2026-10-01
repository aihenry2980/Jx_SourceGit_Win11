using System.Diagnostics;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Fonts;
using Avalonia.Threading;
using SourceGit.Models;
using Repo = SourceGit.ViewModels.Repository;
using Decorator = SourceGit.Models.Decorator;

internal static class Program
{
    private static string _root;
    private static int _checks;
    private static readonly Avalonia.Controls.ApplicationLifetimes.ClassicDesktopStyleApplicationLifetime _lifetime = new()
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown,
    };

    [STAThread]
    private static void Main()
    {
        _root = Path.Combine(Path.GetTempPath(), "SourceGit-regression-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        SourceGit.Native.OS.SetupBasicDirectories();
        SourceGit.Native.OS.BasicDirectories.ConfigDir = _root;
        SourceGit.Native.OS.BasicDirectories.CacheDir = _root;
        AppBuilder.Configure<RegressionApp>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .UseSkia()
            .WithInterFont()
            .ConfigureFonts(manager => manager.AddFontCollection(new EmbeddedFontCollection(
                new Uri("fonts:SourceGit"), new Uri("avares://SourceGit/Resources/Fonts"))))
            .SetupWithLifetime(_lifetime);
        SourceGit.Native.OS.GitExecutable = SourceGit.Native.OS.FindGitExecutable();
        Application.Current.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Light;
        CommitGraph.SetDefaultPens();

        TestNames();
        TestMissingBranches();
        TestFilterColorsAndCheckout();
        TestRendering();
        TestTrackingPairZoom();
        TestCommitMenuChips();
        TestPushConfirmation();
        Console.WriteLine($"PASS: {_checks} checks. Isolated data and preview: {_root}");
    }

    private static void TestNames()
    {
        const string sha = "0123456789abcdef0123456789abcdef01234567";
        foreach (var subject in new[] { "fix: hello world", "~^:?*[\\/@{", ".lock", "a..b.lock.", "", "quote\"test", "中文分支消息测试内容更多", "emoji 😀😀😀😀😀😀😀😀😀😀", "\0\t\n" })
        {
            var name = RefName.FromCommit(sha, subject);
            Check(RefName.IsValidBranchName(name), "Generated branch is valid: " + name);
            Git(_root, "check-ref-format", "--branch", name);
        }
        Check(RefName.FromCommit(sha, "fix: hello world") == "0123456789-fixhello", "Use SHA plus first ten message characters, stripping invalid characters");
        Check(!RefName.FromCommit(sha, "quote\"test").Contains('"'), "Generated branch names avoid command-line quote delimiters");
        Check(new TemplateEngine().Eval("${branch_name}", null, []).Length == 0, "Template tolerates missing branch");
        var worktree = new SourceGit.ViewModels.Worktree(new DirectoryInfo(_root),
            new Worktree { FullPath = Path.Combine(_root, "no-branch") }, false, true);
        Check(worktree.Name == "no-branch", "Worktree tolerates missing branch");
        var option = Wait(Task.Run(() => new SourceGit.ViewModels.PresetBranchColorOption("Orange", 0xFFF7630C)));
        Check(option.Brush is Avalonia.Media.Immutable.ImmutableSolidColorBrush, "Color palette can initialize on a worker thread");
    }

    private static void TestMissingBranches()
    {
        var empty = NewRepository("empty");
        var repo = Open(empty);
        PumpFor(500);
        Check(repo.CurrentBranch == null, "Empty repository has no current branch");
        repo.Close();
        repo.Open();
        PumpFor(500);
        Check(repo.CurrentBranch == null, "Empty repository can reopen");
        repo.Close();

        var missing = NewRepository("missing-head");
        Git(missing, "commit", "--allow-empty", "-m", "initial");
        Git(missing, "symbolic-ref", "HEAD", "refs/heads/missing");
        repo = Open(missing);
        PumpFor(500);
        Check(repo.CurrentBranch == null && repo.Branches.Count > 0, "Missing HEAD branch with other branches can open");
        repo.Close();
        repo.Open();
        PumpFor(500);
        Check(repo.CurrentBranch == null, "Missing HEAD branch can reopen");
        var commit = Wait(new SourceGit.Commands.QuerySingleCommit(missing, "main").GetResultAsync());
        var checkout = new SourceGit.ViewModels.CheckoutDetached(repo, commit);
        Check(Wait(checkout.Sure()), "Checkout commit succeeds without a current branch");
        Check(Git(missing, "branch", "--show-current") == checkout.BranchName, "Commit checkout creates an attached branch");
        repo.Close();

        Git(missing, "symbolic-ref", "HEAD", "refs/heads/still-missing");
        repo = Open(missing);
        Check(Wait(new SourceGit.ViewModels.Checkout(repo, repo.Branches.Single(b => b.IsLocal && b.Name == "main")).Sure()), "Existing branch checkout succeeds without a current branch");
        Check(repo.CurrentBranch?.Name == "main", "Existing branch checkout updates current branch");
        repo.Close();
    }

    private static void TestFilterColorsAndCheckout()
    {
        var path = NewRepository("filters");
        Git(path, "commit", "--allow-empty", "-m", "fix: hello world");
        Git(path, "branch", "feature");
        Git(path, "branch", "other");
        Git(path, "update-ref", "refs/remotes/origin/main", "HEAD");
        Git(path, "config", "branch.main.remote", "origin");
        Git(path, "config", "branch.main.merge", "refs/heads/main");
        Git(path, "remote", "add", "origin", path);
        var repo = Open(path);
        repo.PresetBranchExactNames = "main\nfeature";
        repo.ApplyPresetBranchFilter();
        var main = repo.UIStates.HistoryFilters.Single(f => f.Pattern == "refs/heads/main");
        var feature = repo.UIStates.HistoryFilters.Single(f => f.Pattern == "refs/heads/feature");
        var remote = repo.UIStates.HistoryFilters.Single(f => f.Pattern == "refs/remotes/origin/main");
        Check(main.Color != feature.Color, "Exact-name filters receive distinct automatic colors");
        Check(main.Color == remote.Color, "Tracking local and remote use the same color");
        repo.Settings.SetPresetBranchExactNameColor("feature", 0xFFF7630C);
        repo.ApplyPresetBranchFilter();
        Check(repo.UIStates.HistoryFilters.Single(f => f.Pattern == "refs/heads/feature").Color == 0xFFF7630C, "Explicit branch colors survive applying filters");
        repo.Settings.SetPresetBranchExactNameColor("other", 0xFFF7630C);
        var commits = Wait(new SourceGit.Commands.QueryCommits(path, "--all").GetResultAsync());
        Invoke(repo, "ApplyHistoryFilterColorsToDecorators", commits);
        Check(commits.SelectMany(c => c.Decorators).Where(d => d.Name == "other").All(d => (d.Color >> 24) <= 0x40), "Non-filter branch stays muted even with a saved color");

        var module = new Submodule { Path = "depends/module" };
        typeof(Repo).GetProperty(nameof(Repo.Submodules)).SetValue(repo, new List<Submodule> { module });
        repo.SetSubmoduleUpdateBadgeColor(module.Path, 0xFF744DA9);
        Check(module.AccentColor == repo.ResolveSubmoduleUpdateBadgeColor(module.Path), "Sidebar and graph share submodule colors");
        repo.ShowSubmodulesAsTree = true;
        Check(((SourceGit.ViewModels.SubmoduleCollectionAsTree)repo.VisibleSubmodules).Rows.Single(n => n.Module != null).AccentColor == module.AccentColor, "Tree uses graph color");

        var commit = commits[0];
        var baseName = RefName.FromCommit(commit.SHA, commit.Subject);
        Git(path, "branch", baseName);
        Git(path, "checkout", "--detach", "HEAD");
        Wait(InvokeTask(repo, "RefreshBranchesAsync"));
        Check(repo.CurrentBranch?.IsDetachedHead == true, "Detached repository loads current HEAD");
        var checkout = new SourceGit.ViewModels.CheckoutDetached(repo, commit);
        Check(checkout.BranchName == baseName + "-2", "Checkout avoids branch name collisions");
        Check(Wait(checkout.Sure()), "Checkout from detached HEAD succeeds");
        Check(Git(path, "branch", "--show-current") == checkout.BranchName, "Checkout remains attached");
        Git(path, "checkout", "--detach", "HEAD");
        Git(path, "commit", "--allow-empty", "-m", "unreferenced commit");
        Wait(InvokeTask(repo, "RefreshBranchesAsync"));
        var unreferenced = Wait(new SourceGit.Commands.QuerySingleCommit(path, "HEAD").GetResultAsync());
        Check(Wait(new SourceGit.Commands.QueryRefsContainsCommit(path, unreferenced.SHA).GetResultAsync()).Count == 0, "Detached HEAD test has no containing branch");
        var attach = new SourceGit.ViewModels.CheckoutDetached(repo, unreferenced);
        Check(Wait(attach.Sure()), "Unreferenced detached HEAD can be attached");
        Check(Git(path, "branch", "--show-current") == attach.BranchName, "Attaching HEAD does not show a lost-commits warning");
        repo.Close();
    }

    private static void TestRendering()
    {
        var commit = new Commit { Color = 0, Decorators = [
            new Decorator { Type = DecoratorType.LocalBranchHead, Name = "feature", Color = 0xFF744DA9 },
            new Decorator { Type = DecoratorType.RemoteBranchHead, Name = "origin/muted", Color = 0x18808080 },
            new Decorator { Type = DecoratorType.SuperProjectPointer, Name = "SPP" }] };
        var refs = new SourceGit.Views.CommitRefsPresenter { DataContext = commit, FontSize = 20,
            FontFamily = FontFamily.Default, Foreground = Brushes.Black, Background = Brushes.White, UseGraphColor = true };
        refs.Measure(Size.Infinity);
        var items = (List<SourceGit.Views.CommitRefsPresenter.RenderItem>)typeof(SourceGit.Views.CommitRefsPresenter)
            .GetField("_items", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(refs);
        var muted = items.Single(i => i.Decorator.Name == "origin/muted");
        var icon = ((ISolidColorBrush)muted.IconBrush).Color;
        Check(Math.Abs(icon.R - icon.G) < 15 && Math.Abs(icon.G - icon.B) < 15, "Muted remote icon is gray");
        Check(((ISolidColorBrush)muted.PrimaryIconBackground).Color.A < 0x40, "Muted remote icon has a pale background");
        Check(((ISolidColorBrush)muted.BorderBrush).Color.A < 0x80, "Muted remote border is subtle");
        var pair = new SourceGit.Views.CommitRefsPresenter { DataContext = new Commit { Color = 0, Decorators = [
            new Decorator { Type = DecoratorType.LocalBranchHead, Name = "muted", Color = 0x18808080 },
            new Decorator { Type = DecoratorType.RemoteBranchHead, Name = "origin/muted", Color = 0x18808080 }] },
            FontSize = 20, FontFamily = FontFamily.Default, Foreground = Brushes.Black,
            Background = Brushes.White, UseGraphColor = true, CompactTrackingBranches = true };
        pair.Measure(Size.Infinity);
        var pairItems = (List<SourceGit.Views.CommitRefsPresenter.RenderItem>)typeof(SourceGit.Views.CommitRefsPresenter)
            .GetField("_items", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(pair);
        Check(pairItems is [{ IsTrackingPair: true, IsMuted: true }], "Muted tracking pair keeps its L+R identity");
        var subject = new SourceGit.Views.CommitSubjectPresenter { Subject = "Fix issue #123",
            IssueTrackers = [new IssueTracker { RegexString = @"#(\d+)", URLTemplate = "https://example.com/issues/$1" }],
            FontSize = 20, Foreground = Brushes.Gray, LinkForeground = SourceGit.Converters.CommitConverters.SubjectLinkToBrush.Convert(commit, typeof(IBrush), null, null) as IBrush };
        var panel = new StackPanel { Background = Brushes.White, Margin = new Thickness(16), Spacing = 16 };
        panel.Children.Add(refs);
        panel.Children.Add(pair);
        panel.Children.Add(subject);
        panel.Children.Add(new SourceGit.Views.SubmoduleUpdateBadgeShape { Width = 100, Height = 26, AccentColor = 0xFF744DA9 });
        var window = new Window { Width = 900, Height = 280, Content = panel };
        window.Show();
        PumpFor(200);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var frame = window.CaptureRenderedFrame();
        Check(frame != null, "Preview renders");
        var inlines = (System.Collections.IEnumerable)typeof(SourceGit.Views.CommitSubjectPresenter)
            .GetField("_inlines", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(subject);
        var link = inlines.Cast<object>().Single(inline => inline.GetType().GetProperty("Element").GetValue(inline) is InlineElement { Type: InlineElementType.Link });
        var linkTypeface = (Typeface)link.GetType().GetProperty("Typeface").GetValue(link);
        Check(linkTypeface.Weight == FontWeight.Bold, "Issue links render bold");
        var linkBrush = (ISolidColorBrush)link.GetType().GetProperty("Brush").GetValue(link);
        Check(linkBrush.Color.R == linkBrush.Color.G && linkBrush.Color.G == linkBrush.Color.B && linkBrush.Color.B < 0x80, "Issue links use dark neutral text in light theme");
        frame.Save(Path.Combine(_root, "graph-preview.png"));
        window.Close();
    }

    private static void TestTrackingPairZoom()
    {
        var field = typeof(SourceGit.Views.CommitRefsPresenter).GetField("_items", BindingFlags.NonPublic | BindingFlags.Instance);
        var panel = new StackPanel { Background = Brushes.White, Margin = new Thickness(16), Spacing = 12 };
        foreach (var type in new[] { DecoratorType.CurrentBranchHead, DecoratorType.LocalBranchHead })
        {
            var presenter = new SourceGit.Views.CommitRefsPresenter
            {
                DataContext = new Commit { Color = 0, Decorators = [
                    new Decorator { Type = type, Name = "master", Color = 0xFF2E8B57, IsRebaseBaseBranch = true, IsBranchFoldable = true },
                    new Decorator { Type = DecoratorType.RemoteBranchHead, Name = "origin/master", Color = 0xFF2E8B57 }] },
                FontFamily = FontFamily.Default, Foreground = Brushes.Black, Background = Brushes.White,
                UseGraphColor = true, CompactTrackingBranches = true,
            };
            foreach (var size in new[] { 10.5, 14, 21, 35, 14, 35 })
            {
                presenter.FontSize = size;
                presenter.Measure(Size.Infinity);
                var item = ((List<SourceGit.Views.CommitRefsPresenter.RenderItem>)field.GetValue(presenter)).Single();
                var badgeWidth = item.LeadingWidth - 4 - (item.RebaseBaseIcon != null ? 14 : 0);
                Check(item.TrackingPairLabel.WidthIncludingTrailingWhitespace + 8 <= badgeWidth,
                    $"L+R text fits its background with padding: {type}, font {size}");
                Check(item.TrackingPairLabel.Height <= Math.Max(14, item.Height - 4),
                    $"L+R text fits the chip height: {type}, font {size}");
            }
            panel.Children.Add(presenter);
        }
        var window = new Window { Width = 900, Height = 220, Content = panel };
        window.Show();
        PumpFor(200);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var frame = window.CaptureRenderedFrame();
        Check(frame != null, "Zoomed tracking pairs render");
        frame.Save(Path.Combine(_root, "tracking-pair-zoom-preview.png"));
        window.Close();
    }

    private static void TestCommitMenuChips()
    {
        var path = NewRepository("commit-menu-chips");
        Git(path, "commit", "--allow-empty", "-m", "initial");
        var sha = Git(path, "rev-parse", "HEAD");
        Git(path, "commit", "--allow-empty", "-m", "next");
        var repo = Open(path);
        try
        {
            var commit = Wait(new SourceGit.Commands.QuerySingleCommit(path, sha).GetResultAsync());
            var view = new SourceGit.Views.Histories { DataContext = repo.Histories };
            var menu = (ContextMenu)Invoke(view, "CreateContextMenuForSingleCommit", repo, commit, false, null);
            var panel = new StackPanel { Background = Brushes.White, Margin = new Thickness(16), Spacing = 12 };
            foreach (var key in new[] { "CommitCM.CherryPick", "CommitCM.Revert", "CommitCM.Checkout" })
            {
                var action = SourceGit.App.Text(key).TrimEnd('.', '\u2026');
                var item = menu.Items.OfType<MenuItem>().Single(i => i.Header is StackPanel header &&
                    header.Children.FirstOrDefault() is TextBlock text && text.Text == action);
                var header = (StackPanel)item.Header;
                var chip = header.Children.OfType<Border>().Single();
                var label = (TextBlock)chip.Child;
                Check(label.Text == sha[..10], key + " shows the selected commit SHA in a chip");
                Check(ToolTip.GetTip(chip)?.ToString() == sha, key + " exposes the full SHA in a tooltip");
                var background = ((ISolidColorBrush)chip.Background).Color;
                Check(background.R > 0xC0 && Math.Abs(background.R - background.B) < 15,
                    key + " uses a pale silver background");
                var row = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 10, Height = 36 };
                row.Children.Add((Control)item.Icon);
                row.Children.Add(header);
                panel.Children.Add(row);
            }
            var window = new Window { Width = 640, Height = 200, Content = panel };
            window.Show();
            PumpFor(200);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            using var frame = window.CaptureRenderedFrame();
            Check(frame != null, "Commit action SHA chips render");
            frame.Save(Path.Combine(_root, "commit-menu-sha-preview.png"));
            window.Close();
        }
        finally
        {
            repo.Close();
        }
    }

    private static void TestPushConfirmation()
    {
        var path = NewRepository("push-confirmation");
        var remotePath = Path.Combine(_root, "remote.git");
        Git(path, "commit", "--allow-empty", "-m", "initial");
        Git(_root, "init", "--bare", remotePath);
        Git(path, "remote", "add", "origin", remotePath);
        Git(path, "push", "origin", "main:release");
        var oldHead = Git(remotePath, "rev-parse", "refs/heads/release");
        Git(path, "commit", "--allow-empty", "-m", "next");
        var newHead = Git(path, "rev-parse", "HEAD");

        SourceGit.Commands.Push Push(string source, string destination, bool force = false, bool track = false) =>
            new(path, source, "origin", destination, false, false, track, force, false);
        Check(!Wait(Push("main", "release").ExecAsync()), "Mismatched push without a confirmation window fails closed");
        Check(Git(remotePath, "rev-parse", "refs/heads/release") == oldHead, "Unconfirmed push does not change the remote");
        Check(Wait(Push("main", "refs/heads/main").RunAsync()), "Same branch with canonical remote ref pushes without a warning");
        Check(Git(remotePath, "rev-parse", "refs/heads/main") == newHead, "Same-name push updates the correct branch");

        var owner = new Window { Width = 1100, Height = 800 };
        _lifetime.MainWindow = owner;
        owner.Show();
        try
        {
            var push = Task.Run(() => Push("main", "release", track: true).RunAsync());
            var dialog = WaitForPushDialog(owner, push);
            Check(dialog.FindControl<SelectableTextBlock>("LocalBranch").Text == "main" &&
                dialog.FindControl<SelectableTextBlock>("RemoteBranch").Text == "release", "Warning displays the exact source and destination branches");
            Check(dialog.FindControl<Button>("BtnCancel").IsDefault && dialog.FindControl<Button>("BtnCancel").IsCancel,
                "Enter and Escape default to cancel");
            Check(Git(remotePath, "rev-parse", "refs/heads/release") == oldHead, "Remote remains unchanged while waiting for confirmation");
            dialog.FindControl<Button>("BtnCancel").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Check(!Wait(push), "Cancel prevents Commit Flow style RunAsync push");
            Check(Git(remotePath, "rev-parse", "refs/heads/release") == oldHead, "Cancel leaves remote branch unchanged");
            Check(string.IsNullOrEmpty(Wait(new SourceGit.Commands.Config(path).GetAsync("branch.main.merge"))),
                "Cancel does not change branch tracking configuration");

            push = Task.Run(() => Push("main", "release").RunAsync());
            dialog = WaitForPushDialog(owner, push);
            dialog.KeyPressQwerty(Avalonia.Input.PhysicalKey.Enter, Avalonia.Input.RawInputModifiers.None);
            Check(!Wait(push), "Pressing Enter on the default button cancels push");

            push = Task.Run(() => Push("main", "release").RunAsync());
            dialog = WaitForPushDialog(owner, push);
            dialog.KeyPressQwerty(Avalonia.Input.PhysicalKey.Escape, Avalonia.Input.RawInputModifiers.None);
            Check(!Wait(push), "Pressing Escape cancels push");
            Check(Git(remotePath, "rev-parse", "refs/heads/release") == oldHead, "Keyboard cancellation leaves the remote unchanged");

            push = Task.Run(() => Push("HEAD", "release").ExecAsync());
            dialog = WaitForPushDialog(owner, push);
            Check(dialog.FindControl<SelectableTextBlock>("LocalBranch").Text == "main", "HEAD source resolves to the actual local branch");
            dialog.Close();
            Check(!Wait(push), "Closing the warning cancels push");

            push = Task.Run(() => new SourceGit.Commands.Push(path,
                new Branch { Name = "main", IsLocal = true }, new Remote { Name = "origin" },
                new Branch { Name = "release" }, false, false, false, false, false).ExecAsync());
            dialog = WaitForPushDialog(owner, push);
            dialog.FindControl<Button>("BtnPush").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Check(Wait(push), "Only explicit confirmation starts regular typed push");
            Check(Git(remotePath, "rev-parse", "refs/heads/release") == newHead, "Confirmed push updates the displayed destination");

            push = Task.Run(() => Push("main", "MAIN", true).RunAsync());
            dialog = WaitForPushDialog(owner, push);
            Check(dialog.FindControl<TextBlock>("ForceWarning").IsVisible, "Force push receives the same prominent warning");
            Check(dialog.FindControl<SelectableTextBlock>("RemoteBranch").Text == "MAIN", "Branch-name comparison is case sensitive");
            dialog.Close(false);
            Check(!Wait(push), "Force push can be canceled before execution");

            using var cancellation = new CancellationTokenSource();
            var canceledPush = Push("refs/heads/main", "release");
            canceledPush.CancellationToken = cancellation.Token;
            push = Task.Run(() => canceledPush.ExecAsync());
            dialog = WaitForPushDialog(owner, push);
            cancellation.Cancel();
            Check(!Wait(push) && !dialog.IsVisible, "Cancellation closes the pending warning without pushing");

            Check(Wait(Push(newHead, "release").ExecAsync()), "Explicit SHA push used by undo is not mistaken for a branch mismatch");
            Git(path, "tag", "v-test");
            Check(Wait(new SourceGit.Commands.Push(path, "origin", "refs/tags/v-test", false).RunAsync()), "Tag push is unaffected");
            Check(Wait(new SourceGit.Commands.Push(path, "origin", "refs/tags/v-test", true).RunAsync()), "Tag deletion is unaffected");

            dialog = new SourceGit.Views.ConfirmBranchPush { Width = 420, Height = 540 };
            dialog.SetData(path, "feature/a-very-long-local-branch-with-similar-name-and-extra-details",
                "origin", "feature/a-very-long-remote-branch-with-similar-name-and-extra-details", true);
            var preview = dialog.ShowDialog<bool>(owner);
            PumpFor(200);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            using var frame = dialog.CaptureRenderedFrame();
            Check(frame != null, "Large push warning renders at a narrow width");
            frame.Save(Path.Combine(_root, "push-warning-preview.png"));
            Check(dialog.FindControl<SelectableTextBlock>("LocalBranch").Bounds.Width < dialog.Bounds.Width,
                "Long branch names are constrained to the warning width");
            dialog.Close(false);
            Wait(preview);

            SourceGit.App.SetLocale("zh_CN");
            dialog = new SourceGit.Views.ConfirmBranchPush();
            dialog.SetData(path, "feature/AW2020TE-13568/hapsim-home-somebugs-fix", "origin", "develop", false);
            preview = dialog.ShowDialog<bool>(owner);
            PumpFor(200);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            using var chineseFrame = dialog.CaptureRenderedFrame();
            Check(chineseFrame != null, "Chinese push warning renders at the default size");
            chineseFrame.Save(Path.Combine(_root, "push-warning-zh-preview.png"));
            dialog.Close(false);
            Wait(preview);
            SourceGit.App.SetLocale("en_US");
        }
        finally
        {
            _lifetime.MainWindow = null;
            owner.Close();
        }
    }

    private static SourceGit.Views.ConfirmBranchPush WaitForPushDialog(Window owner, Task push)
    {
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed.TotalSeconds < 10)
        {
            Dispatcher.UIThread.RunJobs();
            var dialog = owner.OwnedWindows.OfType<SourceGit.Views.ConfirmBranchPush>().SingleOrDefault();
            if (dialog != null)
            {
                PumpFor(50);
                return dialog;
            }
            if (push.IsCompleted)
            {
                Wait(push);
                throw new Exception("Push completed before showing its required warning.");
            }
            Thread.Sleep(5);
        }
        throw new TimeoutException("Push warning did not open.");
    }

    private static string NewRepository(string name)
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(path);
        Git(path, "init", "-b", "main");
        Git(path, "config", "user.name", "Regression");
        Git(path, "config", "user.email", "regression@example.com");
        return path;
    }

    private static Repo Open(string path)
    {
        var repo = new Repo(false, path, Path.Combine(path, ".git"));
        repo.Open();
        Wait(InvokeTask(repo, "RefreshBranchesAsync"));
        PumpFor(200);
        return repo;
    }

    private static string Git(string path, params string[] args)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = path, RedirectStandardOutput = true,
            RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var arg in args)
            start.ArgumentList.Add(arg);
        using var process = Process.Start(start);
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new Exception($"git {string.Join(' ', args)} failed: {stderr}");
        return stdout.Trim();
    }

    private static object Invoke(object instance, string name, params object[] args) => instance.GetType()
        .GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(instance, args);
    private static Task InvokeTask(object instance, string name) => (Task)Invoke(instance, name);
    private static void Wait(Task task)
    {
        var timer = Stopwatch.StartNew();
        while (!task.IsCompleted)
        {
            if (timer.Elapsed.TotalSeconds > 20)
                throw new TimeoutException("Regression operation timed out.");
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }
        task.GetAwaiter().GetResult();
    }
    private static T Wait<T>(Task<T> task) { Wait((Task)task); return task.Result; }
    private static void PumpFor(int milliseconds)
    {
        var timer = Stopwatch.StartNew();
        while (timer.ElapsedMilliseconds < milliseconds)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(5);
        }
    }
    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new Exception("FAIL: " + message);
        _checks++;
        Console.WriteLine("PASS: " + message);
    }
}

internal sealed class RegressionApp : SourceGit.App
{
    public override void OnFrameworkInitializationCompleted() { }
}
