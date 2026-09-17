using System;
using System.Collections.Generic;

namespace Nikse.SubtitleEdit.Core.Common
{
    public static class MergeShortLinesUtils
    {
        public static Subtitle MergeShortLinesInSubtitle(Subtitle subtitle, double maxMillisecondsBetweenLines, int maxCharacters, bool onlyContinuousLines)
        {
            var language = LanguageAutoDetect.AutoDetectGoogleLanguage(subtitle);
            var mergedSubtitle = new Subtitle();
            var paragraphs = subtitle.Paragraphs;
            var runStart = 0;
            while (runStart < paragraphs.Count)
            {
                // a run of lines where each may be merged with the next, the total length aside
                var runEnd = runStart;
                while (runEnd + 1 < paragraphs.Count &&
                       Utilities.QualifiesForMerge(paragraphs[runEnd], paragraphs[runEnd + 1], maxMillisecondsBetweenLines, int.MaxValue, onlyContinuousLines))
                {
                    runEnd++;
                }

                var index = runStart;
                foreach (var groupEnd in GetGroupEnds(paragraphs, runStart, runEnd, maxCharacters))
                {
                    var p = new Paragraph(paragraphs[index]);
                    for (var i = index + 1; i <= groupEnd; i++)
                    {
                        Merge(p, paragraphs[i], language);
                    }

                    mergedSubtitle.Paragraphs.Add(p);
                    index = groupEnd + 1;
                }

                runStart = runEnd + 1;
            }

            return mergedSubtitle;
        }

        /// <summary>
        /// Where to cut a run of mergeable lines so every group stays under the maximum length:
        /// as few groups as possible, and of those the most even ones. Merging on until the next
        /// line no longer fits gives as few groups too, but fills every group except the last -
        /// four lines could end up as three + one where two + two fits as well.
        /// Returns the index of the last line of each group.
        /// </summary>
        private static List<int> GetGroupEnds(List<Paragraph> paragraphs, int runStart, int runEnd, int maxCharacters)
        {
            var count = runEnd - runStart + 1;
            if (count == 1)
            {
                return new List<int>(1) { runEnd };
            }

            var lengths = new int[count];
            for (var i = 0; i < count; i++)
            {
                lengths[i] = HtmlUtil.RemoveHtmlTags((paragraphs[runStart + i].Text ?? string.Empty).Trim(), true).Length;
            }

            // for the first "end" lines of the run: fewest groups, then lowest sum of squared group lengths
            var groupCounts = new int[count + 1];
            var squares = new long[count + 1];
            var from = new int[count + 1];
            for (var end = 1; end <= count; end++)
            {
                groupCounts[end] = int.MaxValue;
                var length = 0;
                for (var start = end; start >= 1; start--)
                {
                    var lines = end - start + 1;
                    length += lengths[start - 1];

                    // as QualifiesForMerge: what is merged so far, one separator per merge, plus the next line
                    if (lines > 1 && length + lines - 2 >= maxCharacters)
                    {
                        break;
                    }

                    var groupCount = groupCounts[start - 1] + 1;
                    var square = squares[start - 1] + (long)length * length;
                    if (groupCount < groupCounts[end] || groupCount == groupCounts[end] && square < squares[end])
                    {
                        groupCounts[end] = groupCount;
                        squares[end] = square;
                        from[end] = start - 1;
                    }
                }
            }

            var groupEnds = new List<int>(groupCounts[count]);
            for (var end = count; end > 0; end = from[end])
            {
                groupEnds.Add(runStart + end - 1);
            }

            groupEnds.Reverse();
            return groupEnds;
        }

        private static void Merge(Paragraph p, Paragraph next, string language)
        {
            if (GetStartTag(p.Text) == GetStartTag(next.Text) && GetEndTag(p.Text) == GetEndTag(next.Text))
            {
                var s1 = p.Text.Trim();
                s1 = s1.Substring(0, s1.Length - GetEndTag(s1).Length);
                var s2 = next.Text.Trim();
                s2 = s2.Substring(GetStartTag(s2).Length);
                p.Text = Utilities.AutoBreakLine(s1 + Environment.NewLine + s2, language);
            }
            else
            {
                p.Text = Utilities.AutoBreakLine(p.Text + Environment.NewLine + next.Text, language);
            }

            p.EndTime = next.EndTime;
        }

        public static string GetEndTag(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            text = text.Trim();
            if (!text.EndsWith('>'))
            {
                return string.Empty;
            }

            var endTag = string.Empty;
            var start = text.LastIndexOf("</", StringComparison.Ordinal);
            if (start > 0 && start >= text.Length - 8)
            {
                endTag = text.Substring(start);
            }
            return endTag;
        }

        public static string GetStartTag(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            text = text.Trim();
            if (!text.StartsWith('<'))
            {
                return string.Empty;
            }

            var startTag = string.Empty;
            var end = text.IndexOf('>');
            if (end > 0 && end < 25)
            {
                startTag = text.Substring(0, end + 1);
            }

            return startTag;
        }
    }
}
