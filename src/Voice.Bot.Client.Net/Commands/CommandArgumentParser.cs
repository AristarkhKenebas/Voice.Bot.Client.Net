using System.Text;

namespace Voice.Bot.Client.Net.Commands;

/// <summary>One argument of a text command and where it starts in the text, so the rest of the text can be taken as-is.</summary>
internal readonly record struct ArgumentToken(string Value, int Start);

/// <summary>
/// Splits the text after a command name into positional tokens shell-style - whitespace-separated, with
/// <c>"quoted"</c> or <c>'quoted'</c> substrings kept together as a single token (e.g.
/// <c>tag "hello world" 3</c> tokenizes to <c>["tag", "hello world", "3"]</c>) and a backslash escaping the
/// next character (<c>\"</c>, <c>\\</c>, <c>\ </c>). A quote only opens a quoted token at its start, so
/// words like <c>don't</c> stay intact; an unterminated quote runs to the end of the text.
/// </summary>
internal static class CommandArgumentParser
{
    public static List<string> Tokenize(string input) => Split(input).Select(t => t.Value).ToList();

    public static List<ArgumentToken> Split(string input)
    {
        var tokens = new List<ArgumentToken>();
        var current = new StringBuilder();
        var i = 0;

        while (i < input.Length)
        {
            while (i < input.Length && char.IsWhiteSpace(input[i])) i++;
            if (i >= input.Length) break;

            var start = i;
            current.Clear();
            if (input[i] is '"' or '\'')
            {
                var quote = input[i++];
                while (i < input.Length && input[i] != quote)
                {
                    if (input[i] == '\\' && i + 1 < input.Length && (input[i + 1] == quote || input[i + 1] == '\\')) i++;
                    current.Append(input[i++]);
                }

                i++;
            }
            else
            {
                while (i < input.Length && !char.IsWhiteSpace(input[i]))
                {
                    if (input[i] == '\\' && i + 1 < input.Length) i++;
                    current.Append(input[i++]);
                }
            }

            tokens.Add(new ArgumentToken(current.ToString(), start));
        }

        return tokens;
    }
}
