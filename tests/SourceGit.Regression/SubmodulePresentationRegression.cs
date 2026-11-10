using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using SourceGit.Models;
using SourceGit.ViewModels;

internal static partial class Program
{
    private static void TestSubmodulePresentation()
    {
        var source = NewRepository("branch label source");
        Git(source, "commit", "--allow-empty", "-m", "Initial");
        var root = NewRepository("branch label parent");
        Git(root, "-c", "protocol.file.allow=always", "submodule", "add", source, "packages/library");
        Git(root, "config", "-f", ".gitmodules", "submodule.packages/library.branch", "tracking-only");
        Git(root, "add", ".");
        Git(root, "commit", "-m", "Add submodule");
        var child = Path.Combine(root, "packages", "library");
        Git(child, "checkout", "-b", "feature/current");
        List<Submodule> Query(bool status) => Wait(new SourceGit.Commands.QuerySubmodules(root, 2, status).GetResultAsync());
        foreach (var status in new[] { false, true })
        {
            var module = Query(status).Single();
            Check(module.Branch == "tracking-only" && module.CheckedOutBranch == "feature/current",
                "Sidebar branch reports actual HEAD independently of tracking config and status scans: " + status);
        }

        var repo = Open(root);
        try
        {
            repo.RefreshSubmodules();
            WaitUntil(() => repo.Submodules.SingleOrDefault()?.CheckedOutBranch == "feature/current", "Initial sidebar branch did not load");
            Git(child, "checkout", "-b", "feature/same-commit");
            repo.RefreshSubmodules();
            WaitUntil(() => repo.Submodules.SingleOrDefault()?.CheckedOutBranch == "feature/same-commit", "Same-SHA branch change did not refresh");
            Check(repo.Submodules[0].CheckedOutBranchLabel == " (feature/same-commit)",
                "Switching branches at the same commit refreshes the sidebar label");
            Git(child, "checkout", "--detach", "HEAD");
            repo.RefreshSubmodules();
            WaitUntil(() => repo.Submodules.SingleOrDefault()?.CheckedOutBranch.Length == 0, "Detached branch label did not clear");
            Check(Query(false).Single().CheckedOutBranchLabel.Length == 0, "Detached submodules do not display their configured branch");
        }
        finally { repo.Close(); }

        Git(root, "config", "-f", ".gitmodules", "submodule.missing.path", "missing");
        Git(root, "config", "-f", ".gitmodules", "submodule.missing.url", source);
        Directory.CreateDirectory(Path.Combine(root, "missing"));
        Check(Query(false).Single(m => m.Path == "missing").CheckedOutBranch.Length == 0,
            "Uninitialized submodule does not inherit its parent repository's branch");

        // Older embedded repositories have a .git directory instead of a gitdir file.
        var embedded = Path.Combine(root, "embedded");
        Directory.CreateDirectory(embedded);
        Git(embedded, "init", "-b", "unborn");
        Git(root, "config", "-f", ".gitmodules", "submodule.embedded.path", "embedded");
        Git(root, "config", "-f", ".gitmodules", "submodule.embedded.url", source);
        Check(Query(false).Single(m => m.Path == "embedded").CheckedOutBranch == "unborn",
            "Embedded .git directories and unborn symbolic HEAD are supported");

        var modules = new List<Submodule>
        {
            new() { Path = "packages/library", CheckedOutBranch = "feature/current", AccentColor = 0xFF0EA5A4 },
            new() { Path = "packages/detached", Branch = "tracking-only", AccentColor = 0xFFF7630C },
        };
        var sidebar = new SourceGit.Views.SubmodulesView();
        var window = new Window { Content = sidebar, Width = 400, Height = 230 };
        window.Show();
        try
        {
            foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            foreach (var tree in new[] { true, false })
            {
                Application.Current.RequestedThemeVariant = theme;
                sidebar.Content = tree ? SubmoduleCollectionAsTree.Build(modules, null) : new SubmoduleCollectionAsList { Submodules = modules };
                PumpFor(100);
                var labels = sidebar.GetVisualDescendants().OfType<TextBlock>()
                    .SelectMany(t => t.Inlines?.OfType<Run>() ?? []).Where(r => r.Text == " (feature/current)").ToList();
                Check(labels.Count == 1 && labels[0].FontSize == 11 &&
                    Equals(labels[0].Foreground, sidebar.FindResource("Brush.FG1")),
                    $"Sidebar branch annotation is small and neutral: {theme}, tree={tree}");
                CaptureUxFrame(window, $"sidebar-branch-{theme}-{tree}");
            }
        }
        finally { window.Close(); }

        var diff = new SourceGit.Views.DiffView();
        var content = ((Grid)((Border)diff.Content).Child).Children.OfType<ContentControl>().Single(c => Grid.GetRow(c) == 1);
        content.Content = new SubmoduleDiff
        {
            Changes = [
                new() { Path = "added.txt", AddedLines = "12345", DeletedLines = "0" },
                new() { Path = "deleted.txt", AddedLines = "0", DeletedLines = "12345" },
                new() { Path = "image.png", AddedLines = "-", DeletedLines = "-" },
            ],
        };
        window = new Window { Content = diff, Width = 750, Height = 380 };
        window.Show();
        try
        {
            foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
            {
                Application.Current.RequestedThemeVariant = theme;
                PumpFor(100);
                var counts = diff.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Classes.Contains("submodule_line_count")).ToList();
                Check(counts.Count == 6 && counts.Count(t => t.Classes.Contains("zero")) == 2,
                    "Only numeric zero gets the neutral count style, not nonzero or binary markers");
                Check(counts.Where(t => t.Classes.Contains("zero")).All(t => Equals(t.Foreground, diff.FindResource("Brush.FG1"))),
                    "Zero counts use the theme's neutral text color: " + theme);
                Check(counts.Where(t => !t.Classes.Contains("zero")).All(t =>
                    ((ISolidColorBrush)t.Foreground).Color == (t.Classes.Contains("added") ? Colors.LimeGreen : Colors.Tomato)),
                    "Nonzero additions and deletions retain their semantic colors");
                Check(counts.All(t => t.FontSize == 16 && t.Bounds.Width >= 64 && t.TextLayout.Width <= t.Bounds.Width),
                    "Five-digit counts fit their wider columns without clipping");
                CaptureUxFrame(window, "submodule-line-counts-" + theme);
            }
        }
        finally
        {
            window.Close();
            Application.Current.RequestedThemeVariant = ThemeVariant.Light;
        }
    }
}
