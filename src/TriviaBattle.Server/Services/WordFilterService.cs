using System.Text.RegularExpressions;

namespace TriviaBattle.Server.Services
{
    public record WordFilterEntry(string Word, bool WholeWordOnly);

    public interface IWordFilterService
    {
        Task<string?> CheckAsync(string input);
        IReadOnlyList<WordFilterEntry> GetAll();
        void AddWord(string word, bool wholeWordOnly);
        bool RemoveWord(string word);
    }

    /// <summary>
    /// File-backed word filter. Reads from bannedwords.txt at the content root.
    /// Format: one word per line. Prefix * = whole-word-only match.
    /// Lines starting with # are comments and are preserved on write.
    /// </summary>
    public class FileWordFilterService : IWordFilterService
    {
        private readonly string _filePath;
        private readonly ILogger<FileWordFilterService> _logger;
        private readonly ReaderWriterLockSlim _lock = new();

        // Cached parsed list; invalidated when file mtime changes
        private List<WordFilterEntry>? _cache;
        private DateTime _cacheFileMtime;

        private static readonly (Regex pattern, string replacement)[] LeetMap =
        [
            (new Regex(@"@",    RegexOptions.Compiled), "a"),
            (new Regex(@"3",    RegexOptions.Compiled), "e"),
            (new Regex(@"[1!]", RegexOptions.Compiled), "i"),
            (new Regex(@"0",    RegexOptions.Compiled), "o"),
            (new Regex(@"[\$5]",RegexOptions.Compiled), "s"),
            (new Regex(@"7",    RegexOptions.Compiled), "t"),
        ];

        private const string DefaultFileContent = @"# Trivia Battle Word Filter
# One word per line. Lines starting with # are comments.
# Prefix a word with * to match whole words only.
#   *ass  = blocks 'ass' as a standalone word, but NOT 'Cassandra' or 'class'
#   fuck  = blocks 'fuck' anywhere in the name
#
# Edit this file directly, or use the API to add/remove words.
# Changes take effect within 60 seconds automatically.

# -- Whole-word-only (short words that appear in legitimate names) --
*ass
*cock
*dick
*tit
*crap
*piss
*slag
*twat

# -- Substring match (unambiguous profanities) --
fuck
shit
bitch
bastard
cunt
whore
slut
fag
wank
asshole
arsehole
bullshit
douchebag
";

        public FileWordFilterService(IWebHostEnvironment env, ILogger<FileWordFilterService> logger)
        {
            _filePath = Path.Combine(env.ContentRootPath, "bannedwords.txt");
            _logger = logger;
            EnsureFileExists();
        }

        private void EnsureFileExists()
        {
            if (!File.Exists(_filePath))
            {
                File.WriteAllText(_filePath, DefaultFileContent);
                _logger.LogInformation("Created default bannedwords.txt at {Path}", _filePath);
            }
        }

        private static string NormalizeLeet(string input)
        {
            var result = input.ToLowerInvariant();
            foreach (var (pattern, replacement) in LeetMap)
                result = pattern.Replace(result, replacement);
            return result;
        }

        private List<WordFilterEntry> ParseFile()
        {
            var entries = new List<WordFilterEntry>();
            foreach (var line in File.ReadLines(_filePath))
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#')) continue;

                if (trimmed.StartsWith('*'))
                    entries.Add(new WordFilterEntry(trimmed[1..].ToLowerInvariant(), WholeWordOnly: true));
                else
                    entries.Add(new WordFilterEntry(trimmed.ToLowerInvariant(), WholeWordOnly: false));
            }
            return entries;
        }

        private List<WordFilterEntry> GetCached()
        {
            _lock.EnterReadLock();
            try
            {
                var mtime = File.GetLastWriteTimeUtc(_filePath);
                if (_cache is not null && mtime == _cacheFileMtime)
                    return _cache;
            }
            finally { _lock.ExitReadLock(); }

            _lock.EnterWriteLock();
            try
            {
                // Re-check after acquiring write lock
                var mtime = File.GetLastWriteTimeUtc(_filePath);
                if (_cache is null || mtime != _cacheFileMtime)
                {
                    _cache = ParseFile();
                    _cacheFileMtime = mtime;
                }
                return _cache;
            }
            finally { _lock.ExitWriteLock(); }
        }

        public Task<string?> CheckAsync(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return Task.FromResult<string?>(null);

            var normalized = NormalizeLeet(input);
            var entries = GetCached();

            foreach (var entry in entries)
            {
                bool matched = entry.WholeWordOnly
                    ? Regex.IsMatch(normalized, $@"\b{Regex.Escape(entry.Word)}\b")
                    : normalized.Contains(entry.Word, StringComparison.OrdinalIgnoreCase);

                if (matched) return Task.FromResult<string?>(entry.Word);
            }

            return Task.FromResult<string?>(null);
        }

        public IReadOnlyList<WordFilterEntry> GetAll() => GetCached().AsReadOnly();

        public void AddWord(string word, bool wholeWordOnly)
        {
            _lock.EnterWriteLock();
            try
            {
                var line = wholeWordOnly ? $"*{word}" : word;
                File.AppendAllText(_filePath, Environment.NewLine + line);
                _cache = null; // Invalidate
                _logger.LogInformation("Word filter: added '{Word}' (WholeWordOnly={WholeWordOnly})", word, wholeWordOnly);
            }
            finally { _lock.ExitWriteLock(); }
        }

        public bool RemoveWord(string word)
        {
            _lock.EnterWriteLock();
            try
            {
                var lines = File.ReadAllLines(_filePath);
                var target = word.ToLowerInvariant();

                var filtered = lines.Where(l =>
                {
                    var trimmed = l.Trim();
                    if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#')) return true;
                    var lineWord = trimmed.TrimStart('*').ToLowerInvariant();
                    return lineWord != target;
                }).ToArray();

                if (filtered.Length == lines.Length) return false;

                File.WriteAllLines(_filePath, filtered);
                _cache = null; // Invalidate
                _logger.LogInformation("Word filter: removed '{Word}'", word);
                return true;
            }
            finally { _lock.ExitWriteLock(); }
        }
    }
}
