using System;
using System.Collections.Generic;
using System.Text;

namespace CvAut.Models
{
    /// <summary>
    /// Reads the <c>[MODULE] key=value key="quoted value"</c> shape the backend logs in.
    /// </summary>
    public sealed partial class LogEntry
    {
        private string? GetField(string key)
            => Fields.TryGetValue(key, out string? value) ? value : null;

        private static string ParseModule(string message)
        {
            if (message.StartsWith("[", StringComparison.Ordinal))
            {
                int end = message.IndexOf(']');
                if (end > 1)
                {
                    return message.Substring(1, end - 1);
                }
            }

            return "APP";
        }

        private static IReadOnlyDictionary<string, string> ParseFields(string message)
        {
            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            int i = 0;
            while (i < message.Length)
            {
                while (i < message.Length && char.IsWhiteSpace(message[i]))
                {
                    i++;
                }

                int keyStart = i;
                while (i < message.Length && (char.IsLetterOrDigit(message[i]) || message[i] == '_' || message[i] == '-'))
                {
                    i++;
                }

                if (i <= keyStart || i >= message.Length || message[i] != '=')
                {
                    i++;
                    continue;
                }

                string key = message.Substring(keyStart, i - keyStart);
                i++; // '='
                string value;
                if (i < message.Length && message[i] == '"')
                {
                    i++;
                    var sb = new StringBuilder();
                    while (i < message.Length)
                    {
                        char c = message[i++];
                        if (c == '"')
                        {
                            break;
                        }

                        sb.Append(c);
                    }

                    value = sb.ToString();
                }
                else
                {
                    int valueStart = i;
                    while (i < message.Length && !char.IsWhiteSpace(message[i]))
                    {
                        i++;
                    }

                    value = message.Substring(valueStart, i - valueStart);
                }

                if (!string.IsNullOrWhiteSpace(key))
                {
                    fields[key] = value;
                }
            }

            return fields;
        }
    }
}
