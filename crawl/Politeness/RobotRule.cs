using System.Text.RegularExpressions;

namespace crawl.Politeness {
    public class RobotRule(bool isAllowed, string pattern) {
        private readonly bool _isAllowed = isAllowed;
        private readonly int _length = pattern.Length;
        private readonly Regex _regex = BuildRegex(pattern);

        public bool IsAllowed {
            get { return _isAllowed; }
        }

        public int Length {
            get { return _length; } 
        }

        public bool Matches(string path) {
            return _regex.IsMatch(path);
        }

        private static Regex BuildRegex(string pattern) {
            var original = pattern.Trim();

            if (!original.StartsWith('/')) {
                original = "/" + original;
            }

            var escaped = Regex.Escape(original);
            escaped = escaped.Replace(@"\*", ".*");

            if (original.EndsWith('$')) {
                if (escaped.EndsWith(@"\$", StringComparison.Ordinal)) {
                    escaped = escaped[..^2];
                }

                return new Regex($"^{escaped}$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
            }

             return new Regex($"^{escaped}.*", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        }
    }
}
