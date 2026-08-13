using System.Text;

namespace Artel.Affordances.Scan
{
    /// <summary>Just enough JSON to write a document, and nothing to read one.</summary>
    /// <remarks>
    /// Hand-written so the package carries no serialisation dependency into a game assembly. The
    /// evidence baked by the analyser is passed through as it was written and is never parsed here,
    /// so only the writing half is needed.
    /// </remarks>
    internal static class Json
    {
        internal static void Property(StringBuilder text, string name, string value)
        {
            String(text, name);
            text.Append(':');
            String(text, value);
        }

        internal static void Property(StringBuilder text, string name, int value)
        {
            String(text, name);
            text.Append(':').Append(value);
        }

        internal static void Property(StringBuilder text, string name, bool value)
        {
            String(text, name);
            text.Append(value ? ":true" : ":false");
        }

        internal static void String(StringBuilder text, string value)
        {
            if (value == null)
            {
                text.Append("null");
                return;
            }

            text.Append('"');

            foreach (var character in value)
            {
                switch (character)
                {
                    case '"': text.Append("\\\""); break;
                    case '\\': text.Append("\\\\"); break;
                    case '\b': text.Append("\\b"); break;
                    case '\f': text.Append("\\f"); break;
                    case '\n': text.Append("\\n"); break;
                    case '\r': text.Append("\\r"); break;
                    case '\t': text.Append("\\t"); break;
                    default:
                        if (character < 32)
                        {
                            text.Append("\\u").Append(((int)character).ToString("x4"));
                        }
                        else
                        {
                            text.Append(character);
                        }

                        break;
                }
            }

            text.Append('"');
        }
    }
}
