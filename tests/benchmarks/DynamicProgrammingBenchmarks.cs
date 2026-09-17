using BenchmarkDotNet.Attributes;
using Nikse.SubtitleEdit.Core.Common;
using Nikse.SubtitleEdit.Core.Dictionaries;
using Nikse.SubtitleEdit.Core.SubtitleFormats;

namespace Nikse.SubtitleEdit.Benchmarks;

/// <summary>
/// The four libse paths moved from greedy / brute force to dynamic programming: the OCR word
/// splitter, the 3+ line auto-break, the N-way text split and merge short lines. Public entry
/// points only, so the same file runs on the commit before and after;
/// <see cref="SubRipControl"/> is the drift control.
///
/// Default job, i7-13700, .NET 10, a machine that was not quiet (SubRipControl 56.1 us -> 52.1 us,
/// identical allocations):
///
///   AutoBreak 3+ lines, word longer than max   29,970 us -> 6.6 us    4,500x  65.6 MB -> 13.6 KB
///   SplitWord, unsplittable (16 words)           8,536 us -> 4.2 us    2,000x  14 KB -> 7.9 KB
///   SplitWord, merged words (16 words)           4,719 us -> 10.5 us     450x  same allocations
///   AutoBreak 3+ lines, max 37 (5 lines)           125 us -> 53 us       2.3x  187 KB -> 90 KB
///   AutoBreak 3+ lines, max 20 (5 lines)            88 us -> 55 us       1.6x  194 KB -> 102 KB
///   MergeShortLines (2000 lines)                12,508 us -> 12,823 us   same  (language detect + auto-break dominate)
///
/// Slower, bought for the better split - the partition is quadratic in the words when there
/// is no maximum length to cut the search short, and these texts are 60-150 words:
///
///   SplitToThree (5 lines)                         4.7 us -> 12.2 us     2.6x slower
///   SplitMulti (20 texts, 3-6 parts)               580 us -> 2,609 us    4.5x slower  1.35 MB -> 410 KB
///   SplitText (20 texts, 3-6 parts)                5.0 us -> 2,460 us    ~125 us per text; 29 KB -> 242 KB
/// </summary>
[MemoryDiagnoser]
public class DynamicProgrammingBenchmarks
{
    private string[] _wordSplitList = Array.Empty<string>();
    private string[] _mergedWords = Array.Empty<string>();
    private string[] _unsplittableWords = Array.Empty<string>();
    private string[] _longLines = Array.Empty<string>();
    private string _lineWithLongWord = string.Empty;
    private string[] _paragraphTexts = Array.Empty<string>();
    private PlainTextImporter _importer = null!;
    private Subtitle _shortLines = new();
    private Subtitle _controlSubtitle = new();

    [GlobalSetup]
    public void Setup()
    {
        Configuration.Settings.Tools.OcrUseWordSplitList = true;
        Configuration.Settings.Tools.UseNoLineBreakAfter = false;

        var dictionaries = FindDictionariesFolder();
        var names = new NameList(dictionaries, "en", false, string.Empty).GetAllNames();
        _wordSplitList = StringWithoutSpaceSplitToWords.LoadWordSplitList(dictionaries, "eng", names);

        // what OCR hands over: two or three words run together, and unknown words that are not
        _mergedWords = new[]
        {
            "thequick", "overthere", "whatisthis", "comewithme", "nothingelse", "goodmorning", "areyousure",
            "iknowthat", "somethinghappened", "neveragain", "lookatthat", "yesterdaymorning", "wherewereyou",
            "thankyouverymuch", "thisisimportant", "rightbehindyou",
        };
        _unsplittableWords = new[]
        {
            "xqzvkrtw", "bvnmqpzx", "wrtplkjh", "zzxxccvv", "qwrtypsd", "mnbvcxzl", "plkmjnhb", "ghfdtrsw",
            "thequickxq", "overtherezv", "whatisthiskq", "nothingelsezx", "goodmorningqj", "areyousurevk",
            "somethingxqz", "yesterdayzvq",
        };

        _longLines = new[]
        {
            "The quick brown fox jumps over the lazy dog while the farmer sleeps quietly under the old oak tree near the river bank.",
            "We hold these truths to be self-evident, that all men are created equal, that they are endowed by their Creator with certain unalienable Rights.",
            "I told you yesterday. You never listen to me! Why would today be any different from all the other days we have spent together?",
            "<i>The quick brown fox jumps over the lazy dog while the farmer sleeps quietly</i> under the old oak tree near the river bank.",
            "It was the best of times, it was the worst of times, it was the age of wisdom, it was the age of foolishness, it was the epoch of belief, it was the epoch of incredulity.",
        };
        _lineWithLongWord = "Supercalifragilisticexpialidocious is a word that Mr. Banks never wanted to hear in his house again, not even once, not ever.";

        // a translated sentence group to deal back out over its paragraphs
        _paragraphTexts = Enumerable.Range(0, 20).Select(i => string.Join(" ", Enumerable.Range(0, 3 + i % 4).Select(j => _longLines[(i + j) % _longLines.Length].Replace("<i>", string.Empty).Replace("</i>", string.Empty)))).ToArray();

        _importer = new PlainTextImporter(false, false, 1, ".?!", 43, "en");

        _shortLines = new Subtitle();
        var fragments = new[] { "and then we went,", "over to the old house,", "where nobody had been", "for a very long time.", "It was dark,", "and cold," };
        for (var i = 0; i < 2000; i++)
        {
            _shortLines.Paragraphs.Add(new Paragraph(fragments[i % fragments.Length], i * 1000, i * 1000 + 900));
        }

        _controlSubtitle = new Subtitle();
        for (var i = 0; i < 400; i++)
        {
            _controlSubtitle.Paragraphs.Add(new Paragraph($"<i>The quick brown fox number {i}</i>{Environment.NewLine}jumps over the lazy dog.", i * 3000, i * 3000 + 2500));
        }
    }

