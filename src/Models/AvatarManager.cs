using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

namespace SourceGit.Models
{
    public interface IAvatarHost
    {
        void OnAvatarResourceChanged(string email, Bitmap image);
    }

    public partial class AvatarManager
    {
        private const int AVATAR_WIDTH = 64;
        private const int MAX_CACHE_SIZE = 256;

        public static AvatarManager Instance
        {
            get
            {
                return _instance ??= new AvatarManager();
            }
        }

        private static AvatarManager _instance = null;

        [GeneratedRegex(@"^(?:(\d+)\+)?(.+?)@.+\.github\.com$")]
        private static partial Regex REG_GITHUB_USER_EMAIL();

        private readonly Lock _synclock = new();
        private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(2) };
        private string _storePath;
        private List<IAvatarHost> _avatars = new List<IAvatarHost>();
        private Dictionary<string, CacheEntry> _resources = new Dictionary<string, CacheEntry>();
        private LinkedList<string> _resourceLru = new LinkedList<string>();
        private HashSet<string> _requesting = new HashSet<string>();
        private HashSet<string> _defaultAvatars = new HashSet<string>();

        public void Start()
        {
            _storePath = Path.Combine(Native.OS.DataDir, "avatars");
            if (!Directory.Exists(_storePath))
                Directory.CreateDirectory(_storePath);

            LoadDefaultAvatar("noreply@github.com", "github.png");
            LoadDefaultAvatar("unrealbot@epicgames.com", "unreal.png");

            Task.Run(async () =>
            {
                while (true)
                {
                    var email = GetNextRequestingEmail();
                    if (email == null)
                    {
                        Thread.Sleep(100);
                        continue;
                    }

                    var md5 = GetEmailHash(email);
                    var url = GetAvatarUrl(email, md5);
                    var localFile = Path.Combine(_storePath, md5);
                    Bitmap img = null;

                    try
                    {
                        var rsp = await _httpClient.GetAsync(url).ConfigureAwait(false);
                        if (rsp.IsSuccessStatusCode)
                        {
                            await using (var stream = await rsp.Content.ReadAsStreamAsync().ConfigureAwait(false))
                            await using (var writer = File.Create(localFile))
                                await stream.CopyToAsync(writer).ConfigureAwait(false);

                            img = LoadBitmap(localFile);
                        }
                    }
                    catch
                    {
                        // ignored
                    }

                    lock (_synclock)
                    {
                        _requesting.Remove(email);
                    }

                    Dispatcher.UIThread.Post(() =>
                    {
                        SetResource(email, img);
                        NotifyResourceChanged(email, img);
                    });
                }

                // ReSharper disable once FunctionNeverReturns
            });
        }

        public void Subscribe(IAvatarHost host)
        {
            _avatars.Add(host);
        }

        public void Unsubscribe(IAvatarHost host)
        {
            _avatars.Remove(host);
        }

        public Bitmap Request(string email, bool forceRefetch)
        {
            if (string.IsNullOrWhiteSpace(email))
                return null;

            if (forceRefetch)
            {
                if (_defaultAvatars.Contains(email))
                    return null;

                RemoveResource(email);
                DeleteLocalAvatarFile(email);
                NotifyResourceChanged(email, null);
            }
            else
            {
                if (TryGetResource(email, out var cached))
                    return cached;

                var stored = LoadFromStore(email);
                if (stored != null)
                {
                    SetResource(email, stored);
                    return stored;
                }
            }

            lock (_synclock)
            {
                _requesting.Add(email);
            }

            return null;
        }

        public void SetFromLocal(string email, string file)
        {
            try
            {
                var image = LoadBitmap(file);
                if (image == null)
                    return;

                SetResource(email, image);

                lock (_synclock)
                {
                    _requesting.Remove(email);
                }

                File.Copy(file, GetLocalAvatarFile(email), true);
                NotifyResourceChanged(email, image);
            }
            catch
            {
                // ignore
            }
        }

        private void LoadDefaultAvatar(string key, string img)
        {
            using var icon = AssetLoader.Open(new Uri($"avares://SourceGit/Resources/Images/{img}", UriKind.RelativeOrAbsolute));
            SetResource(key, new Bitmap(icon), true);
            _defaultAvatars.Add(key);
        }

