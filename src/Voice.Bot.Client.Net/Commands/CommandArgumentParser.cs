namespace Voice.Bot.Client.Net.Commands;

/// <summary>
/// Splits the text after a command name into positional tokens - whitespace-separated, with
/// <c>"quoted substrings"</c> kept together as a single token (e.g. <c>tag "hello world" 3</c> tokenizes
/// to <c>["tag", "hello world", "3"]</c>). No escape-sequence support - kept intentionally simple.
/// </summary>
internal static class CommandArgumentParser
{
    public static List<string> Tokenize(string input)
    {
        var tokens = new List<string>();
        var i = 0;

        while (i < input.Length)
        {
            while (i < input.Length && char.IsWhiteSpace(input[i])) i++;
            if (i >= input.Length) break;

            if (input[i] == '"')
            {
                var closing = input.IndexOf('"', i + 1);
                if (closing < 0)
                {
                    tokens.Add(input[(i + 1)..]);
                    break;
                }

                tokens.Add(input[(i + 1)..closing]);
                i = closing + 1;
            }
            else
            {
                var start = i;
                while (i < input.Length && !char.IsWhiteSpace(input[i])) i++;
                tokens.Add(input[start..i]);
            }
        }

        return tokens;
    }
}
