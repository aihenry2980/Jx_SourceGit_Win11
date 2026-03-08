using System.Collections.Concurrent;

namespace SourceGit.Models
{
    public class User
    {
        public static readonly User Invalid = new User();

        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;

        public User()
        {
            // Only used by User.Invalid
        }

        public User(string data)
        {
            var parts = data.Split('±', 2);
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
            if (_caches.TryGetValue(data, out var existed))
                return existed;

            var created = _caches.GetOrAdd(data, key =>
            {
                var user = new User(key);
                _insertOrders.Enqueue(key);
                TrimCacheIfNeeded();
                return user;
            });

            return created;
        }

        public override string ToString()
        {
            return $"{Name} <{Email}>";
        }

        private static void TrimCacheIfNeeded()
        {
            while (_caches.Count > MAX_USER_CACHE_SIZE && _insertOrders.TryDequeue(out var oldest))
                _caches.TryRemove(oldest, out _);
        }

        private static ConcurrentDictionary<string, User> _caches = new ConcurrentDictionary<string, User>();
        private static ConcurrentQueue<string> _insertOrders = new ConcurrentQueue<string>();
        private const int MAX_USER_CACHE_SIZE = 20000;
        private readonly int _hash;
    }
}