    private static string FindDictionariesFolder()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "Dictionaries");
            if (File.Exists(Path.Combine(candidate, "names.xml")))
            {
                return candidate + Path.DirectorySeparatorChar;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Dictionaries/names.xml not found above " + AppContext.BaseDirectory);
    }

    // 1. OCR word splitter, real eng_WordSplitList + names

    [Benchmark]
    public int SplitWord_MergedWords()
    {
        var splits = 0;
        foreach (var word in _mergedWords)
        {
            if (StringWithoutSpaceSplitToWords.SplitWord(_wordSplitList, word, "eng") != word)
            {
                splits++;
            }
        }

        return splits;
    }

    [Benchmark]
    public int SplitWord_UnsplittableWords()
    {
        var splits = 0;
        foreach (var word in _unsplittableWords)
        {
            if (StringWithoutSpaceSplitToWords.SplitWord(_wordSplitList, word, "eng") != word)
            {
                splits++;
            }
        }

        return splits;
    }

    // 2. 3+ line auto-break

    [Benchmark]
    public int AutoBreakMoreThanTwoLines_Max37()
    {
        var length = 0;
        foreach (var line in _longLines)
        {
            length += Utilities.AutoBreakLineMoreThanTwoLines(line, 37, 25, "en").Length;
        }

        return length;
    }

    [Benchmark]
    public int AutoBreakMoreThanTwoLines_Max20()
    {
        var length = 0;
        foreach (var line in _longLines)
        {
            length += Utilities.AutoBreakLineMoreThanTwoLines(line, 20, 25, "en").Length;
        }

        return length;
    }

    [Benchmark]
    public int AutoBreakMoreThanTwoLines_WordLongerThanMax()
    {
        return Utilities.AutoBreakLineMoreThanTwoLines(_lineWithLongWord, 30, 25, "en").Length;
    }

    [Benchmark]
    public int SplitToThree()
    {
        var count = 0;
        foreach (var line in _longLines)
        {
            count += _importer.SplitToThree(line).Count;
        }

        return count;
    }

    // 3. N-way split

    [Benchmark]
    public int SplitText()
    {
        var count = 0;
        for (var i = 0; i < _paragraphTexts.Length; i++)
        {
            count += TextSplit.SplitText(_paragraphTexts[i], 3 + i % 4).Count;
        }

        return count;
    }

    [Benchmark]
    public int SplitMulti()
    {
        var count = 0;
        for (var i = 0; i < _paragraphTexts.Length; i++)
        {
            count += TextSplit.SplitMulti(_paragraphTexts[i], 3 + i % 4, "en").Count;
        }

        return count;
    }

    // 4. merge short lines

    [Benchmark]
    public int MergeShortLines_2000()
    {
        return MergeShortLinesUtils.MergeShortLinesInSubtitle(_shortLines, 500, 80, true).Paragraphs.Count;
    }

    [Benchmark]
    public int SubRipControl() => new SubRip().ToText(_controlSubtitle, "t").Length;
}
