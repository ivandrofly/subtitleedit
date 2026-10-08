using Nikse.SubtitleEdit.Core.Interfaces;
using Nikse.SubtitleEdit.UiLogic.SpellCheck;
using System;
using System.IO;

namespace UITests.Features.SpellCheck;

public class UserPhraseSpellCheckTests : IDisposable
{
    private const string LanguageName = "en_US";

    private readonly Func<string> _originalSpellCheckDictionariesFolder;
    private readonly string _tempDictionariesFolder;

    public UserPhraseSpellCheckTests()
    {
        // Phrases are seeded through a user dictionary file in an isolated temp folder, which the
        // SpellCheckWordLists constructor loads - AddUserWord would write to the real one.
        _originalSpellCheckDictionariesFolder = SpellCheckConfig.DictionariesFolder;
        _tempDictionariesFolder = Path.Combine(Path.GetTempPath(), "SeUserPhraseTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDictionariesFolder);
        SpellCheckConfig.DictionariesFolder = () => _tempDictionariesFolder;
    }

    private SpellCheckWordLists CreateWordLists(params string[] phrases)
    {
        var xml = "<words>" + string.Concat(Array.ConvertAll(phrases, p => "<word>" + p + "</word>")) + "</words>";
        File.WriteAllText(Path.Combine(_tempDictionariesFolder, LanguageName + "_user.xml"), xml);
        return new SpellCheckWordLists(LanguageName, new AlwaysMissSpellChecker());
    }

    private static SpellCheckWord Word(string text, string word, int occurrence = 0)
    {
        var index = -1;
        for (var i = 0; i <= occurrence; i++)
        {
            index = text.IndexOf(word, index + 1, StringComparison.Ordinal);
        }

        return new SpellCheckWord { Index = index, Text = word };
    }

    [Fact]
    public void WordsInsidePhrase_AreCovered()
    {
        var wordLists = CreateWordLists("hasta la vista");
        const string text = "Hasta la vista, baby.";

        Assert.True(wordLists.IsWordInUserPhrases(Word(text, "Hasta"), text));
        Assert.True(wordLists.IsWordInUserPhrases(Word(text, "la"), text));
        Assert.True(wordLists.IsWordInUserPhrases(Word(text, "vista"), text));
        Assert.False(wordLists.IsWordInUserPhrases(Word(text, "baby"), text));
    }

    [Fact]
    public void SameWordOutsidePhrase_IsNotCovered()
    {
        var wordLists = CreateWordLists("la vista");
        const string text = "la la vista";

        Assert.False(wordLists.IsWordInUserPhrases(Word(text, "la", 0), text));
        Assert.True(wordLists.IsWordInUserPhrases(Word(text, "la", 1), text));
    }

    [Fact]
    public void WordOnlyPartlyInsidePhrase_IsNotCovered()
    {
        var wordLists = CreateWordLists("la vis");
        const string text = "la vista";

        Assert.True(wordLists.IsWordInUserPhrases(Word(text, "la"), text));
        Assert.False(wordLists.IsWordInUserPhrases(Word(text, "vista"), text));
    }

    [Fact]
    public void OverlappingPhrases_BothCover()
    {
        var wordLists = CreateWordLists("ad hoc", "hoc est");
        const string text = "ad hoc est";

        Assert.True(wordLists.IsWordInUserPhrases(Word(text, "ad"), text));
        Assert.True(wordLists.IsWordInUserPhrases(Word(text, "est"), text));
    }

    [Fact]
    public void CachedLine_DoesNotLeakIntoNextLine()
    {
        var wordLists = CreateWordLists("hasta la vista");
        const string first = "hasta la vista";
        const string second = "hasta mañana";

        Assert.True(wordLists.IsWordInUserPhrases(Word(first, "hasta"), first));
        Assert.False(wordLists.IsWordInUserPhrases(Word(second, "hasta"), second));
        Assert.True(wordLists.IsWordInUserPhrases(Word(first, "hasta"), first));
    }

    [Fact]
    public void NoPhrases_NothingCovered()
    {
        var wordLists = CreateWordLists();
        const string text = "hasta la vista";

        Assert.False(wordLists.IsWordInUserPhrases(Word(text, "la"), text));
    }

    public void Dispose()
    {
        SpellCheckConfig.DictionariesFolder = _originalSpellCheckDictionariesFolder;
        try
        {
            Directory.Delete(_tempDictionariesFolder, recursive: true);
        }
        catch
        {
            // Best-effort cleanup; leaving a temp dir behind is harmless.
        }
    }

    private sealed class AlwaysMissSpellChecker : IDoSpell
    {
        public bool DoSpell(string word)
        {
            return false;
        }
    }
}
