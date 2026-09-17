using Nikse.SubtitleEdit.Core.Common;

namespace LibSETests.Core;

public class MergeShortLinesUtilsTest
{
    [Fact]
    public void ThreeShortLines()
    {
        var subtitle = new Subtitle();
        subtitle.Paragraphs.Add(new Paragraph("How", 0, 200));
        subtitle.Paragraphs.Add(new Paragraph("are", 200, 400));
        subtitle.Paragraphs.Add(new Paragraph("you?", 400, 600));
        var mergedSubtitle = MergeShortLinesUtils.MergeShortLinesInSubtitle(subtitle, 500, 80, true);

        Assert.Single(mergedSubtitle.Paragraphs);
        Assert.Equal("How are you?", mergedSubtitle.Paragraphs[0].Text);
    }

    [Fact]
    public void ThreeShortLinesNoMergeDueToLength()
    {
        var subtitle = new Subtitle();
        subtitle.Paragraphs.Add(new Paragraph("How", 0, 200));
        subtitle.Paragraphs.Add(new Paragraph("are", 200, 400));
        subtitle.Paragraphs.Add(new Paragraph("you?", 400, 600));
        var mergedSubtitle = MergeShortLinesUtils.MergeShortLinesInSubtitle(subtitle, 500, 2, true);

        Assert.Equal(3, mergedSubtitle.Paragraphs.Count);
    }

    [Fact]
    public void ThreeShortLinesNoMergeDueToGap()
    {
        var subtitle = new Subtitle();
        subtitle.Paragraphs.Add(new Paragraph("How", 0, 200));
        subtitle.Paragraphs.Add(new Paragraph("are", 2000, 2400));
        subtitle.Paragraphs.Add(new Paragraph("you?", 4400, 4600));
        var mergedSubtitle = MergeShortLinesUtils.MergeShortLinesInSubtitle(subtitle, 500, 80, true);

        Assert.Equal(3, mergedSubtitle.Paragraphs.Count);
    }

    [Fact]
    public void FourLines_AreMergedTwoAndTwo_NotThreeAndOne()
    {
        // Merging on until the next line no longer fits gave "aaaa, bbbb, cccc," + "dddd."
        var subtitle = new Subtitle();
        subtitle.Paragraphs.Add(new Paragraph("aaaa aaaa,", 0, 1000));
        subtitle.Paragraphs.Add(new Paragraph("bbbb bbbb,", 1000, 2000));
        subtitle.Paragraphs.Add(new Paragraph("cccc cccc,", 2000, 3000));
        subtitle.Paragraphs.Add(new Paragraph("dddd dddd.", 3000, 4000));

        var mergedSubtitle = MergeShortLinesUtils.MergeShortLinesInSubtitle(subtitle, 500, 35, true);

        Assert.Equal(2, mergedSubtitle.Paragraphs.Count);
        Assert.Equal("aaaa aaaa, bbbb bbbb,", Utilities.UnbreakLine(mergedSubtitle.Paragraphs[0].Text));
        Assert.Equal("cccc cccc, dddd dddd.", Utilities.UnbreakLine(mergedSubtitle.Paragraphs[1].Text));
        Assert.Equal(2000, mergedSubtitle.Paragraphs[0].EndTime.TotalMilliseconds);
        Assert.Equal(2000, mergedSubtitle.Paragraphs[1].StartTime.TotalMilliseconds);
        Assert.Equal(4000, mergedSubtitle.Paragraphs[1].EndTime.TotalMilliseconds);
    }

    [Fact]
    public void NeverMoreParagraphsThanMergingOnUntilFull()
    {
        // 10 + 10 + 10 fits in 35, the fourth does not: still two paragraphs, not three.
        var subtitle = new Subtitle();
        subtitle.Paragraphs.Add(new Paragraph("aaaa aaaa,", 0, 1000));
        subtitle.Paragraphs.Add(new Paragraph("bbbb bbbb,", 1000, 2000));
        subtitle.Paragraphs.Add(new Paragraph("cccc cccc,", 2000, 3000));
        subtitle.Paragraphs.Add(new Paragraph("dddd dddd dddd dddd dddd dddd.", 3000, 4000));

        var mergedSubtitle = MergeShortLinesUtils.MergeShortLinesInSubtitle(subtitle, 500, 35, true);

        Assert.Equal(2, mergedSubtitle.Paragraphs.Count);
        Assert.Equal("aaaa aaaa, bbbb bbbb, cccc cccc,", Utilities.UnbreakLine(mergedSubtitle.Paragraphs[0].Text));
    }

    [Fact]
    public void SentenceEndingStopsTheRun()
    {
        var subtitle = new Subtitle();
        subtitle.Paragraphs.Add(new Paragraph("aaaa aaaa,", 0, 1000));
        subtitle.Paragraphs.Add(new Paragraph("bbbb bbbb.", 1000, 2000));
        subtitle.Paragraphs.Add(new Paragraph("cccc cccc,", 2000, 3000));
        subtitle.Paragraphs.Add(new Paragraph("dddd dddd.", 3000, 4000));
        subtitle.Paragraphs.Add(new Paragraph("eeee eeee.", 4000, 5000));

        var mergedSubtitle = MergeShortLinesUtils.MergeShortLinesInSubtitle(subtitle, 500, 80, true);

        Assert.Equal(3, mergedSubtitle.Paragraphs.Count);
        Assert.Equal("eeee eeee.", mergedSubtitle.Paragraphs[2].Text);
    }

    [Fact]
    public void EmptyAndSingle()
    {
        Assert.Empty(MergeShortLinesUtils.MergeShortLinesInSubtitle(new Subtitle(), 500, 80, true).Paragraphs);

        var subtitle = new Subtitle();
        subtitle.Paragraphs.Add(new Paragraph("How", 0, 200));
        Assert.Single(MergeShortLinesUtils.MergeShortLinesInSubtitle(subtitle, 500, 80, true).Paragraphs);
    }
}
