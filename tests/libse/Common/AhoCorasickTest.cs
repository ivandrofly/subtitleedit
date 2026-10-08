using Nikse.SubtitleEdit.Core.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace LibSETests.Common;

public class AhoCorasickTest
{
    private static List<(int Index, string Pattern)> FindAll(AhoCorasick matcher, string text)
    {
        var matches = new List<AhoCorasickMatch>();
        matcher.FindAll(text, matches);
        return matches.Select(m => (m.Index, matcher.GetPattern(m.PatternIndex))).ToList();
    }

    private static List<(int Index, string Pattern)> NaiveFindAll(IEnumerable<string> patterns, string text, StringComparison comparison)
    {
        var result = new List<(int, string)>();
        foreach (var pattern in patterns.Where(p => p.Length > 0).Distinct(StringComparer.FromComparison(comparison)))
        {
            var start = text.IndexOf(pattern, comparison);
            while (start >= 0)
            {
                result.Add((start, pattern));
                start = start + 1 >= text.Length ? -1 : text.IndexOf(pattern, start + 1, comparison);
            }
        }

        return result;
    }

    [Fact]
    public void FindAll_ClassicExample()
    {
        var matcher = new AhoCorasick(new[] { "he", "she", "his", "hers" }, ignoreCase: false);

        var matches = FindAll(matcher, "ushers");

        Assert.Equal(new[] { (1, "she"), (2, "he"), (2, "hers") }, matches);
    }

    [Fact]
    public void FindAll_PatternInsideAnotherPattern()
    {
        var matcher = new AhoCorasick(new[] { "New York", "York" }, ignoreCase: false);

        var matches = FindAll(matcher, "I love New York!");

        Assert.Equal(new[] { (7, "New York"), (11, "York") }, matches);
    }

    [Fact]
    public void FindAll_OverlappingOccurrences()
    {
        var matcher = new AhoCorasick(new[] { "aa" }, ignoreCase: false);

        var matches = FindAll(matcher, "aaaa");

        Assert.Equal(new[] { (0, "aa"), (1, "aa"), (2, "aa") }, matches);
    }

    [Fact]
    public void FindAll_CaseSensitive_DoesNotMatchOtherCase()
    {
        var matcher = new AhoCorasick(new[] { "Hello" }, ignoreCase: false);

        Assert.Empty(FindAll(matcher, "hello HELLO"));
        Assert.False(matcher.ContainsAny("hello HELLO"));
    }

    [Fact]
    public void FindAll_IgnoreCase_NonAscii()
    {
        var matcher = new AhoCorasick(new[] { "ÆBLE" }, ignoreCase: true);

        var matches = FindAll(matcher, "et æble og et Æble");

        Assert.Equal(new[] { (3, "ÆBLE"), (14, "ÆBLE") }, matches);
    }

    [Fact]
    public void Constructor_SkipsEmptyAndDuplicatePatterns()
    {
        var matcher = new AhoCorasick(new[] { "", "abc", "ABC", "abc", null }, ignoreCase: true);

        Assert.Equal(1, matcher.PatternCount);
        Assert.Equal("abc", matcher.GetPattern(0));
    }

    [Fact]
    public void FindAll_EmptyTextOrNoPatterns_FindsNothing()
    {
        Assert.Empty(FindAll(new AhoCorasick(new[] { "a" }, ignoreCase: false), string.Empty));
        Assert.Empty(FindAll(new AhoCorasick(Array.Empty<string>(), ignoreCase: false), "abc"));
        Assert.False(new AhoCorasick(Array.Empty<string>(), ignoreCase: false).ContainsAny("abc"));
    }

    [Fact]
    public void ContainsAny_PatternAtEnd()
    {
        var matcher = new AhoCorasick(new[] { "xyz", "end" }, ignoreCase: true);

        Assert.True(matcher.ContainsAny("this is the END"));
        Assert.False(matcher.ContainsAny("this is the en"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FindAll_Random_SameAsIndexOfLoop(bool ignoreCase)
    {
        var random = new Random(15825);
        const string alphabet = "abAB c";
        var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        for (var round = 0; round < 300; round++)
        {
            var patterns = Enumerable.Range(0, random.Next(1, 12))
                .Select(_ => RandomString(random, alphabet, random.Next(0, 5)))
                .ToList();
            var text = RandomString(random, alphabet, random.Next(0, 40));
            var matcher = new AhoCorasick(patterns, ignoreCase);

            var expected = NaiveFindAll(patterns, text, comparison).OrderBy(m => m.Index).ThenBy(m => m.Pattern, StringComparer.Ordinal).ToList();
            var actual = FindAll(matcher, text).OrderBy(m => m.Index).ThenBy(m => m.Pattern, StringComparer.Ordinal).ToList();

            Assert.Equal(expected, actual);
            Assert.Equal(expected.Count > 0, matcher.ContainsAny(text));
        }
    }

    private static string RandomString(Random random, string alphabet, int length)
    {
        var chars = new char[length];
        for (var i = 0; i < length; i++)
        {
            chars[i] = alphabet[random.Next(alphabet.Length)];
        }

        return new string(chars);
    }
}
