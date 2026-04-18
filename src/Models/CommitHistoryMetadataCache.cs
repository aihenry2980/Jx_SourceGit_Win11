using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace SourceGit.Models
{
    public class CommitHistoryMetadata
    {
        public int Version { get; set; } = 0;
        public int ChangedFileCount { get; set; } = 0;
        public bool HasSubmodulePointerChange { get; set; } = false;
        public int RegularFileChangeCount { get; set; } = 0;
        public int AddedFileChangeCount { get; set; } = 0;
        public int ModifiedFileChangeCount { get; set; } = 0;
        public int SubmodulePointerChangeCount { get; set; } = 0;
        public bool HasRenameOrCopyChange { get; set; } = false;
        public bool HasTypeChange { get; set; } = false;
    }

    public class CommitHistoryMetadataCacheData
    {
        public Dictionary<string, CommitHistoryMetadata> Entries { get; set; } = new(StringComparer.Ordinal);
    }

    public class CommitHistoryMetadataCache
    {
        public static CommitHistoryMetadataCache Load(string gitCommonDir)
        {
            var file = Path.Combine(gitCommonDir, "sourcegit.commit-history-cache");
            CommitHistoryMetadataCacheData data = null;

            if (File.Exists(file))
            {
                try
                {
                    using var stream = File.OpenRead(file);
                    data = JsonSerializer.Deserialize(stream, JsonCodeGen.Default.CommitHistoryMetadataCacheData);
                }
                catch
                {
                    data = null;
                }
            }

            data ??= new CommitHistoryMetadataCacheData();
            return new CommitHistoryMetadataCache(file, data);
        }

        public bool TryGet(string sha, out CommitHistoryMetadata metadata)
        {
            lock (_lock)
            {
                if (!_data.Entries.TryGetValue(sha, out metadata))
                    return false;

                return metadata.Version >= CURRENT_VERSION;
            }
        }

        public void UpdateRange(Dictionary<string, CommitHistoryMetadata> updates)
        {
            if (updates == null || updates.Count == 0)
                return;

            var changed = false;
            lock (_lock)
            {
                foreach (var (sha, metadata) in updates)
                {
                    if (string.IsNullOrWhiteSpace(sha) || metadata == null)
                        continue;

                    metadata.Version = CURRENT_VERSION;
                    if (_data.Entries.TryGetValue(sha, out var exists) &&
                        exists.Version == metadata.Version &&
                        exists.ChangedFileCount == metadata.ChangedFileCount &&
                        exists.HasSubmodulePointerChange == metadata.HasSubmodulePointerChange &&
                        exists.RegularFileChangeCount == metadata.RegularFileChangeCount &&
                        exists.AddedFileChangeCount == metadata.AddedFileChangeCount &&
                        exists.ModifiedFileChangeCount == metadata.ModifiedFileChangeCount &&
                        exists.SubmodulePointerChangeCount == metadata.SubmodulePointerChangeCount &&
                        exists.HasRenameOrCopyChange == metadata.HasRenameOrCopyChange &&
                        exists.HasTypeChange == metadata.HasTypeChange)
                        continue;

                    _data.Entries[sha] = metadata;
                    changed = true;
                }
            }

            if (changed)
                _ = SaveAsync();
        }

        private CommitHistoryMetadataCache(string file, CommitHistoryMetadataCacheData data)
        {
            _file = file;
            _data = data ?? new CommitHistoryMetadataCacheData();
            _contentHash = HashContent(JsonSerializer.Serialize(_data, JsonCodeGen.Default.CommitHistoryMetadataCacheData));
        }

        private async Task SaveAsync()
        {
            CommitHistoryMetadataCacheData snapshot;
            lock (_lock)
            {
                snapshot = new CommitHistoryMetadataCacheData()
                {
                    Entries = new Dictionary<string, CommitHistoryMetadata>(_data.Entries, StringComparer.Ordinal),
                };
            }

            var content = JsonSerializer.Serialize(snapshot, JsonCodeGen.Default.CommitHistoryMetadataCacheData);
            var hash = HashContent(content);
            if (hash.Equals(_contentHash, StringComparison.Ordinal))
                return;

            try
            {
                await File.WriteAllTextAsync(_file, content);
                _contentHash = hash;
            }
            catch
            {
                // Ignore cache save errors.
            }
        }

        private static string HashContent(string source)
        {
            var hash = MD5.HashData(Encoding.UTF8.GetBytes(source ?? string.Empty));
            var builder = new StringBuilder(hash.Length * 2);
            foreach (var c in hash)
                builder.Append(c.ToString("x2"));
            return builder.ToString();
        }

        private readonly string _file = string.Empty;
        private readonly object _lock = new();
        private readonly CommitHistoryMetadataCacheData _data = null;
        private string _contentHash = string.Empty;
        private const int CURRENT_VERSION = 3;
    }
}
