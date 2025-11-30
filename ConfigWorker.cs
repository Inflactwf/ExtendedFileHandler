using ExtendedFileHandler.Extensions;
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
        private static extern int GetPrivateProfileString(string section, string key, string @default, StringBuilder strBuilder, int size, string filePath);

        [DllImport("kernel32")]
        private static extern int GetPrivateProfileString(string section, string key, string @default, [In, Out] char[] chars, int size, string filePath);

        [DllImport("kernel32")]
        private static extern long WritePrivateProfileString(string section, string key, string value, string filePath);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        private static extern int GetPrivateProfileSection(string section, IntPtr keyValue, int size, string filePath);

        private readonly string _applicationName = Assembly.GetExecutingAssembly().GetName().Name;
        private int _capacity = 0xFFFF;

        public string FullPath { get; }

        public ConfigWorker(string configPath = null) =>
            FullPath = new FileInfo(configPath ?? $"{_applicationName}.cfg").FullName;

        public override string ToString() => FullPath;

        public void DeleteKey(string key, string section) =>
            Write(key, null, section);

        public void DeleteSection(string section) =>
            Write(null, null, section);

        public bool IsKeyExists(string key, string section) =>
            ReadValue(key, section).Length > 0;

        public string ReadValue(string key, string section)
        {
            var retVal = new StringBuilder(_capacity);
            GetPrivateProfileString(section.ToDefault(), key.ToDefault(), "", retVal, _capacity, FullPath);
            return retVal.ToString().ToUTF8();
        }

        public void Write(string key, string value, string section) =>
            WritePrivateProfileString(string.IsNullOrEmpty(section) ? section : section.ToDefault(),
                                      string.IsNullOrEmpty(key) ? key : key.ToDefault(),
                                      string.IsNullOrEmpty(value) ? value : value.ToDefault(), FullPath);

        public string[] ReadSections()
        {
            // first line will not recognize if ini file is saved in UTF-8 with BOM
            while (true)
            {
                var chars = new char[_capacity];

                var profileStringSize = GetPrivateProfileString(null, null, "", chars, _capacity, FullPath);

                if (profileStringSize == 0)
                    return null;

                if (profileStringSize < _capacity - 2)
                {
                    var result = new string(chars, 0, profileStringSize).ToUTF8();
                    var sections = result.Split(['\0'], StringSplitOptions.RemoveEmptyEntries);
                    return sections;
                }

                _capacity *= 2;
            }
        }

        public string[] ReadKeys(string section)
        {
            // first line will not recognize if ini file is saved in UTF-8 with BOM
            while (true)
            {
                var chars = new char[_capacity];
                var size = GetPrivateProfileString(section.ToDefault(), null, "", chars, _capacity, FullPath);

                if (size == 0)
                    return null;

                if (size < _capacity - 2)
                {
                    var result = new string(chars, 0, size).ToUTF8();
                    var keys = result.Split(['\0'], StringSplitOptions.RemoveEmptyEntries);
                    return keys;
                }

                _capacity *= 2;
            }
        }

        public List<KeyValuePair<string, string>> ReadKeyValuePairs(string section)
        {
            var keyValuePairCollection = new List<KeyValuePair<string, string>>();

            while (true)
            {
                var returnedString = Marshal.AllocCoTaskMem(_capacity * sizeof(char));
                var size = GetPrivateProfileSection(section.ToDefault(), returnedString, _capacity, FullPath);

                if (size == 0)
                {
                    Marshal.FreeCoTaskMem(returnedString);
                    return null;
                }

                if (size < _capacity - 2)
                {
                    var result = Marshal.PtrToStringAuto(returnedString, size - 1).Split('\0');
                    Marshal.FreeCoTaskMem(returnedString);

                    foreach (var pair in result)
                    {
                        var keyValuePair = pair.ToUTF8().Split('=');
                        keyValuePairCollection.Add(new KeyValuePair<string, string>(keyValuePair[0], keyValuePair[1]));
                    }

                    return keyValuePairCollection;
                }

                Marshal.FreeCoTaskMem(returnedString);
                _capacity *= 2;
            }
        }
    }
}

