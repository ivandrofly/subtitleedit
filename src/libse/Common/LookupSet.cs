using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Nikse.SubtitleEdit.Core.Common
{
    /// <summary>
    /// Represents a set of characters that can be looked up.
    /// </summary>
    public class LookupSet : IEnumerable<char>
    {
        public static readonly LookupSet UppercaseLetters = Configuration.Settings.General.UppercaseLetters.ToUpperInvariant() + "ΑΒΓΔΕΖΗΘΙΚΛΜΝΞΟΠΡΣΤΥΦΧΨΩ";
        public static readonly LookupSet LowercaseLetters = Configuration.Settings.General.UppercaseLetters.ToLowerInvariant() + "αβγδεζηθικλμνξοπρσςτυφχψωήάόέ";
        public static readonly LookupSet LowercaseLettersWithNumbers = LowercaseLetters + "0123456789";
        public static readonly LookupSet Letters = UppercaseLetters + LowercaseLetters;
        public static readonly LookupSet LettersAndNumbers = UppercaseLetters + LowercaseLettersWithNumbers;
        
        
        private readonly HashSet<char> _hashSet;

        private LookupSet(IEnumerable<char> chars) => _hashSet = chars.ToHashSet();

        /// <summary>
        /// Determines whether the specified character is contained in the LookupSet.
        /// </summary>
        /// <param name="ch">The character to locate in the LookupSet.</param>
        /// <returns>
        /// <c>true</c> if the character is found in the LookupSet; otherwise, <c>false</c>.
        /// </returns>
        public bool Contains(char ch) => _hashSet.Contains(ch);

        /// <summary>
        /// Determines whether the specified character is contained in the LookupSet.
        /// </summary>
        /// <param name="ch">The character to locate in the LookupSet.</param>
        /// <returns>
        /// <c>true</c> if the character is found in the LookupSet; otherwise, <c>false</c>.
        /// </returns>
        public bool Contains(string s) => s.Length == 1 && Contains(s[0]);

        IEnumerator<char> IEnumerable<char>.GetEnumerator() => _hashSet.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => _hashSet.GetEnumerator();

        public static implicit operator LookupSet(string value) => new LookupSet(value);
        public static LookupSet operator +(LookupSet left, string right) => new LookupSet(left.Concat(right));
        public static LookupSet operator +(LookupSet left, LookupSet right) => new LookupSet(left.Concat(right));
    }
}