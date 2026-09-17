using Nikse.SubtitleEdit.Core.Common;

namespace LibSETests.Common;

public class AutoBreakMoreThanTwoLinesTest
{
    private static List<string> Break(string text, int maximumLength)
    {
        return Utilities.AutoBreakLineMoreThanTwoLines(text, maximumLength, 25, "en").SplitToLines();
    }

    [Fact]
    public void LinesAreBalanced_NotLeftOverInTheLastLine()
    {
        // Filling line by line gave 26/26/28/28/32.
        var lines = Break("We hold these truths to be self-evident, that all men are created equal, that they are endowed by their Creator with certain unalienable Rights.", 37);

        Assert.Equal(new List<string>
        {
            "We hold these truths to be",
            "self-evident, that all men are",
            "created equal, that they are",
            "endowed by their Creator with",
            "certain unalienable Rights.",
        }, lines);
    }

    [Fact]
    public void UsesTheFewestLinesThatFit()
    {
        // 118 characters fit in four lines of 30 - this used to take five.
        var lines = Break("The quick brown fox jumps over the lazy dog while the farmer sleeps quietly under the old oak tree near the river bank.", 30);

        Assert.Equal(4, lines.Count);
        Assert.All(lines, line => Assert.True(line.Length <= 30));
    }

    [Fact]
    public void ShortTextOverTheMaximum_TakesTwoLinesNotThree()
    {
        // Under "merge lines shorter than", so the two line break leaves it alone - was "Short/line/that fits."
        var lines = Break("Short line that fits.", 20);

        Assert.Equal(new List<string> { "Short line", "that fits." }, lines);
    }

    [Fact]
    public void WordLongerThanMaximum_GetsItsOwnLine_AndTheRestIsStillBroken()
    {
        // One unbreakable word used to make every attempt fail and the text come back unbroken.
        var lines = Break("Supercalifragilisticexpialidocious is a word that Mr. Banks never wanted to hear in his house again, not even once, not ever.", 30);

        Assert.Equal("Supercalifragilisticexpialidocious", lines[0]);
        Assert.All(lines.Skip(1), line => Assert.True(line.Length <= 30));
        Assert.DoesNotContain(lines, line => line.EndsWith("Mr."));
    }

    [Fact]
    public void NoEmptyLines()
    {
        var lines = Break("Supercalifragilisticexpialidocious is a word that Mr. Banks never wanted to hear in his house again, not even once, not ever.", 37);

        Assert.DoesNotContain(lines, string.IsNullOrWhiteSpace);
    }

    [Fact]
    public void HtmlTagsStayOnTheirWords()
    {
        var lines = Break("<i>The quick brown fox jumps over the lazy dog while the farmer sleeps quietly</i> under the old oak tree near the river bank.", 30);

        Assert.StartsWith("<i>The quick", lines[0]);
        Assert.Contains(lines, line => line.StartsWith("sleeps quietly</i> under"));
        Assert.All(lines, line => Assert.True(HtmlUtil.RemoveHtmlTags(line, true).Length <= 30));
    }

    [Fact]
    public void SplitToThree_IsTheMostEvenSplit()
    {
        var importer = new PlainTextImporter(false, false, 1, ".?!", 43, "en");

        var lines = importer.SplitToThree("I told you yesterday. You never listen to me! Why would today be any different from all the other days we have spent together?");

        Assert.Equal(3, lines.Count);
        Assert.True(lines.Max(p => p.Length) - lines.Min(p => p.Length) <= 3);
    }

    [Fact]
    public void SplitToFour_ReturnsFourLines()
    {
        // Was a two line break of a two line break, returned as two elements - so never four.
        var importer = new PlainTextImporter(false, false, 1, ".?!", 37, "en");

        var lines = importer.SplitToFour("The quick brown fox jumps over the lazy dog while the farmer sleeps quietly under the old oak tree near the river bank.");

        Assert.Equal(4, lines.Count);
        Assert.All(lines, line => Assert.True(line.Length <= 37));
    }

    [Fact]
    public void SplitToThree_DoesNotFit_ReturnsTheText()
    {
        var importer = new PlainTextImporter(false, false, 1, ".?!", 10, "en");
        const string text = "The quick brown fox jumps over the lazy dog while the farmer sleeps.";

        Assert.Equal(new List<string> { text }, importer.SplitToThree(text));
    }
}
