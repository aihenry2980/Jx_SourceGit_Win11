using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Collections;

namespace SourceGit.ViewModels
{
    public class AddToIgnore : Popup
    {
        [Required(ErrorMessage = "Ignore pattern is required!")]
        public string Pattern
        {
            get => _pattern;
            set => SetProperty(ref _pattern, value, true);
        }

        [Required(ErrorMessage = "Storage file is required!!!")]
        public Models.GitIgnoreFile StorageFile
        {
            get => _storageFile;
            set
            {
                if (SetProperty(ref _storageFile, value, true))
                {
                    if (_storageFile?.IsCustom == true)
                        CustomStorageFile = _storageFile.CustomPath;

                    OnPropertyChanged(nameof(IsCustomStorageSelected));
                }
            }
        }

        public AvaloniaList<Models.GitIgnoreFile> StorageFiles
        {
            get;
        } = [];

        public string CustomStorageFile
        {
            get => _customStorageFile;
            set
            {
                if (SetProperty(ref _customStorageFile, value))
                {
                    if (_storageFile?.IsCustom == true)
                        _storageFile.CustomPath = value ?? string.Empty;
                }
            }
        }

        public bool IsCustomStorageSelected => _storageFile?.IsCustom == true;

        public AddToIgnore(Repository repo, string pattern)
        {
            _repo = repo;
            _pattern = pattern;

            var customPath = repo.Settings.CustomGitIgnoreStorageFile ?? string.Empty;
            StorageFiles.Add(new Models.GitIgnoreFile(true));
            StorageFiles.Add(new Models.GitIgnoreFile(false));
            StorageFiles.Add(new Models.GitIgnoreFile(customPath));

            CustomStorageFile = customPath;
            var preferredKind = (Models.GitIgnoreFileKind)repo.Settings.PreferredGitIgnoreStorageKind;
            StorageFile = preferredKind switch
            {
                Models.GitIgnoreFileKind.Private => StorageFiles[1],
                Models.GitIgnoreFileKind.Custom => StorageFiles[2],
                _ => StorageFiles[0],
            };
        }

        public override async Task<bool> Sure()
        {
            using var lockWatcher = _repo.LockWatcher();
            ProgressDescription = "Adding Ignored File(s) ...";

            var file = StorageFile.GetFullPath(_repo.FullPath, _repo.GitDir);
            if (string.IsNullOrWhiteSpace(file))
            {
                ProgressDescription = "Custom ignore file path is empty.";
                return false;
            }

            var parent = Path.GetDirectoryName(file);
            if (!string.IsNullOrEmpty(parent) && !Directory.Exists(parent))
                Directory.CreateDirectory(parent);

            if (!File.Exists(file))
            {
                await File.WriteAllLinesAsync(file, [_pattern]);
            }
            else
            {
                var org = await File.ReadAllTextAsync(file);
                if (!org.EndsWith('\n'))
                    await File.AppendAllLinesAsync(file, ["", _pattern]);
                else
                    await File.AppendAllLinesAsync(file, [_pattern]);
            }

            _repo.Settings.PreferredGitIgnoreStorageKind = (int)StorageFile.Kind;
            if (StorageFile.IsCustom)
                _repo.Settings.CustomGitIgnoreStorageFile = CustomStorageFile?.Trim() ?? string.Empty;
            await _repo.Settings.SaveAsync();

            _repo.MarkWorkingCopyDirtyManually();
            return true;
        }

        private readonly Repository _repo;
        private string _pattern;
        private Models.GitIgnoreFile _storageFile;
        private string _customStorageFile = string.Empty;
    }
}
