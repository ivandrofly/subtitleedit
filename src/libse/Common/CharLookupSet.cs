using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Nikse.SubtitleEdit.Core.Common
{
    /// <summary>
    /// Represents a set of characters that can be looked up.
    /// </summary>
    public class CharLookupSet : IEnumerable<char>
    {
        public static readonly CharLookupSet UppercaseLetters = Configuration.Settings.General.UppercaseLetters.ToUpperInvariant() + "ΑΒΓΔΕΖΗΘΙΚΛΜΝΞΟΠΡΣΤΥΦΧΨΩ";
        public static readonly CharLookupSet LowercaseLetters = Configuration.Settings.General.UppercaseLetters.ToLowerInvariant() + "αβγδεζηθικλμνξοπρσςτυφχψωήάόέ";
        public static readonly CharLookupSet LowercaseLettersWithNumbers = LowercaseLetters + "0123456789";
        public static readonly CharLookupSet Letters = UppercaseLetters + LowercaseLetters;
        public static readonly CharLookupSet LettersAndNumbers = UppercaseLetters + LowercaseLettersWithNumbers;
        
        
        private readonly HashSet<char> _hashSet;

        private CharLookupSet(IEnumerable<char> chars) => _hashSet = chars.ToHashSet();

        /// <summary>
        /// Determines whether the specified character is contained in the CharLookupSet.
        /// </summary>
        /// <param name="ch">The character to locate in the CharLookupSet.</param>
        /// <returns>
        /// <c>true</c> if the character is found in the CharLookupSet; otherwise, <c>false</c>.
        /// </returns>
        public bool Contains(char ch) => _hashSet.Contains(ch);

        /// <summary>
        /// Determines whether the specified character is contained in the CharLookupSet.
        /// </summary>
        /// <param name="ch">The character to locate in the CharLookupSet.</param>
        /// <returns>
        /// <c>true</c> if the character is found in the CharLookupSet; otherwise, <c>false</c>.
        /// </returns>
        public bool Contains(string s) => s.Length == 1 && Contains(s[0]);

        IEnumerator<char> IEnumerable<char>.GetEnumerator() => _hashSet.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => _hashSet.GetEnumerator();

        public static implicit operator CharLookupSet(string value) => new CharLookupSet(value);
        public static CharLookupSet operator +(CharLookupSet left, string right) => new CharLookupSet(left.Concat(right));
        public static CharLookupSet operator +(CharLookupSet left, CharLookupSet right) => new CharLookupSet(left.Concat(right));
    }
}