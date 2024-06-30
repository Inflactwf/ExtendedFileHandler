using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace ExtendedFileHandler
{
    public class ConfigWorker
    {
        [DllImport("kernel32", CharSet = CharSet.Unicode)]
        private static extern int GetPrivateProfileString(string Section, string Key, string Default, StringBuilder strBuilder, int Size, string FilePath);

        [DllImport("kernel32")]
        private static extern int GetPrivateProfileString(string Section, string Key, string Default, [In, Out] char[] chars, int Size, string FilePath);

        [DllImport("kernel32")]
        private static extern long WritePrivateProfileString(string Section, string Key, string Value, string FilePath);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        private static extern int GetPrivateProfileSection(string section, IntPtr keyValue, int size, string filePath);

        private readonly string ApplicationName = Assembly.GetExecutingAssembly().GetName().Name;
        private int capacity = 0xFFFF;

        public string Path { get; }

        public ConfigWorker(string configPath = null) =>
            Path = new FileInfo(configPath ?? $"{ApplicationName}.cfg").FullName.ToString();

        public override string ToString() => Path;

        public void DeleteKey(string Key, string Section) =>
            Write(Key, null, Section);

        public void DeleteSection(string Section) =>
            Write(null, null, Section);

        public bool IsKeyExists(string Key, string Section) =>
            ReadValue(Key, Section).Length > 0;

        public string ReadValue(string Key, string Section)
        {
            var retVal = new StringBuilder(capacity);
            GetPrivateProfileString(Section.ToDefault(), Key.ToDefault(), "", retVal, capacity, Path);
            return retVal.ToString().ToUTF8();
        }

        public void Write(string Key, string Value, string Section) =>
            WritePrivateProfileString(string.IsNullOrEmpty(Section) ? Section : Section.ToDefault(),
                                      string.IsNullOrEmpty(Key) ? Key : Key.ToDefault(),
                                      string.IsNullOrEmpty(Value) ? Value : Value.ToDefault(), Path);

        public string[] ReadSections()
        {
            // first line will not recognize if ini file is saved in UTF-8 with BOM
            while (true)
            {
                char[] chars = new char[capacity];

                var profileStringSize = GetPrivateProfileString(null, null, "", chars, capacity, Path);
                int size = profileStringSize;

                if (size == 0)
                    return null;

                if (size < capacity - 2)
                {
                    string result = new string(chars, 0, size).ToUTF8();
                    string[] sections = result.Split(new char[] { '\0' }, StringSplitOptions.RemoveEmptyEntries);
                    return sections;
                }

                capacity *= 2;
            }
        }

        public string[] ReadKeys(string section)
        {
            // first line will not recognize if ini file is saved in UTF-8 with BOM
            while (true)
            {
                char[] chars = new char[capacity];
                int size = GetPrivateProfileString(section.ToDefault(), null, "", chars, capacity, Path);

                if (size == 0)
                    return null;

                if (size < capacity - 2)
                {
                    string result = new string(chars, 0, size).ToUTF8();
                    string[] keys = result.Split(new char[] { '\0' }, StringSplitOptions.RemoveEmptyEntries);
                    return keys;
                }

                capacity *= 2;
            }
        }

        public List<KeyValuePair<string, string>> ReadKeyValuePairs(string Section)
        {
            var KeyValuePairCollection = new List<KeyValuePair<string, string>>();

            while (true)
            {
                IntPtr returnedString = Marshal.AllocCoTaskMem(capacity * sizeof(char));
                int size = GetPrivateProfileSection(Section.ToDefault(), returnedString, capacity, Path);

                if (size == 0)
                {
                    Marshal.FreeCoTaskMem(returnedString);
                    return default;
                }

                if (size < capacity - 2)
                {
                    string[] result = Marshal.PtrToStringAuto(returnedString, size - 1).Split('\0');
                    Marshal.FreeCoTaskMem(returnedString);
                    foreach (var pair in result)
                    {
                        var keyValuePair = pair.ToUTF8().Split('=');
                        KeyValuePairCollection.Add(new KeyValuePair<string, string>(keyValuePair[0], keyValuePair[1]));
                    }

                    return KeyValuePairCollection;
                }

                Marshal.FreeCoTaskMem(returnedString);
                capacity *= 2;
            }
        }
    }
}

