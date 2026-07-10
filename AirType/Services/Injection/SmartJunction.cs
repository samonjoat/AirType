namespace AirType.Services.Injection;

internal static class SmartJunction
{
    private static readonly HashSet<char> SentenceEndingCharacters = new() { '.', '!', '?' };
    private static readonly HashSet<char> OpeningBracketCharacters = new() { '(', '[', '{' };
    private static readonly HashSet<char> QuoteCharacters = new() { '"', '\'' };
    private static readonly HashSet<char> ClosingWrapperCharacters = new() { ')', ']', '}', '"', '\'' };

    public static string Apply(string newText, CaretContext context)
    {
        if (string.IsNullOrEmpty(newText) ||
            context.IsDocumentEmpty ||
            context.IsCaretAtStart ||
            context.PrecedingCharacter == null)
        {
            return newText;
        }

        string result = newText;
        char decisionCharacter = ResolveDecisionCharacter(context);

        if (IsSentenceEnding(decisionCharacter))
        {
            result = DropLeadingDuplicatePunctuation(result, decisionCharacter);
        }

        if (ShouldPrependSeparatingSpace(context, result))
        {
            result = " " + result;
        }

        return ApplyLeadingCasing(result, decisionCharacter);
    }

    private static string DropLeadingDuplicatePunctuation(string text, char preceding)
    {
        if (text.Length == 0 || text[0] != preceding)
        {
            return text;
        }

        return text[1..];
    }

    private static bool ShouldPrependSeparatingSpace(CaretContext context, string text)
    {
        if (text.Length == 0)
        {
            return false;
        }

        char preceding = context.PrecedingCharacter!.Value;
        return !char.IsWhiteSpace(preceding) &&
               !IsOpeningSpacingException(context) &&
               !char.IsWhiteSpace(text[0]);
    }

    private static string ApplyLeadingCasing(string text, char preceding)
    {
        int firstLetterIndex = FindFirstLetterIndex(text);
        if (firstLetterIndex < 0)
        {
            return text;
        }

        if (IsSentenceEnding(preceding))
        {
            return ReplaceChar(text, firstLetterIndex, char.ToUpperInvariant(text[firstLetterIndex]));
        }

        if (IsMidSentencePrecedingCharacter(preceding) &&
            !ShouldPreserveMidSentenceCapitalization(text, firstLetterIndex))
        {
            return ReplaceChar(text, firstLetterIndex, char.ToLowerInvariant(text[firstLetterIndex]));
        }

        return text;
    }

    private static int FindFirstLetterIndex(string text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            if (char.IsLetter(text[i]))
            {
                return i;
            }
        }

        return -1;
    }

    private static bool ShouldPreserveMidSentenceCapitalization(string text, int firstLetterIndex)
    {
        string firstWord = ReadFirstWord(text, firstLetterIndex);
        if (firstWord.Equals("I", StringComparison.Ordinal) ||
            firstWord.StartsWith("I'", StringComparison.Ordinal) ||
            firstWord.StartsWith("I\u2019", StringComparison.Ordinal))
        {
            return true;
        }

        return CountLeadingUppercaseLetters(firstWord) >= 2;
    }

    private static string ReadFirstWord(string text, int firstLetterIndex)
    {
        int end = firstLetterIndex;
        while (end < text.Length && (char.IsLetter(text[end]) || text[end] is '\'' or '\u2019'))
        {
            end++;
        }

        return text[firstLetterIndex..end];
    }

    private static int CountLeadingUppercaseLetters(string word)
    {
        int count = 0;
        foreach (char ch in word)
        {
            if (!char.IsLetter(ch))
            {
                continue;
            }

            if (!char.IsUpper(ch))
            {
                break;
            }

            count++;
        }

        return count;
    }

    private static bool IsMidSentencePrecedingCharacter(char preceding) =>
        !char.IsWhiteSpace(preceding) &&
        !OpeningBracketCharacters.Contains(preceding) &&
        !QuoteCharacters.Contains(preceding) &&
        !IsSentenceEnding(preceding);

    private static bool IsSentenceEnding(char ch) => SentenceEndingCharacters.Contains(ch);

    private static bool IsOpeningSpacingException(CaretContext context)
    {
        char preceding = context.PrecedingCharacter!.Value;
        if (OpeningBracketCharacters.Contains(preceding))
        {
            return true;
        }

        return QuoteCharacters.Contains(preceding) && !HasLookbackBeforeTrailingClosers(context);
    }

    private static char ResolveDecisionCharacter(CaretContext context)
    {
        char preceding = context.PrecedingCharacter!.Value;
        if (!ClosingWrapperCharacters.Contains(preceding))
        {
            return preceding;
        }

        string? precedingText = context.PrecedingText;
        if (string.IsNullOrEmpty(precedingText))
        {
            return preceding;
        }

        for (int i = precedingText.Length - 1; i >= 0; i--)
        {
            char ch = precedingText[i];
            if (ClosingWrapperCharacters.Contains(ch))
            {
                continue;
            }

            return ch;
        }

        return preceding;
    }

    private static bool HasLookbackBeforeTrailingClosers(CaretContext context)
    {
        string? precedingText = context.PrecedingText;
        if (string.IsNullOrEmpty(precedingText))
        {
            return false;
        }

        for (int i = precedingText.Length - 1; i >= 0; i--)
        {
            if (!ClosingWrapperCharacters.Contains(precedingText[i]))
            {
                return true;
            }
        }

        return false;
    }

    private static string ReplaceChar(string text, int index, char replacement) =>
        replacement == text[index]
            ? text
            : text[..index] + replacement + text[(index + 1)..];
}
