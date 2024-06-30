using System;
using System.Text;

namespace ExtendedFileHandler
{
    internal static class Extensions
    {
        public static string ToUTF8(this string text) =>
            String.IsNullOrEmpty(text) ? String.Empty : Encoding.UTF8.GetString(Encoding.Default.GetBytes(text));

        public static string ToDefault(this string text) =>
            String.IsNullOrEmpty(text) ? String.Empty : Encoding.Default.GetString(Encoding.UTF8.GetBytes(text));
    }
}
