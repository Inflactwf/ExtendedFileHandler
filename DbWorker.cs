using ExtendedFileHandler.EventArguments;
using ExtendedFileHandler.Extensions;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ExtendedFileHandler
{
    public class DbWorker<T> where T : class, IComparable
    {
        private readonly object syncLocker = new();
        private readonly FileInfo dbFileInfo;

        public delegate void OnErrorEncountered(ErrorMessageEventArgs e);
        public event OnErrorEncountered OnError;

        public DbWorker(FileInfo fileInfo, CultureInfo cultureInfo)
        {
            dbFileInfo = fileInfo;
            Thread.CurrentThread.CurrentCulture = cultureInfo;
            Initialize();
        }

        public FileInfo DbFileInfo
        {
            get
            {
                dbFileInfo.Refresh();
                return dbFileInfo;
            }
        }

        public override string ToString() => DbFileInfo.Name;

        private void Initialize()
        {
            lock (syncLocker)
            {
                if (!DbFileInfo.Exists)
                {
                    try
                    {
                        using var fs = DbFileInfo.Open(FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                        using var sw = new StreamWriter(fs, new UTF8Encoding());
                        sw.Flush();
                    }
                    catch (Exception ex)
                    {
                        LogMessage($"An error occurred while initializing the file.\nMessage: {ex.Message}", ex.StackTrace);
                    }
                }
            }
        }

        private void LogMessage(string message, string stackTrace) =>
            OnError?.Invoke(new(message, stackTrace));

        #region Entries getters

        /// <summary>
        /// Obtains deserialized <typeparamref name="T"/> entry from the cache file.
        /// </summary>
        /// <param name="predicate"></param>
        /// <returns><typeparamref name="T"/> entry if exists, otherwise <see langword="null"/>.</returns>
        public T GetEntry(Func<T, bool> predicate) =>
            GetEntries().FirstOrDefault(predicate);

        /// <summary>
        /// Obtains deserialized collection of <typeparamref name="T"/> entries from the cache file.
        /// </summary>
        /// <returns><see cref="IEnumerable{T}"/> collection.</returns>
        public IEnumerable<T> GetEntries() =>
            ReadInternal();

        public async Task<IEnumerable<T>> GetEntriesAsync() =>
            await ReadInternalAsync();

        /// <summary>
        /// Obtains deserialized collection of <typeparamref name="T"/> entries from the cache file.
        /// </summary>
        /// <param name="predicate"></param>
        /// <returns><see cref="IEnumerable{T}"/> collection.</returns>
        public IEnumerable<T> GetEntries(Func<T, bool> predicate) =>
            ReadInternal().Where(predicate);

        /// <summary>
        /// Searches directly for a entry through comparer's default property to obtain reference of <typeparamref name="T"/>.
        /// </summary>
        /// <param name="entry"></param>
        /// <param name="source"></param>
        /// <returns><typeparamref name="T"/> entry if exists, otherwise <see langword="null"/>.</returns>
        /// <exception cref="ArgumentNullException"></exception>
        public T SearchEntryDirectlyOrNull(T entry, IEnumerable<T> source = null)
        {
            if (entry is null)
                throw new ArgumentNullException(nameof(entry));

            source ??= GetEntries();

            foreach (var sEntry in source)
                if (Comparer<T>.Default.Compare(sEntry, entry) == 0)
                    return sEntry;

            return default;
        }

        #endregion

        #region Entries checkers

        /// <summary>
        /// Uses direct search to determine if the entry exists in the cache file.
        /// </summary>
        /// <param name="entry"></param>
        /// <param name="source"></param>
        /// <returns><see langword="True"/> if the entry exists, otherwise <see langword="False"/>.</returns>
        /// <exception cref="ArgumentNullException"></exception>
        public bool IsEntryExists(T entry, IEnumerable<T> source) =>
            entry is null
            ? throw new ArgumentNullException(nameof(entry))
            : SearchEntryDirectlyOrNull(entry, source) != null;

        /// <summary>
        /// Uses direct search to determine if the entry exists in the cache file.
        /// </summary>
        /// <param name="entry"></param>
        /// <param name="source"></param>
        /// <returns><see langword="True"/> if the entry exists, otherwise <see langword="False"/>.</returns>
        /// <exception cref="ArgumentNullException"></exception>
        public bool IsEntryExists(T entry) =>
            entry is null
            ? throw new ArgumentNullException(nameof(entry))
            : SearchEntryDirectlyOrNull(entry, GetEntries()) != null;

        /// <summary>
        /// Searches for the entry to determine if the record exists in the cache file.
        /// </summary>
        /// <param name="entry"></param>
        /// <param name="source"></param>
        /// <returns><see langword="True"/> if the entry exists, otherwise <see langword="False"/>.</returns>
        /// <exception cref="ArgumentNullException"></exception>
        public bool IsEntryExists(Func<T, bool> predicate) =>
            GetEntry(predicate) != null;


        /// <summary>
        /// Searches for the entry to determine if the record exists in the cache file and provides the entry model reference.
        /// </summary>
        /// <param name="entry"></param>
        /// <param name="source"></param>
        /// <returns><see langword="True"/> if the entry exists, otherwise <see langword="False"/>.</returns>
        /// <exception cref="ArgumentNullException"></exception>
        public bool TryGetEntry(Func<T, bool> predicate, out T entry)
        {
            entry = GetEntry(predicate);
            return entry != null;
        }

        #endregion

        #region Add

        private void AddOrReplaceEntry(T entry, ref IEnumerable<T> source, bool overwriteExisting)
        {
            var sourceList = source.ToList();
            var foundEntry = SearchEntryDirectlyOrNull(entry, source);
            if (foundEntry == null || foundEntry != null && overwriteExisting)
            {
                var oldEntryIndex = sourceList.IndexOf(foundEntry);
                if (oldEntryIndex == -1)
                    sourceList.Add(entry);
                else
                    sourceList[oldEntryIndex] = entry;
            }
            else
            {
                sourceList.Add(entry);
            }

            source = sourceList;
        }

        /// <summary>
        /// Adds collection of entries to the cache file.
        /// </summary>
        /// <param name="entries"></param>
        /// <exception cref="ArgumentNullException"></exception>
        public void AddEntries(IEnumerable<T> entries, bool overwriteExisting = true)
        {
            if (entries is null)
                throw new ArgumentNullException(nameof(entries));

            try
            {
                var source = GetEntries();

                foreach (var entry in entries)
                    AddOrReplaceEntry(entry, ref source, overwriteExisting);

                WriteInternal(source);
            }
            catch (Exception ex)
            {
                LogMessage($"An error occurred while executing {MethodBase.GetCurrentMethod().Name}.\nMessage: {ex.Message}", ex.StackTrace);
            }
        }

        /// <summary>
        /// Adds the entry to the cache file.
        /// </summary>
        /// <param name="entry"></param>
        /// <exception cref="ArgumentNullException"></exception>
        public void AddEntry(T entry, bool overwriteExisting = true)
        {
            if (entry is null)
                throw new ArgumentNullException(nameof(entry));

            try
            {
                var source = GetEntries();
                AddOrReplaceEntry(entry, ref source, overwriteExisting);
                WriteInternal(source);
            }
            catch (Exception ex)
            {
                LogMessage($"An error occurred while executing {MethodBase.GetCurrentMethod().Name}.\nMessage: {ex.Message}", ex.StackTrace);
            }
        }

        #endregion

        #region Delete

        /// <summary>
        /// Removes selected entry from the cache file.
        /// </summary>
        /// <param name="predicate"></param>
        public void DeleteEntry(Func<T, bool> predicate)
        {
            try
            {
                var source = GetEntries().ToList();
                if (source != null)
                {
                    var foundEntry = source.FirstOrDefault(predicate);
                    if (foundEntry != null)
                    {
                        source.Remove(foundEntry);
                        WriteInternal(source);
                    }
                }
            }
            catch (Exception ex)
            {
                LogMessage($"An error occurred while executing {MethodBase.GetCurrentMethod().Name}.\nMessage: {ex.Message}", ex.StackTrace);
            }
        }

        /// <summary>
        /// Removes selected entry from the cache file.
        /// </summary>
        /// <param name="entry"></param>
        public void DeleteEntry(T entry)
        {
            try
            {
                var source = GetEntries().ToList();
                if (source != null)
                {
                    var foundEntry = SearchEntryDirectlyOrNull(entry, source);
                    if (foundEntry != null)
                    {
                        source.Remove(foundEntry);
                        WriteInternal(source);
                    }
                }
            }
            catch (Exception ex)
            {
                LogMessage($"An error occurred while executing {MethodBase.GetCurrentMethod().Name}.\nMessage: {ex.Message}", ex.StackTrace);
            }
        }

        /// <summary>
        /// Removes selected entries from the cache file.
        /// </summary>
        /// <param name="predicate"></param>
        public void DeleteEntries(Func<T, bool> predicate)
        {
            try
            {
                var source = GetEntries();
                if (source != null)
                {
                    var exceptList = source.Where(predicate);
                    if (exceptList.Any())
                        WriteInternal(source.Except(exceptList));
                }
            }
            catch (Exception ex)
            {
                LogMessage($"An error occurred while executing {MethodBase.GetCurrentMethod().Name}.\nMessage: {ex.Message}", ex.StackTrace);
            }
        }

        /// <summary>
        /// Removes selected entries from the cache file.
        /// </summary>
        /// <param name="entries"></param>
        public void DeleteEntries(IEnumerable<T> entries)
        {
            try
            {
                var source = GetEntries().ToList();
                if (source != null && source.Any())
                {
                    foreach (var tEntry in entries)
                        source.RemoveAll(sEntry => Comparer<T>.Default.Compare(sEntry, tEntry) == 0);

                    WriteInternal(source);
                }
            }
            catch (Exception ex)
            {
                LogMessage($"An error occurred while executing {MethodBase.GetCurrentMethod().Name}.\nMessage: {ex.Message}", ex.StackTrace);
            }
        }

        #endregion

        #region Edit

        /// <summary>
        /// Changes selected entry's properties to the cache file.
        /// </summary>
        /// <param name="entry"></param>
        /// <param name="action"></param>
        /// <returns>Edited <typeparamref name="T"/> entry.</returns>
        /// <exception cref="ArgumentNullException"></exception>
        public T EditEntry(T entry, Action<T> action)
        {
            if (entry is null)
                throw new ArgumentNullException(nameof(entry));

            try
            {
                var source = GetEntries();
                var foundEntry = SearchEntryDirectlyOrNull(entry, source);
                if (foundEntry != null)
                {
                    action(foundEntry);
                    WriteInternal(source);
                }

                return foundEntry;
            }
            catch (Exception ex)
            {
                LogMessage($"An error occurred while executing {MethodBase.GetCurrentMethod().Name}.\nMessage: {ex.Message}", ex.StackTrace);
                return entry;
            }
        }

        /// <summary>
        /// Changes selected entry's properties to the cache file.
        /// </summary>
        /// <param name="entry"></param>
        /// <param name="action"></param>
        /// <returns>Edited <typeparamref name="T"/> entry.</returns>
        /// <exception cref="ArgumentNullException"></exception>
        public T EditEntry(IEnumerable<T> entriesList, T entry, Action<T> action)
        {
            if (entry is null)
                throw new ArgumentNullException(nameof(entry));

            try
            {
                var source = entriesList;
                var foundEntry = SearchEntryDirectlyOrNull(entry, source);
                if (foundEntry != null)
                {
                    action(foundEntry);
                    WriteInternal(source);
                }

                return foundEntry;
            }
            catch (Exception ex)
            {
                LogMessage($"An error occurred while executing {MethodBase.GetCurrentMethod().Name}.\nMessage: {ex.Message}", ex.StackTrace);
                return entry;
            }
        }

        #endregion

        #region Save

        /// <summary>
        /// Universal method that overwrites (if enabled) selected entry if exists, otherwise adds a new one to the cache file.
        /// </summary>
        /// <param name="entry"></param>
        /// <param name="overwriteExisting"></param>
        /// <exception cref="ArgumentNullException"></exception>
        public void SaveEntry(T entry, bool overwriteExisting = true)
        {
            if (entry is null)
                throw new ArgumentNullException(nameof(entry));

            try
            {
                var source = GetEntries();
                AddOrReplaceEntry(entry, ref source, overwriteExisting);
                WriteInternal(source);
            }
            catch (Exception ex)
            {
                LogMessage($"An error occurred while executing {MethodBase.GetCurrentMethod().Name}.\nMessage: {ex.Message}", ex.StackTrace);
            }
        }


        public void ReplaceAll(IEnumerable<T> newEntries)
        {
            if (newEntries is null)
                throw new ArgumentNullException(nameof(newEntries));

            try
            {
                WriteInternal(newEntries);
            }
            catch (Exception ex)
            {
                LogMessage($"An error occurred while executing {MethodBase.GetCurrentMethod().Name}.\nMessage: {ex.Message}", ex.StackTrace);
            }
        }

        #endregion

        #region Json Handlers

        private void WriteInternal(IEnumerable<T> content)
        {
            lock (syncLocker)
            {
                try
                {
                    using var stream = DbFileInfo.Open(FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite);
                    using var writer = new StreamWriter(stream);
                    stream.SetLength(0);
                    writer.Write(JsonConvert.SerializeObject(content, Formatting.Indented));
                }
                catch (FileNotFoundException)
                {
                    Initialize();
                    WriteInternal(content);
                }
                catch (Exception ex)
                {
                    LogMessage($"An error occurred while writing the content to the json file.\nMessage: {ex.Message}", ex.StackTrace);
                }
            }
        }

        private IEnumerable<T> ReadInternal()
        {
            lock (syncLocker)
            {
                try
                {
                    using var stream = DbFileInfo.Open(FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    using var reader = new StreamReader(stream);
                    var content = reader.ReadToEnd();

                    if (!content.IsNullOrWhiteSpace())
                        return JsonConvert.DeserializeObject<IEnumerable<T>>(content);
                }
                catch (FileNotFoundException)
                {
                    Initialize();
                }
                catch (Exception ex)
                {
                    LogMessage($"An error occurred while reading the json file.\nMessage: {ex.Message}", ex.StackTrace);
                }

                return [];
            }
        }

        private async Task<IEnumerable<T>> ReadInternalAsync()
        {
            try
            {
                using var fs = new FileStream(DbFileInfo.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, true);
                var buffer = new byte[fs.Length];
                await fs.ReadAsync(buffer, 0, buffer.Length);
                var content = Encoding.UTF8.GetString(buffer);

                if (!content.IsNullOrWhiteSpace())
                    return JsonConvert.DeserializeObject<IEnumerable<T>>(content);
            }
            catch (FileNotFoundException)
            {
                Initialize();
            }
            catch (Exception ex)
            {
                LogMessage($"An error occurred while asynchronously reading the json file.\nMessage: {ex.Message}", ex.StackTrace);
            }

            return [];
        }

        #endregion
    }
}
