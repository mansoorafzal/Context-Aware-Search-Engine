namespace crawl.Politeness {
    public class Robot {
        private readonly List<RobotRule> _rules;

        private Robot(List<RobotRule> rules) {
            _rules = rules;
        }

        public static Robot Parse(string content) {
            var rules = new List<RobotRule>();
            var rows = content.Split('\n');
            bool asterikUserAgent = false;

            foreach (var row in rows) {
                var line = row.Trim();

                if (string.IsNullOrEmpty(line) || line.StartsWith('#')) {
                    continue;
                }

                var index = line.IndexOf(':');

                if (index <= 0) {
                    continue;
                }

                var directive = line[..index].Trim().ToLowerInvariant();
                var value = line[(index + 1)..].Trim();

                switch (directive) {
                    case "user-agent":
                        asterikUserAgent = value == "*" || value == "\"*\"";
                        break;

                    case "allow":
                        if (asterikUserAgent && !string.IsNullOrEmpty(value))
                            rules.Add(new RobotRule(isAllowed: true, pattern: value));
                        break;

                    case "disallow":
                        if (asterikUserAgent && !string.IsNullOrEmpty(value))
                            rules.Add(new RobotRule(isAllowed: false, pattern: value));
                        break;
                }
            }

            return new Robot(rules);
        }

        public bool IsAllowed(Uri uri) {
            var path = uri.PathAndQuery;
            
            if (!path.StartsWith('/')) {
                path = "/" + path;
            }

            RobotRule? robotRule = null;
            int length = -1;

            foreach (var rule in _rules) {
                if (!rule.Matches(path)) {
                    continue;
                }

                if (rule.Length > length) {
                    robotRule = rule;
                    length = rule.Length;
                }
            }

            return robotRule?.IsAllowed ?? true;
        }
    }
}
