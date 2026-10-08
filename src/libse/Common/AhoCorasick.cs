using System.Collections.Generic;

namespace Nikse.SubtitleEdit.Core.Common
{
    /// <summary>
    /// One occurrence of a pattern found by <see cref="AhoCorasick"/>.
    /// </summary>
    public readonly struct AhoCorasickMatch
    {
        /// <summary>Index in the text where the occurrence starts.</summary>
        public int Index { get; }

        /// <summary>Length of the occurrence (the pattern length).</summary>
        public int Length { get; }

        /// <summary>Index of the matched pattern, see <see cref="AhoCorasick.GetPattern"/>.</summary>
        public int PatternIndex { get; }

        /// <summary>Creates a match.</summary>
        public AhoCorasickMatch(int index, int length, int patternIndex)
        {
            Index = index;
            Length = length;
            PatternIndex = patternIndex;
        }
    }

    /// <summary>
    /// Finds all occurrences of a fixed set of patterns in a text in a single pass
    /// (Aho-Corasick automaton): the cost is O(text length + number of matches), independent of
    /// how many patterns there are - where a loop of <c>text.IndexOf(pattern)</c> scans the text
    /// once per pattern.
    ///
    /// Occurrences may overlap, and every one is reported - the same set a loop of
    /// <c>IndexOf(pattern, start + 1)</c> per pattern finds.
    ///
    /// With ignoreCase, patterns and text are compared one char at a time through
    /// <see cref="char.ToUpperInvariant"/>, which is how <c>StringComparison.OrdinalIgnoreCase</c>
    /// compares chars outside surrogate pairs.
    /// </summary>
    public sealed class AhoCorasick
    {
        private const int Root = 0;
        private const int None = -1;

        private readonly bool _ignoreCase;
        private readonly string[] _patterns;

        // All trie edges in one table, keyed by (state << 16) | char - far less memory than a
        // dictionary per node for lists with thousands of patterns.
        private readonly Dictionary<long, int> _edges;
        private readonly int[] _fail;
        private readonly int[] _nodePattern;   // pattern ending at this node, or None
        private readonly int[] _dictionaryLink; // nearest node on the fail chain where a pattern ends, or None

        /// <summary>
        /// Builds the automaton. Empty patterns are skipped, and so are duplicates (after case
        /// folding, when <paramref name="ignoreCase"/> is set) - the first one is kept.
        /// </summary>
        public AhoCorasick(IEnumerable<string> patterns, bool ignoreCase)
        {
            _ignoreCase = ignoreCase;
            _edges = new Dictionary<long, int>();

            var patternList = new List<string>();
            var nodePattern = new List<int> { None };
            var depth = new List<int> { 0 };
            foreach (var pattern in patterns)
            {
                if (string.IsNullOrEmpty(pattern))
                {
                    continue;
                }

                var state = Root;
                foreach (var ch in pattern)
                {
                    var key = Key(state, Fold(ch));
                    if (!_edges.TryGetValue(key, out var next))
                    {
                        next = nodePattern.Count;
                        nodePattern.Add(None);
                        depth.Add(depth[state] + 1);
                        _edges.Add(key, next);
                    }

                    state = next;
                }

                if (nodePattern[state] == None)
                {
                    nodePattern[state] = patternList.Count;
                    patternList.Add(pattern);
                }
            }

            _patterns = patternList.ToArray();
            _nodePattern = nodePattern.ToArray();
            _fail = new int[_nodePattern.Length];
            _dictionaryLink = new int[_nodePattern.Length];
            BuildLinks(depth);
        }

        /// <summary>Number of distinct, non-empty patterns.</summary>
        public int PatternCount => _patterns.Length;

        /// <summary>The pattern a match's <see cref="AhoCorasickMatch.PatternIndex"/> refers to.</summary>
        public string GetPattern(int index) => _patterns[index];

        /// <summary>
        /// Adds every occurrence of every pattern in <paramref name="text"/> to
        /// <paramref name="matches"/>, ordered by end position (then longest first).
        /// </summary>
        public void FindAll(string text, List<AhoCorasickMatch> matches)
        {
            if (string.IsNullOrEmpty(text) || _patterns.Length == 0)
            {
                return;
            }

            var state = Root;
            for (var i = 0; i < text.Length; i++)
            {
                state = Step(state, Fold(text[i]));
                var node = _nodePattern[state] != None ? state : _dictionaryLink[state];
                while (node != None)
                {
                    var patternIndex = _nodePattern[node];
                    var length = _patterns[patternIndex].Length;
                    matches.Add(new AhoCorasickMatch(i - length + 1, length, patternIndex));
                    node = _dictionaryLink[node];
                }
            }
        }

        /// <summary>True if any pattern occurs in <paramref name="text"/>.</summary>
        public bool ContainsAny(string text)
        {
            if (string.IsNullOrEmpty(text) || _patterns.Length == 0)
            {
                return false;
            }

            var state = Root;
            for (var i = 0; i < text.Length; i++)
            {
                state = Step(state, Fold(text[i]));
                if (_nodePattern[state] != None || _dictionaryLink[state] != None)
                {
                    return true;
                }
            }

            return false;
        }

        private static long Key(int state, char ch) => ((long)state << 16) | ch;

        private char Fold(char ch) => _ignoreCase ? char.ToUpperInvariant(ch) : ch;

        private int Step(int state, char ch)
        {
            while (true)
            {
                if (_edges.TryGetValue(Key(state, ch), out var next))
                {
                    return next;
                }

                if (state == Root)
                {
                    return Root;
                }

                state = _fail[state];
            }
        }

        private void BuildLinks(List<int> depth)
        {
            _fail[Root] = Root;
            _dictionaryLink[Root] = None;

            // A node's fail link is a strictly shallower node, so processing nodes by depth
            // (breadth-first) guarantees the parent's links are final before the child's.
            var byDepth = new List<KeyValuePair<long, int>>(_edges);
            byDepth.Sort((a, b) => depth[a.Value].CompareTo(depth[b.Value]));
            foreach (var edge in byDepth)
            {
                var parent = (int)(edge.Key >> 16);
                var ch = (char)(edge.Key & 0xFFFF);
                var child = edge.Value;

                _fail[child] = parent == Root ? Root : Step(_fail[parent], ch);

                var fail = _fail[child];
                _dictionaryLink[child] = _nodePattern[fail] != None ? fail : _dictionaryLink[fail];
            }
        }
    }
}