        private string GetEmailHash(string email)
        {
            var lowered = email.ToLower(CultureInfo.CurrentCulture).Trim();
            var hash = MD5.HashData(Encoding.Default.GetBytes(lowered));
            var builder = new StringBuilder(hash.Length * 2);
            foreach (var c in hash)
                builder.Append(c.ToString("x2"));
            return builder.ToString();
        }

        private string GetAvatarUrl(string email, string md5)
        {
            var matchGitHubUser = REG_GITHUB_USER_EMAIL().Match(email);
            if (!matchGitHubUser.Success)
                return $"https://www.gravatar.com/avatar/{md5}?d=404";

            var githubUser = matchGitHubUser.Groups[2].Value;
            if (githubUser.EndsWith("[bot]", StringComparison.OrdinalIgnoreCase))
                githubUser = githubUser.Substring(0, githubUser.Length - 5);

            return $"https://avatars.githubusercontent.com/{githubUser}";
        }

        private void NotifyResourceChanged(string email, Bitmap image)
        {
            foreach (var avatar in _avatars)
                avatar.OnAvatarResourceChanged(email, image);
        }

        private string GetNextRequestingEmail()
        {
            lock (_synclock)
            {
                foreach (var email in _requesting)
                    return email;
            }

            return null;
        }

        private string GetLocalAvatarFile(string email)
        {
            return Path.Combine(_storePath, GetEmailHash(email));
        }

        private Bitmap LoadFromStore(string email)
        {
            var localFile = GetLocalAvatarFile(email);
            if (!File.Exists(localFile))
                return null;

            try
            {
                return LoadBitmap(localFile);
            }
            catch
            {
                return null;
            }
        }

        private Bitmap LoadBitmap(string file)
        {
            using var stream = File.OpenRead(file);
            return Bitmap.DecodeToWidth(stream, AVATAR_WIDTH);
        }

        private bool TryGetResource(string email, out Bitmap value)
        {
            if (_resources.TryGetValue(email, out var entry))
            {
                TouchResource(entry);
                value = entry.Image;
                return true;
            }

            value = null;
            return false;
        }

        private void SetResource(string email, Bitmap image, bool isDefault = false)
        {
            if (_resources.TryGetValue(email, out var existing))
            {
                if (!ReferenceEquals(existing.Image, image) && !existing.IsDefault)
                    existing.Image?.Dispose();

                existing.Image = image;
                existing.IsDefault = isDefault;
                TouchResource(existing);
            }
            else
            {
                var entry = new CacheEntry
                {
                    Image = image,
                    IsDefault = isDefault,
                    LruNode = _resourceLru.AddFirst(email),
                };
                _resources[email] = entry;
            }

            TrimResources();
        }

        private void RemoveResource(string email)
        {
            if (!_resources.Remove(email, out var entry))
                return;

            if (entry.LruNode != null)
                _resourceLru.Remove(entry.LruNode);

            if (!entry.IsDefault)
                entry.Image?.Dispose();
        }

        private void TouchResource(CacheEntry entry)
        {
            if (entry?.LruNode == null || ReferenceEquals(_resourceLru.First, entry.LruNode))
                return;

            _resourceLru.Remove(entry.LruNode);
            _resourceLru.AddFirst(entry.LruNode);
        }

        private void TrimResources()
        {
            while (_resources.Count > MAX_CACHE_SIZE && _resourceLru.Last != null)
            {
                var key = _resourceLru.Last.Value;
                if (_defaultAvatars.Contains(key))
                {
                    _resourceLru.RemoveLast();
                    if (_resources.TryGetValue(key, out var pinned))
                        pinned.LruNode = _resourceLru.AddFirst(key);
                    continue;
                }

                RemoveResource(key);
            }
        }

        private void DeleteLocalAvatarFile(string email)
        {
            var localFile = GetLocalAvatarFile(email);
            if (File.Exists(localFile))
                File.Delete(localFile);
        }

        private class CacheEntry
        {
            public Bitmap Image { get; set; }
            public bool IsDefault { get; set; }
            public LinkedListNode<string> LruNode { get; set; }
        }
    }
}
