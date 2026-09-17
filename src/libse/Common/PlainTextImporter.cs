    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;

    namespace Nikse.SubtitleEdit.Core.Common
    {
        public class PlainTextImporter
        {
            private static readonly CharLookup NonLetterChars =
                CharLookup.Create('0', '1', '2', '3', '4', '5', '6', '7', '8', '9', ':', '.', ',', '-', '>', '/');

            private readonly bool _splitAtBlankLines;
            private readonly bool _removeLinesWithoutLetters;
            private readonly int _numberOfLines;
            private readonly string _endChars;
            private readonly int _singleLineMaxLength;
            private readonly string _language;

            public PlainTextImporter(bool splitAtBlankLines, bool removeLinesWithoutLetters, int numberOfLines, string endChars, int singleLineMaxLength, string language)
            {
                _splitAtBlankLines = splitAtBlankLines;
                _removeLinesWithoutLetters = removeLinesWithoutLetters;
                _numberOfLines = numberOfLines;
                _endChars = endChars ?? string.Empty;
                _singleLineMaxLength = singleLineMaxLength;
                _language = language;
            }

            public List<string> ImportAutoSplit(IEnumerable<string> textLines)
            {
                var sb = new StringBuilder();
                foreach (var line in textLines)
                {
                    var s = line.Replace("\0", string.Empty);
                    if (string.IsNullOrWhiteSpace(s) && _splitAtBlankLines)
                    {
                        sb.Append('\0');
                    }
                    else if (!ContainsLetters(s.Trim()))
                    {
                        if (!_removeLinesWithoutLetters)
                        {
                            sb.AppendLine(s);
                        }
                    }
                    else
                    {
                        sb.AppendLine(s);
                    }
                }
                var text = sb.ToString().Replace(Environment.NewLine, " ");


                var list = new List<string>();
                var untilLastSpace = new StringBuilder();
                var fromLastSpace = new StringBuilder();
                var maxLength = _singleLineMaxLength * _numberOfLines;
                var oneLineOnly = _numberOfLines == 1;
                var hardSplitIndices = new List<int>();
                for (var index = 0; index < text.Length; index++)
                {
                    var ch = text[index];
                    if (ch == '\0')
                    {
                        if (list.Count > 0)
                        {
                            hardSplitIndices.Add(list.Count);
                        }

                        var nextWord = AutoSplitAddLine(oneLineOnly, list, untilLastSpace.ToString() + fromLastSpace, text, index, hardSplitIndices);
                        untilLastSpace.Clear();
                        fromLastSpace.Clear();
                        untilLastSpace.Append(nextWord);
                    }
                    else if (ch == ' ')
                    {
                        if (untilLastSpace.Length + fromLastSpace.Length > maxLength && !NextWordInDoNotBreakList(text, index))
                        {
                            var nextWord = AutoSplitAddLine(oneLineOnly, list, untilLastSpace.ToString(), text, index, hardSplitIndices);
                            untilLastSpace.Clear();
                            untilLastSpace.Append(nextWord);
                        }
                        untilLastSpace.Append(fromLastSpace);
                        if (untilLastSpace.Length > 0)
                        {
                            untilLastSpace.Append(ch);
                        }

                        fromLastSpace.Clear();
                    }
                    else if (_endChars.Contains(ch) && index < text.Length - 2 &&
                             (text[index + 1] == ' ' || _language == "zh" || "。、·？".Contains(ch)) &&
                             !CurrentWordInDoNotBreakList(text, index + 1))
                    {
                        fromLastSpace.Append(ch);

                        if (untilLastSpace.Length + fromLastSpace.Length > maxLength)
                        {
                            var nextWord = AutoSplitAddLine(oneLineOnly, list, untilLastSpace.ToString(), text, index, hardSplitIndices);
                            untilLastSpace.Clear();
                            untilLastSpace.Append(nextWord);
                        }
                        else
                        {
                            var nextWord = AutoSplitAddLine(oneLineOnly, list, untilLastSpace.ToString() + fromLastSpace, text, index, hardSplitIndices);
                            untilLastSpace.Clear();
                            untilLastSpace.Append(nextWord);
                            fromLastSpace.Clear();
                        }
                    }
                    else
                    {
                        fromLastSpace.Append(ch);
                    }
                }
                AutoSplitAddLine(oneLineOnly, list, untilLastSpace.ToString() + fromLastSpace, text, text.Length, hardSplitIndices);
                return list;
            }

            private bool CurrentWordInDoNotBreakList(string text, int index)
            {
                var s2 = " " + text.Substring(0, index).TrimEnd();
                if (Configuration.Settings.Tools.UseNoLineBreakAfter)
                {
                    foreach (var ending in Utilities.NoBreakAfterList(_language))
                    {
                        if (ending.IsMatch(s2))
                        {
                            return true;
                        }
                    }
                }
                else
                {
                    if (s2.EndsWith(" mr.", StringComparison.OrdinalIgnoreCase) ||
                        s2.EndsWith(" dr.", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
                return false;
            }

            private bool NextWordInDoNotBreakList(string text, int index)
            {
                for (var i = index + 1; i < text.Length; i++)
                {
                    var ch = text[i];
                    if (ch == '\0')
                    {
                        return false;
                    }

                    if (ch == ' ')
                    {
                        var s2 = text.Substring(0, i).TrimEnd();
                        if (Configuration.Settings.Tools.UseNoLineBreakAfter)
                        {
                            foreach (var ending in Utilities.NoBreakAfterList(_language))
                            {
                                if (ending.IsMatch(s2))
                                {
                                    return true;
                                }
                            }
                        }
                        else
                        {
                            if (s2.EndsWith(" mr.", StringComparison.OrdinalIgnoreCase) ||
                                s2.EndsWith(" dr.", StringComparison.OrdinalIgnoreCase))
                            {
                                return true;
                            }
                        }
                        return false;
                    }
                }
                return false;
            }

            private string AutoSplitAddLine(bool oneLineOnly, List<string> list, string input, string allText, int allIndex, List<int> hardSplitIndices)
            {
                if (string.IsNullOrWhiteSpace(input))
                {
                    return string.Empty;
                }

                var text = input;
                while (text.Contains("  "))
                {
                    text = text.Replace("  ", " ");
                }

                if (oneLineOnly)
                {
                    list.Add(Utilities.UnbreakLine(text.Trim()));
                    return string.Empty;
                }

                var line = Utilities.AutoBreakLine(text.Trim(), _singleLineMaxLength, Configuration.Settings.General.MergeLinesShorterThan, _language);
                if (ExceedsMax(line) || CurrentWordInDoNotBreakList(line, line.Length))
                {
                    var lastWords = string.Empty;
                    for (int i = 0; i < 3; i++)
                    {
                        int lastIndexOfSpace = line.LastIndexOf(' ');
                        if (lastIndexOfSpace > 0 && CanLastWordBeMovedToNext(line, allText, allIndex))
                        {
                            lastWords = line.Substring(lastIndexOfSpace).Trim() + " " + lastWords;
                            line = line.Substring(0, lastIndexOfSpace).Trim();
                            line = Utilities.AutoBreakLine(line, _singleLineMaxLength, Configuration.Settings.General.MergeLinesShorterThan, _language);
                            if (!ExceedsMax(line))
                            {
                                list.Add(line);
                                return lastWords.Trim() + " "; // use this word in next paragraph
                            }
                        }
                    }
                    foreach (var l in SplitToThreeOrFourLines(text))
                    {
                        list.Add(l);
                    }
                }
                else
                {
                    if (list.Count > 0 &&
                        line.Length < _singleLineMaxLength / 2.5 &&
                        !hardSplitIndices.Contains(list.Count - 1) &&
                        CanLastWordBeMovedToNext(list[list.Count - 1], allText, allIndex))
                    {
                        var three = SplitToThree(Utilities.UnbreakLine(list[list.Count - 1] + " " + line));
                        if (three.Count == 3)
                        {
                            list.RemoveAt(list.Count - 1);
                            var firstLine = Utilities.AutoBreakLine(three[0] + " " + three[1], _singleLineMaxLength, Configuration.Settings.General.MergeLinesShorterThan, _language);
                            list.Add(firstLine);
                            list.Add(three[2]);
                            return string.Empty;
                        }
                    }
                    list.Add(line);
                }
                return string.Empty;
            }

            private bool ExceedsMax(string line)
            {
                foreach (var s in line.SplitToLines())
                {
                    if (s.Length > _singleLineMaxLength)
                    {
                        return true;
                    }
                }
                return false;
            }

            private bool CanLastWordBeMovedToNext(string line, string allText, int allIndex)
            {
                if (allIndex == allText.Length ||
                    _endChars.Contains(line[line.Length - 1]) && !CurrentWordInDoNotBreakList(line, line.Length))
                {
                    return false;
                }

                return true;
            }

            private List<string> SplitToThreeOrFourLines(string input)
            {
                var text = Utilities.UnbreakLine(input);
                var three = SplitToThree(text);
                var four = SplitToFour(text);
                if (three.Count == 3)
                {
                    return three;
                }
                if (four.Count == 4)
                {
                    return four;
                }
                return new List<string> { text };
            }

            public List<string> SplitToFour(string text)
            {
                return SplitToBalancedLines(text, 4);
            }

            public List<string> SplitToThree(string input)
            {
                return SplitToBalancedLines(input, 3);
            }

            /// <summary>
            /// The most even split into <paramref name="count"/> lines that all fit the single line
            /// maximum, or the text itself when there is none.
            /// </summary>
            private List<string> SplitToBalancedLines(string input, int count)
            {
                var text = input.Trim();
                var breaks = TextPartition.Split(text, count, _singleLineMaxLength, i => Utilities.CanBreak(text, i, _language) ? 0 : Utilities.NoBreakCost);
                return breaks == null ? new List<string> { text } : TextPartition.GetParts(text, breaks);
            }

            public static bool ContainsLetters(string line)
            {
                if (line.IsOnlyCharsOrWhiteSpace(NonLetterChars))
                {
                    return false;
                }

                const string expectedChars = "\r\n\t .?\0";
                foreach (char ch in line)
                {
                    if (!expectedChars.Contains(ch))
                    {
                        return true;
                    }
                }
                return false;
            }

        }
    }
