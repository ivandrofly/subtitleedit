using BenchmarkDotNet.Attributes;
using Nikse.SubtitleEdit.Core.Common;

namespace Nikse.SubtitleEdit.Benchmarks;

/// <summary>
/// SpellCheckWordLists.IsWordInUserPhrases(word, text), live spell-check path: every phrase
/// IndexOf'ed over the line for every word ("Old", copied here) vs one AhoCorasick pass per line
/// and a scan of its matches per word ("New", same shape as the real method).
/// [GlobalSetup] throws when the two disagree.
/// </summary>
[MemoryDiagnoser]
public class UserPhraseInLineBenchmarks
{
    private readonly HashSet<string> _userPhraseList = new();
    private (string Text, (int Index, int Length)[] Words)[] _lines = Array.Empty<(string, (int, int)[])>();
    private AhoCorasick _matcher = null!;

    [Params(18, 500)]
    public int Phrases { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _userPhraseList.Add("it was the best");
        _userPhraseList.Add("then stay here");
        for (var i = _userPhraseList.Count; i < Phrases; i++)
        {
            _userPhraseList.Add("phrase" + i + " word" + i);
        }

        _matcher = new AhoCorasick(_userPhraseList, ignoreCase: true);
        _lines = BenchmarkSubtitles.Sentences.Select(text =>
        {
            var words = new List<(int, int)>();
            var start = -1;
            for (var i = 0; i <= text.Length; i++)
            {
                var isLetter = i < text.Length && char.IsLetter(text[i]);
                if (isLetter && start < 0)
                {
                    start = i;
                }
                else if (!isLetter && start >= 0)
                {
                    words.Add((start, i - start));
                    start = -1;
                }
            }

            return (text, words.ToArray());
        }).ToArray();

        if (Old() != New() || Old() == 0)
        {
            throw new InvalidOperationException("user phrase checks differ");
        }
    }

    [Benchmark(Baseline = true)]
    public int Old()
    {
        var hits = 0;
        foreach (var (text, words) in _lines)
        {
            foreach (var (index, length) in words)
            {
                if (OldIsWordInUserPhrases(index, length, text))
                {
                    hits++;
                }
            }
        }

        return hits;
    }

    [Benchmark]
    public int New()
    {
        var hits = 0;
        var matches = new List<AhoCorasickMatch>();
        foreach (var (text, words) in _lines)
        {
            // The real method caches this per line; every word of the line reuses it.
            matches.Clear();
            _matcher.FindAll(text, matches);
            foreach (var (index, length) in words)
            {
                foreach (var match in matches)
                {
                    if (index >= match.Index && index + length <= match.Index + match.Length)
                    {
                        hits++;
                        break;
                    }
                }
            }
        }

        return hits;
    }

    private bool OldIsWordInUserPhrases(int wordIndex, int wordLength, string text)
    {
        foreach (var userPhrase in _userPhraseList)
        {
            var start = text.IndexOf(userPhrase, StringComparison.OrdinalIgnoreCase);
            while (start >= 0)
            {
                if (wordIndex >= start && wordIndex + wordLength <= start + userPhrase.Length)
                {
                    return true;
                }

                if (start + 1 >= text.Length)
                {
                    break;
                }

                start = text.IndexOf(userPhrase, start + 1, StringComparison.OrdinalIgnoreCase);
            }
        }

        return false;
    }
}
