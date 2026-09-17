using Nikse.SubtitleEdit.Core.Common;

namespace LibSETests.Common;

public class TextSplitBalanceTest
{
    private const string Rights = "We hold these truths to be self-evident, that all men are created equal, that they are endowed by their Creator with certain unalienable Rights.";
    private const string Listen = "I told you yesterday. You never listen to me! Why would today be any different from all the other days we have spent together?";

    [Fact]
    public void SplitText_DoesNotLeaveTheRemainderToTheLastPart()
    {
        // Cutting part by part at the space before each multiple of the average gave 26/30/36/49.
        var parts = TextSplit.SplitText(Rights, 4);

        Assert.Equal(new List<string>
        {
            "We hold these truths to be self-evident,",
            "that all men are created equal, that",
            "they are endowed by their Creator",
            "with certain unalienable Rights.",
        }, parts);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(6)]
    [InlineData(10)]
    public void SplitText_ReturnsTheRequestedParts_AndLosesNothing(int numberOfParts)
    {
        var parts = TextSplit.SplitText(Listen, numberOfParts);

        Assert.Equal(numberOfParts, parts.Count);
        Assert.DoesNotContain(parts, string.IsNullOrWhiteSpace);
        Assert.Equal(Listen, string.Join(" ", parts));
    }

    [Fact]
    public void SplitText_FewerWordsThanParts_ReturnsTheWords()
    {
        Assert.Equal(new List<string> { "Hello", "world" }, TextSplit.SplitText("Hello world", 3));
    }

    [Fact]
    public void SplitText_OnePart_ReturnsTheText()
    {
        Assert.Equal(new List<string> { "Hello world" }, TextSplit.SplitText("Hello world", 1));
    }

    [Fact]
    public void SplitMulti_PrefersWholeSentences()
    {
        // The even split is 41/41/42 with "...listen to" / "me! Why would..."
        var parts = TextSplit.SplitMulti(Listen, 3, "en");

        Assert.Equal(new List<string>
        {
            "I told you yesterday. You never listen to me!",
            "Why would today be any different from all",
            "the other days we have spent together?",
        }, parts);
    }

    [Fact]
    public void SplitMulti_PrefersCommasOverMidClause()
    {
        var parts = TextSplit.SplitMulti(Rights, 3, "en");

        Assert.Equal(3, parts.Count);
        Assert.Equal("We hold these truths to be self-evident,", parts[0]);
    }

    [Fact]
    public void SplitMulti_FarAwaySentenceEnding_DoesNotUnbalanceTheSplit()
    {
        var parts = TextSplit.SplitMulti("Yes. The quick brown fox jumps over the lazy dog while the farmer sleeps quietly under the old oak tree near the river bank", 2, "en");

        Assert.Equal(2, parts.Count);
        Assert.NotEqual("Yes.", parts[0]);
    }
}
