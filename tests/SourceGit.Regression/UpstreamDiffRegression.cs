using System.Collections.Concurrent;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.VisualTree;
using SourceGit.Models;
using SourceGit.ViewModels;

internal static partial class Program
{
    private static void TestUpstreamDiffIntegration()
    {
        var path = NewRepository("upstream diff integration");
        File.WriteAllText(Path.Combine(path, "changed.txt"), "before\n");
        File.WriteAllText(Path.Combine(path, "deleted.txt"), "remove me\n");
        using (var bitmap = new WriteableBitmap(new PixelSize(2, 2), new Vector(96, 96), PixelFormat.Rgba8888, AlphaFormat.Opaque))
        {
            using (var buffer = bitmap.Lock())
                System.Runtime.InteropServices.Marshal.Copy(Enumerable.Repeat((byte)255, buffer.RowBytes * buffer.Size.Height).ToArray(), 0,
                    buffer.Address, buffer.RowBytes * buffer.Size.Height);
            bitmap.Save(Path.Combine(path, "before.png"));
        }
        Git(path, "add", ".");
        Git(path, "commit", "-m", "Initial diff fixture");
        File.WriteAllText(Path.Combine(path, "changed.txt"), "after\n");
        File.WriteAllText(Path.Combine(path, "added.txt"), "new content\n");
        Git(path, "rm", "deleted.txt");
        Git(path, "mv", "before.png", "after.png");
        using (var bitmap = new WriteableBitmap(new PixelSize(3, 3), new Vector(96, 96), PixelFormat.Rgba8888, AlphaFormat.Opaque))
        {
            using (var buffer = bitmap.Lock())
                System.Runtime.InteropServices.Marshal.Copy(new byte[buffer.RowBytes * buffer.Size.Height], 0,
                    buffer.Address, buffer.RowBytes * buffer.Size.Height);
            bitmap.Save(Path.Combine(path, "after.png"));
        }
        Git(path, "add", "changed.txt", "added.txt", "after.png");

        var pref = Preferences.Instance;
        var originalSideBySide = pref.UseSideBySideDiff;
        var contexts = new List<DiffContext>();
        DiffContext Load(string file, ChangeState state, string original = "")
        {
            var context = new DiffContext(path, new DiffOption(new Change { Path = file, OriginalPath = original, Index = state }, false));
            contexts.Add(context);
            WaitUntil(() => context.Content != null, "Diff content did not load for " + file);
            return context;
        }
        try
        {
            pref.UseSideBySideDiff = true;
            foreach (var (file, state) in new[] { ("added.txt", ChangeState.Added), ("deleted.txt", ChangeState.Deleted) })
            {
                var context = Load(file, state);
                Check(context.Content is CombinedTextDiff && ((TextDiffContext)context.Content).Data.IsNewOrDeletedFile,
                    "Added/deleted files use a single-sided unified diff despite the split-view preference: " + file);
                context.UseSideBySide = false;
                context.UseSideBySide = true;
                context.CheckSettings();
                Check(context.Content is CombinedTextDiff, "Changing diff settings does not reintroduce an empty split pane: " + file);
                var view = new SourceGit.Views.TextDiffView { DataContext = context.Content };
                var window = new Window { Content = view, Width = 800, Height = 280 };
                window.Show();
                try
                {
                    PumpFor(100);
                    var presenter = view.GetVisualDescendants().OfType<SourceGit.Views.CombinedTextDiffPresenter>().Single();
                    var added = state == ChangeState.Added;
                    Check(presenter.TextArea.LeftMargins[0].IsVisible == !added &&
                        presenter.TextArea.LeftMargins[1].IsVisible == !added &&
                        presenter.TextArea.LeftMargins[2].IsVisible == added &&
                        presenter.TextArea.LeftMargins[3].IsVisible == added,
                        "Only the relevant side's line numbers are shown: " + file);
                    CaptureUxFrame(window, "upstream-single-side-" + file);
                }
                finally { window.Close(); }
            }
            var changed = Load("changed.txt", ChangeState.Modified);
            Check(changed.Content is TwoSideTextDiff, "Ordinary modified files keep the split diff preference");
            changed.UseSideBySide = false;
            Check(changed.Content is CombinedTextDiff, "Customized diff mode control still switches ordinary changes");
            var image = Load("after.png", ChangeState.Renamed, "before.png");
            Check(image.Content is ImageDiff { Old: not null, New: not null },
                "Staged renamed images load both the old HEAD path and the new index path");
        }
        finally
        {
            foreach (var context in contexts)
                context.CancelLoading();
            pref.UseSideBySideDiff = originalSideBySide;
        }

        var messagePath = NewRepository("no commit message integration");
        File.WriteAllText(Path.Combine(messagePath, "file.txt"), "base\n");
        Git(messagePath, "add", ".");
        Git(messagePath, "commit", "-m", "Base");
        Git(messagePath, "checkout", "-b", "source");
        File.WriteAllText(Path.Combine(messagePath, "file.txt"), "picked\n");
        Git(messagePath, "commit", "-am", "Expected cherry-pick message");
        var picked = Git(messagePath, "rev-parse", "HEAD");
        Git(messagePath, "checkout", "main");
        var repo = Open(messagePath);
        try
        {
            Git(messagePath, "cherry-pick", "--no-commit", picked);
            repo.WorkingCopy.CommitMessage = string.Empty;
            Invoke(repo.WorkingCopy, "ScheduleUpdateInProgressState");
            WaitUntil(() => repo.WorkingCopy.CommitMessage.Contains("Expected cherry-pick message"), "No-commit message did not load.");
            Check(repo.WorkingCopy.InProgressContext == null && repo.WorkingCopy.CommitMessage.Contains("Expected cherry-pick message"),
                "Successful no-commit cherry-pick loads MERGE_MSG even without CHERRY_PICK_HEAD");
            Git(messagePath, "commit", "-m", "Apply picked content");
            Git(messagePath, "revert", "--no-commit", "HEAD");
            repo.WorkingCopy.CommitMessage = string.Empty;
            Invoke(repo.WorkingCopy, "ScheduleUpdateInProgressState");
            WaitUntil(() => repo.WorkingCopy.CommitMessage.Contains("Revert"), "No-commit revert message did not load.");
            Check(repo.WorkingCopy.CommitMessage.Contains("Apply picked content"), "No-commit revert also restores its generated commit message");
        }
        finally { repo.Close(); }

        var cacheKey = "Merge Cache User±merge-cache@example.invalid";
        Check(ReferenceEquals(User.FindOrAdd(cacheKey), User.FindOrAdd(cacheKey)), "Static user factory preserves cache hits");
        for (var i = 0; i < 5000; i++)
            User.FindOrAdd($"Merge User {i}±merge-user-{i}@example.invalid");
        var cache = (ConcurrentDictionary<string, User>)typeof(User).GetField("_caches", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).GetValue(null);
        Check(cache.Count <= 4096, "Upstream static user factory retains the customized cache size limit");
    }
}
