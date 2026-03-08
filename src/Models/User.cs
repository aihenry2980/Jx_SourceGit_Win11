using System.Collections.Concurrent;
using System.Threading;

namespace SourceGit.Models
{
    public class User
    {
        private const int MAX_CACHE_SIZE = 4096;
        private const int TRIMMED_CACHE_SIZE = 3072;

        public static readonly User Invalid = new User();

        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;

        public User()
        {
            // Only used by User.Invalid
        }

        public User(string data)
        {
            var parts = data.Split('\u00B1', 2);
            if (parts.Length < 2)
                parts = [string.Empty, data];

            Name = parts[0];
            Email = parts[1].TrimStart('<').TrimEnd('>');
            _hash = data.GetHashCode();
        }

        public override bool Equals(object obj)
        {
            return obj is User other && Name == other.Name && Email == other.Email;
        }

        public override int GetHashCode()
        {
            return _hash;
        }

        public static User FindOrAdd(string data)
        {
            var created = false;
            var user = _caches.GetOrAdd(data, key =>
            {
                created = true;
                return new User(key);
            });

            if (created)
            {
                _cacheKeys.Enqueue(data);
                TryTrimCaches();
            }

            return user;
        }

        public override string ToString()
        {
            return $"{Name} <{Email}>";
        }

        private static ConcurrentDictionary<string, User> _caches = new ConcurrentDictionary<string, User>();
        private static ConcurrentQueue<string> _cacheKeys = new ConcurrentQueue<string>();
        private static int _isTrimming = 0;
        private readonly int _hash;

        private static void TryTrimCaches()
        {
            if (_caches.Count <= MAX_CACHE_SIZE || Interlocked.CompareExchange(ref _isTrimming, 1, 0) != 0)
                return;

            try
            {
                while (_caches.Count > TRIMMED_CACHE_SIZE && _cacheKeys.TryDequeue(out var oldest))
                    _caches.TryRemove(oldest, out _);
            }
            finally
            {
                Volatile.Write(ref _isTrimming, 0);
            }
        }
    }
}
