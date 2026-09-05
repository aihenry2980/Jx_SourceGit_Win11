using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SourceGit.ViewModels
{
    public class DiffContext : ObservableObject
    {
        private const long MAX_DIFF_IMAGE_SIZE = ImageSource.DEFAULT_DIFF_IMAGE_SIZE_LIMIT;

        public string Title
        {
            get;
        }

        public bool IgnoreWhitespace
        {
            get => Preferences.Instance.IgnoreWhitespaceChangesInDiff;
            set
            {
                if (value != Preferences.Instance.IgnoreWhitespaceChangesInDiff)
                {
                    Preferences.Instance.IgnoreWhitespaceChangesInDiff = value;
                    OnPropertyChanged();
                    LoadContent();
                }
            }
        }

        public bool ShowEntireFile
        {
            get => Preferences.Instance.UseFullTextDiff;
            set
            {
                if (value != Preferences.Instance.UseFullTextDiff)
                {
                    Preferences.Instance.UseFullTextDiff = value;
                    OnPropertyChanged();

                    if (Content is TextDiffContext)
                        LoadContent();
                }
            }
        }

        public bool UseSideBySide
        {
            get => Preferences.Instance.UseSideBySideDiff;
            set
            {
                if (value != Preferences.Instance.UseSideBySideDiff)
                {
                    Preferences.Instance.UseSideBySideDiff = value;
                    OnPropertyChanged();

                    if (Content is TextDiffContext ctx && ctx.IsSideBySide() != value)
                        Content = ctx.SwitchMode();
                }
            }
        }

        public int OldMode
        {
            get => _oldMode;
            private set => SetProperty(ref _oldMode, value);
        }

        public int NewMode
        {
            get => _newMode;
            private set => SetProperty(ref _newMode, value);
        }

        public bool IsTextDiff
        {
            get => _isTextDiff;
            private set => SetProperty(ref _isTextDiff, value);
        }

        public bool IsIgnoreWhitespaceVisible
        {
            get => _isIgnoreWhitespaceVisible;
            private set => SetProperty(ref _isIgnoreWhitespaceVisible, value);
        }

        public object Content
        {
            get => _content;
            private set => SetProperty(ref _content, value);
        }

        public int UnifiedLines
        {
            get => _unifiedLines;
            private set => SetProperty(ref _unifiedLines, value);
        }

        public DiffContext(string repo, Models.DiffOption option, DiffContext previous = null)
        {
            _repo = repo;
            _option = option;

            if (previous != null)
            {
                _isTextDiff = previous._isTextDiff;
                _isIgnoreWhitespaceVisible = previous._isIgnoreWhitespaceVisible;
                _content = previous._content;
                _oldMode = previous._oldMode;
                _newMode = previous._newMode;
                _unifiedLines = previous._unifiedLines;
                _info = previous._info;
            }

            if (string.IsNullOrEmpty(_option.OrgPath) || _option.OrgPath == "/dev/null")
                Title = _option.Path;
            else
                Title = $"{_option.OrgPath} -> {_option.Path}";

            LoadContent();
        }

        public void IncrUnified()
        {
            UnifiedLines = _unifiedLines + 1;
            LoadContent();
        }

        public void DecrUnified()
        {
            UnifiedLines = Math.Max(4, _unifiedLines - 1);
            LoadContent();
        }

        public void OpenExternalMergeTool()
        {
            new Commands.DiffTool(_repo, _option).Open();
        }

        public void CheckSettings()
        {
            if (Content is TextDiffContext ctx)
            {
                if ((ShowEntireFile && _info.UnifiedLines != _entireFileLine) ||
                    (!ShowEntireFile && _info.UnifiedLines == _entireFileLine) ||
                    (IgnoreWhitespace != _info.IgnoreWhitespace))
                {
                    LoadContent();
                    return;
                }

                if (ctx.IsSideBySide() != UseSideBySide)
                    Content = ctx.SwitchMode();
            }
        }

        public void CancelLoading()
        {
            Interlocked.Increment(ref _loadRequestVersion);
            Interlocked.Exchange(ref _loadCancellation, null)?.Cancel();
        }

        private void LoadContent()
        {
            CancelLoading();
            if (_option.Path.EndsWith('/'))
            {
                OldMode = 0;
                NewMode = 160000;
                IsTextDiff = false;
                IsIgnoreWhitespaceVisible = false;
                Content = null;
                _info = null;
                return;
            }

            var requestVersion = Volatile.Read(ref _loadRequestVersion);
            var cancellation = new CancellationTokenSource();
            _loadCancellation = cancellation;
            Task.Run(async () =>
            {
                try
                {
                    var numLines = Preferences.Instance.UseFullTextDiff ? _entireFileLine : _unifiedLines;
                    var ignoreWhitespace = Preferences.Instance.IgnoreWhitespaceChangesInDiff;
                    var ignoreCRAtEOL = Preferences.Instance.IgnoreCRAtEOLInDiff;
                    var latest = await new Commands.Diff(_repo, _option, numLines, ignoreWhitespace, ignoreCRAtEOL)
                    {
                        CancellationToken = cancellation.Token,
                    }.ReadAsync().ConfigureAwait(false);
                    if (!IsLatestRequest(requestVersion, cancellation.Token))
                        return;

                    var info = new Info(_option, numLines, ignoreWhitespace, latest);
                    if (_info != null && info.IsSame(_info))
                        return;

                    var rs = await BuildContentAsync(latest).ConfigureAwait(false);
                    if (!IsLatestRequest(requestVersion, cancellation.Token))
                        return;

                    Dispatcher.UIThread.Post(() =>
                    {
                        if (!IsLatestRequest(requestVersion, cancellation.Token))
                            return;

                        _info = info;
                        OldMode = latest.OldMode;
                        NewMode = latest.NewMode;

                        if (rs is Models.TextDiff cur)
                        {
                            IsTextDiff = true;
                            IsIgnoreWhitespaceVisible = true;

                            if (Preferences.Instance.UseSideBySideDiff)
                                Content = new TwoSideTextDiff(_option, cur, _content as TextDiffContext);
                            else
                                Content = new CombinedTextDiff(_option, cur, _content as TextDiffContext);
                        }
                        else
                        {
                            IsTextDiff = false;
                            IsIgnoreWhitespaceVisible = rs is Models.NoOrEOLChange;
                            Content = rs;
                        }
                    });
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                {
                    // A newer selection superseded this request.
                }
                finally
                {
                    Interlocked.CompareExchange(ref _loadCancellation, null, cancellation);
                }
            });
        }

        private bool IsLatestRequest(int requestVersion, CancellationToken cancellationToken)
        {
            return !cancellationToken.IsCancellationRequested && requestVersion == Volatile.Read(ref _loadRequestVersion);
        }

        private async Task<Models.ImageDiff> CreateImageDiffAsync(Models.ImageDecoder imgDecoder)
        {
            var oldPath = string.IsNullOrEmpty(_option.OrgPath) ? _option.Path : _option.OrgPath;
            var imgDiff = new Models.ImageDiff();
            var fullPath = Path.Combine(_repo, _option.Path);

            if (_option.Revisions.Count == 2) // Two revisions are specified, compare them
            {
                if (_option.Revisions[0].Equals("-R", StringComparison.Ordinal)) // `-R` means the old side is the working tree
                {
                    var oldImage = await ImageSource.FromFileAsync(fullPath, imgDecoder).ConfigureAwait(false);
                    imgDiff.Old = oldImage.Bitmap;
                    imgDiff.OldFileSize = oldImage.Size;
                }
                else
                {
                    var oldImage = await ImageSource.FromRevisionAsync(_repo, _option.Revisions[0], oldPath, imgDecoder).ConfigureAwait(false);
                    imgDiff.Old = oldImage.Bitmap;
                    imgDiff.OldFileSize = oldImage.Size;
                }

                if (string.IsNullOrEmpty(_option.Revisions[1])) // Empty string in the second revision means the new side is the working tree
                {
                    var newImage = await ImageSource.FromFileAsync(fullPath, imgDecoder).ConfigureAwait(false);
                    imgDiff.New = newImage.Bitmap;
                    imgDiff.NewFileSize = newImage.Size;
                }
                else
                {
                    var newImage = await ImageSource.FromRevisionAsync(_repo, _option.Revisions[1], _option.Path, imgDecoder).ConfigureAwait(false);
                    imgDiff.New = newImage.Bitmap;
                    imgDiff.NewFileSize = newImage.Size;
                }
            }
            else if (_option.IsUnstaged) // Unstaged change compared to staged or HEAD
            {
                if (!oldPath.Equals("/dev/null", StringComparison.Ordinal))
                {
                    var oldImage = await ImageSource.FromRevisionAsync(_repo, string.Empty, oldPath, imgDecoder).ConfigureAwait(false);
                    imgDiff.Old = oldImage.Bitmap;
                    imgDiff.OldFileSize = oldImage.Size;
                }

                var newImage = await ImageSource.FromFileAsync(fullPath, imgDecoder).ConfigureAwait(false);
                imgDiff.New = newImage.Bitmap;
                imgDiff.NewFileSize = newImage.Size;
            }
            else // Staged change compared to the last commit (HEAD)
            {
                var oldImage = await ImageSource.FromRevisionAsync(_repo, "HEAD", oldPath, imgDecoder).ConfigureAwait(false);
                imgDiff.Old = oldImage.Bitmap;
                imgDiff.OldFileSize = oldImage.Size;

                var newImage = await ImageSource.FromRevisionAsync(_repo, string.Empty, oldPath, imgDecoder).ConfigureAwait(false);
                imgDiff.New = newImage.Bitmap;
                imgDiff.NewFileSize = newImage.Size;
            }

            return imgDiff;
        }

        private async Task<Models.BinaryDiff> CreateBinaryDiffAsync()
        {
            var oldPath = string.IsNullOrEmpty(_option.OrgPath) ? _option.Path : _option.OrgPath;
            var binaryDiff = new Models.BinaryDiff();
            var fullPath = Path.Combine(_repo, _option.Path);

            binaryDiff.Repository = _repo;
            binaryDiff.FilePath = _option.Path;

            if (_option.Revisions.Count == 2) // Two revisions are specified, compare them
            {
                if (_option.Revisions[0].Equals("-R", StringComparison.Ordinal)) // `-R` means the old side is the working tree
                    binaryDiff.OldSize = File.Exists(fullPath) ? new FileInfo(fullPath).Length : 0;
                else
                    binaryDiff.OldSize = await new Commands.QueryFileSize(_repo, oldPath, _option.Revisions[0]).GetResultAsync().ConfigureAwait(false);

                if (string.IsNullOrEmpty(_option.Revisions[1])) // Empty string in the second revision means the new side is the working tree
                {
                    binaryDiff.NewSize = File.Exists(fullPath) ? new FileInfo(fullPath).Length : 0;
                    binaryDiff.NewRevision = null;
                }
                else
                {
                    binaryDiff.NewSize = await new Commands.QueryFileSize(_repo, _option.Path, _option.Revisions[1]).GetResultAsync().ConfigureAwait(false);
                    binaryDiff.NewRevision = _option.Revisions[1];
                }
            }
            else if (_option.IsUnstaged) // Unstaged change compared to staged or HEAD
            {
                if (!oldPath.Equals("/dev/null", StringComparison.Ordinal))
                    binaryDiff.OldSize = await new Commands.QueryFileSize(_repo, oldPath, string.Empty).GetResultAsync().ConfigureAwait(false);

                binaryDiff.NewSize = File.Exists(fullPath) ? new FileInfo(fullPath).Length : 0;
                binaryDiff.NewRevision = null;
            }
            else // Staged change compared to the last commit (HEAD)
            {
                binaryDiff.OldSize = await new Commands.QueryFileSize(_repo, oldPath, "HEAD").GetResultAsync().ConfigureAwait(false);
                binaryDiff.NewSize = await new Commands.QueryFileSize(_repo, _option.Path, string.Empty).GetResultAsync().ConfigureAwait(false);
                binaryDiff.NewRevision = string.Empty;
            }

            return binaryDiff;
        }

        private async Task<Models.SubmoduleDiff> CreateSubmoduleDiffAsync(string oldRevision, string newRevision)
        {
            var submoduleDiff = new Models.SubmoduleDiff();
            var submoduleRoot = $"{_repo}/{_option.Path}".Replace('\\', '/').TrimEnd('/');
            submoduleDiff.FullPath = submoduleRoot;
            submoduleDiff.RepositoryPath = submoduleRoot;

            if (IsValidSubmoduleHash(oldRevision))
                submoduleDiff.Old = await QuerySubmoduleRevisionAsync(submoduleRoot, oldRevision).ConfigureAwait(false);

            if (IsValidSubmoduleHash(newRevision))
                submoduleDiff.New = await QuerySubmoduleRevisionAsync(submoduleRoot, newRevision).ConfigureAwait(false);

            var oldSHA = submoduleDiff.Old?.Commit?.SHA;
            var newSHA = submoduleDiff.New?.Commit?.SHA;
            if (!string.IsNullOrWhiteSpace(oldSHA) || !string.IsNullOrWhiteSpace(newSHA))
            {
                var start = string.IsNullOrWhiteSpace(oldSHA) ? Models.Commit.EmptyTreeSHA1 : oldSHA;
                var end = string.IsNullOrWhiteSpace(newSHA) ? Models.Commit.EmptyTreeSHA1 : newSHA;
                submoduleDiff.BaseRevision = start;
                submoduleDiff.TargetRevision = end;

                var remotes = await new Commands.QueryRemotes(submoduleRoot).GetResultAsync().ConfigureAwait(false);
                var links = Models.CommitLink.Get(remotes);
                if (links.Count > 0)
                {
                    submoduleDiff.OldPointerURL = string.IsNullOrWhiteSpace(oldSHA) ? string.Empty : $"{links[0].URLPrefix}{oldSHA}";
                    submoduleDiff.NewPointerURL = string.IsNullOrWhiteSpace(newSHA) ? string.Empty : $"{links[0].URLPrefix}{newSHA}";
                }

                submoduleDiff.Changes = await new Commands.CompareRevisions(submoduleRoot, start, end).ReadAsync().ConfigureAwait(false);
                await Commands.QueryRevisionLineStats.ApplyAsync(submoduleRoot, start, end, submoduleDiff.Changes).ConfigureAwait(false);
            }

            return submoduleDiff;
        }

        private async Task<Models.RevisionSubmodule> QuerySubmoduleRevisionAsync(string repo, string sha)
        {
            if (!File.Exists(Path.Combine(repo, ".git")))
                return new Models.RevisionSubmodule() { Commit = new Models.Commit() { SHA = sha } };

            var revision = await new Commands.QuerySubmoduleRevision(repo, sha)
                .GetResultAsync()
                .ConfigureAwait(false);

            return revision ?? new Models.RevisionSubmodule() { Commit = new Models.Commit() { SHA = sha } };
        }

        private bool IsValidSubmoduleHash(string hash)
        {
            if (string.IsNullOrEmpty(hash))
                return false;

            for (int i = 0; i < hash.Length; i++)
            {
                if (hash[i] != '0')
                    return true;
            }

            return false;
        }

        private async Task<object> BuildContentAsync(Models.DiffResult latest)
        {
            if (latest.IsSubmoduleChange)
                return await CreateSubmoduleDiffAsync(latest.OldHash, latest.NewHash).ConfigureAwait(false);

            if (latest.TextDiff != null)
                return await BuildTextOrSubmoduleDiffAsync(latest.TextDiff).ConfigureAwait(false);

            if (latest.IsBinary)
                return await BuildBinaryOrImageDiffAsync().ConfigureAwait(false);

            if (latest.LFSDiff != null)
                return BuildLFSContent(latest.LFSDiff);

            if (IsEmptyFileHash(latest.OldHash) || IsEmptyFileHash(latest.NewHash))
                return new Models.EmptyFile();

            return new Models.NoOrEOLChange();
        }

        private async Task<object> BuildTextOrSubmoduleDiffAsync(Models.TextDiff textDiff)
        {
            var count = textDiff.Lines.Count;
            if (count < 2 || count > 3)
                return textDiff;

            var submoduleDiff = new Models.SubmoduleDiff();
            var submoduleRoot = $"{_repo}/{_option.Path}".Replace('\\', '/').TrimEnd('/');
            for (int i = 1; i < count; i++)
            {
                var line = textDiff.Lines[i];
                if (!line.Content.StartsWith("Subproject commit ", StringComparison.Ordinal))
                    return textDiff;

                var sha = line.Content.Substring(18);
                if (line.Type == Models.TextDiffLineType.Added)
                    submoduleDiff.New = await QuerySubmoduleRevisionAsync(submoduleRoot, sha).ConfigureAwait(false);
                else if (line.Type == Models.TextDiffLineType.Deleted)
                    submoduleDiff.Old = await QuerySubmoduleRevisionAsync(submoduleRoot, sha).ConfigureAwait(false);
            }

            var oldSHA = submoduleDiff.Old?.Commit?.SHA;
            var newSHA = submoduleDiff.New?.Commit?.SHA;
            if (!string.IsNullOrWhiteSpace(oldSHA) || !string.IsNullOrWhiteSpace(newSHA))
            {
                var start = string.IsNullOrWhiteSpace(oldSHA) ? Models.Commit.EmptyTreeSHA1 : oldSHA;
                var end = string.IsNullOrWhiteSpace(newSHA) ? Models.Commit.EmptyTreeSHA1 : newSHA;
                submoduleDiff.FullPath = submoduleRoot;
                submoduleDiff.RepositoryPath = submoduleRoot;
                submoduleDiff.BaseRevision = start;
                submoduleDiff.TargetRevision = end;
                var remotes = await new Commands.QueryRemotes(submoduleRoot).GetResultAsync().ConfigureAwait(false);
                var links = Models.CommitLink.Get(remotes);
                if (links.Count > 0)
                {
                    submoduleDiff.OldPointerURL = string.IsNullOrWhiteSpace(oldSHA) ? string.Empty : $"{links[0].URLPrefix}{oldSHA}";
                    submoduleDiff.NewPointerURL = string.IsNullOrWhiteSpace(newSHA) ? string.Empty : $"{links[0].URLPrefix}{newSHA}";
                }
                submoduleDiff.Changes = await new Commands.CompareRevisions(submoduleRoot, start, end).ReadAsync().ConfigureAwait(false);
                await Commands.QueryRevisionLineStats.ApplyAsync(submoduleRoot, start, end, submoduleDiff.Changes).ConfigureAwait(false);
            }

            return submoduleDiff;
        }

        private async Task<object> BuildBinaryOrImageDiffAsync()
        {
            var oldPath = string.IsNullOrEmpty(_option.OrgPath) ? _option.Path : _option.OrgPath;
            var imgDecoder = ImageSource.GetDecoder(_option.Path);
            var sizes = await QueryBinaryDiffSizesAsync(oldPath).ConfigureAwait(false);
            if (imgDecoder == Models.ImageDecoder.None ||
                sizes.OldSize > MAX_DIFF_IMAGE_SIZE ||
                sizes.NewSize > MAX_DIFF_IMAGE_SIZE)
            {
                return sizes;
            }

            var imgDiff = new Models.ImageDiff
            {
                OldFileSize = sizes.OldSize,
                NewFileSize = sizes.NewSize,
            };

            if (_option.Revisions.Count == 2)
            {
                if (!oldPath.Equals("/dev/null", StringComparison.Ordinal))
                    imgDiff.Old = (await ImageSource.FromRevisionAsync(_repo, _option.Revisions[0], oldPath, imgDecoder, MAX_DIFF_IMAGE_SIZE).ConfigureAwait(false)).Bitmap;

                if (!_option.Path.Equals("/dev/null", StringComparison.Ordinal))
                    imgDiff.New = (await ImageSource.FromRevisionAsync(_repo, _option.Revisions[1], _option.Path, imgDecoder, MAX_DIFF_IMAGE_SIZE).ConfigureAwait(false)).Bitmap;
            }
            else
            {
                if (!oldPath.Equals("/dev/null", StringComparison.Ordinal))
                    imgDiff.Old = (await ImageSource.FromRevisionAsync(_repo, "HEAD", oldPath, imgDecoder, MAX_DIFF_IMAGE_SIZE).ConfigureAwait(false)).Bitmap;

                var fullPath = Path.Combine(_repo, _option.Path);
                if (File.Exists(fullPath))
                    imgDiff.New = (await ImageSource.FromFileAsync(fullPath, imgDecoder, MAX_DIFF_IMAGE_SIZE).ConfigureAwait(false)).Bitmap;
            }

            return imgDiff;
        }

        private object BuildLFSContent(Models.LFSDiff lfsDiff)
        {
            var imgDecoder = ImageSource.GetDecoder(_option.Path);
            if (imgDecoder == Models.ImageDecoder.None ||
                lfsDiff.Old.Size > MAX_DIFF_IMAGE_SIZE ||
                lfsDiff.New.Size > MAX_DIFF_IMAGE_SIZE)
            {
                return lfsDiff;
            }

            return new LFSImageDiff(_repo, lfsDiff, imgDecoder, MAX_DIFF_IMAGE_SIZE);
        }

        private async Task<Models.BinaryDiff> QueryBinaryDiffSizesAsync(string oldPath)
        {
            var binaryDiff = new Models.BinaryDiff();
            if (_option.Revisions.Count == 2)
            {
                if (!oldPath.Equals("/dev/null", StringComparison.Ordinal))
                    binaryDiff.OldSize = await new Commands.QueryFileSize(_repo, oldPath, _option.Revisions[0]).GetResultAsync().ConfigureAwait(false);

                if (!_option.Path.Equals("/dev/null", StringComparison.Ordinal))
                    binaryDiff.NewSize = await new Commands.QueryFileSize(_repo, _option.Path, _option.Revisions[1]).GetResultAsync().ConfigureAwait(false);
            }
            else
            {
                if (!oldPath.Equals("/dev/null", StringComparison.Ordinal))
                    binaryDiff.OldSize = await new Commands.QueryFileSize(_repo, oldPath, "HEAD").GetResultAsync().ConfigureAwait(false);

                var fullPath = Path.Combine(_repo, _option.Path);
                binaryDiff.NewSize = File.Exists(fullPath) ? new FileInfo(fullPath).Length : 0;
            }

            return binaryDiff;
        }

        private bool IsEmptyFileHash(string hash)
        {
            if (string.IsNullOrEmpty(hash))
                return false;

            if (hash.Length == 40)
                return hash.Equals(Models.EmptyFile.SHA1, StringComparison.Ordinal);

            if (hash.Length == 64)
                return hash.Equals(Models.EmptyFile.SHA256, StringComparison.Ordinal);

            return false;
        }

        private class Info
        {
            public string Argument { get; }
            public int UnifiedLines { get; }
            public bool IgnoreWhitespace { get; }
            public string OldHash { get; }
            public string NewHash { get; }

            public Info(Models.DiffOption option, int unifiedLines, bool ignoreWhitespace, Models.DiffResult result)
            {
                Argument = option.ToString();
                UnifiedLines = unifiedLines;
                IgnoreWhitespace = ignoreWhitespace;
                OldHash = result.OldHash;
                NewHash = result.NewHash;
            }

            public bool IsSame(Info other)
            {
                return Argument.Equals(other.Argument, StringComparison.Ordinal) &&
                    UnifiedLines == other.UnifiedLines &&
                    IgnoreWhitespace == other.IgnoreWhitespace &&
                    OldHash.Equals(other.OldHash, StringComparison.Ordinal) &&
                    NewHash.Equals(other.NewHash, StringComparison.Ordinal);
            }
        }

        private readonly int _entireFileLine = 999999999;
        private readonly string _repo;
        private readonly Models.DiffOption _option = null;
        private int _loadRequestVersion = 0;
        private CancellationTokenSource _loadCancellation = null;
        private int _oldMode = 0;
        private int _newMode = 0;
        private int _unifiedLines = 4;
        private bool _isTextDiff = false;
        private bool _isIgnoreWhitespaceVisible = true;
        private object _content = null;
        private Info _info = null;
    }
}
