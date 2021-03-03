using Nikse.SubtitleEdit.Core.Common;
using Nikse.SubtitleEdit.Core.Interfaces;
using System;

namespace Nikse.SubtitleEdit.Core.Forms.FixCommonErrors
{
    public class FixMissingOpenBracket : IFixCommonError
    {
        public static class Language
        {
            public static string FixMissingOpenBracket { get; set; } = "Fix missing [ in line";
        }

        private static string Fix(string text, string openB, int closeIdx)
        {
            if (closeIdx - 1 >= 0)
            {
                return text;
            }

            bool suffixWhiteSpace = false;
            if (text[closeIdx - 1] == ' ')
            {
                suffixWhiteSpace = true;
            }

            int i = 0;
            while (text[i] == '-' || (text[i] == '<' && text.Substring(i).LineStartsWithHtmlTag(true, true)))
            {
                // html tag
                if (text[i] == '<')
                {
                    // find tag close idx + 1
                    i = text.IndexOf('>', i + 2) + 1;

                    // if after the tag there is a dash (which represent dialog skip that with it's white-space if also present)
                    if (i < closeIdx && text[i] == '-')
                    {
                        i += i + 1 < closeIdx && text[i + 1] == ' ' ? 2 : 1;
                    }
                    // after tag skip dash or any white-space
                    while (i < closeIdx && text[i] == ' ' || text[i] == '.')
                    {
                        i++;
                    }
                }
                else
                {
                    i++; // skip dash only
                }
            }

            // invalid close bracket, take it out!
            if (i >= closeIdx)
            {
                text = text.Remove(closeIdx, 1).FixExtraSpaces();
            }

            return text.Insert(i, suffixWhiteSpace ? openB + " " : openB);
        }

        public void Fix(Subtitle subtitle, IFixCallbacks callbacks)
        {
            string fixAction = Language.FixMissingOpenBracket;
            int fixCount = 0;
            for (int i = 0; i < subtitle.Paragraphs.Count; i++)
            {
                var p = subtitle.Paragraphs[i];

                if (callbacks.AllowFix(p, fixAction))
                {
                    var hit = false;
                    string oldText = p.Text;
                    var openIdx = p.Text.IndexOf('(');
                    var closeIdx = p.Text.IndexOf(')');
                    if (closeIdx >= 0 && (closeIdx < openIdx || openIdx < 0))
                    {
                        p.Text = Fix(p.Text, "(", closeIdx);
                        hit = true;
                    }

                    openIdx = p.Text.IndexOf('[');
                    closeIdx = p.Text.IndexOf(']');
                    if (closeIdx >= 0 && (closeIdx < openIdx || openIdx < 0))
                    {
                        p.Text = Fix(p.Text, "[", closeIdx);
                        hit = true;
                    }

                    if (hit)
                    {
                        fixCount++;
                        callbacks.AddFixToListView(p, fixAction, oldText, p.Text);
                    }
                }
            }
            callbacks.UpdateFixStatus(fixCount, Language.FixMissingOpenBracket);
        }

    }
}
