using System.Text;

namespace ExtendedFileHandler.Extensions
{
    internal static class StringExtensions
    {
        internal static string ToUTF8(this string text) =>
            string.IsNullOrEmpty(text)
            ? string.Empty
            : Encoding.UTF8.GetString(Encoding.Default.GetBytes(text));

        internal static string ToDefault(this string text) =>
            string.IsNullOrEmpty(text)
            ? string.Empty
            : Encoding.Default.GetString(Encoding.UTF8.GetBytes(text));

        internal static bool IsNullOrWhiteSpace(this string str) => string.IsNullOrWhiteSpace(str);
    }
}
