using ExtendedFileHandler.EventArgs;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ExtendedFileHandler
{
    public class DbWorker<T> where T : class, IComparable
    {
        private readonly object syncLocker = new();
        private readonly FileInfo _dbFileInfo;

        public event EventHandler<LogMessageArgs> LogMessageReceived;

        public DbWorker(FileInfo fileInfo, CultureInfo cultureInfo)
        {
            _dbFileInfo = fileInfo;
            Thread.CurrentThread.CurrentCulture = cultureInfo;
            Initialize();
        }

        public FileInfo DbFileInfo
        {
            get
            {
                _dbFileInfo.Refresh();
                return _dbFileInfo;
            }
        }

        public override string ToString() => DbFileInfo.Name;

        private void Initialize()
        {
            lock (syncLocker)
            {
                if (!DbFileInfo.Exists)
                {
                    using var fs = new FileStream(DbFileInfo.FullName, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                    using var sw = new StreamWriter(fs, new UTF8Encoding());
                    sw.Flush();
                    DbFileInfo.Refresh();
                }
            }
        }

        private void LogMessage(string message) =>
            LogMessageReceived?.Invoke(this, new(message));

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
                DialogResult dr = MessageBox.Show($"{ex.Message}\n\nПовторить попытку?",
                    "Extended File Handler", MessageBoxButtons.RetryCancel, MessageBoxIcon.Error);

                if (dr == DialogResult.Retry)
                    AddEntries(entries, overwriteExisting);

                LogMessage($"Не удалось добавить записи. Причина: {ex.Message}");
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
                DialogResult dr = MessageBox.Show($"{ex.Message}\n\nПовторить попытку?",
                    "Extended File Handler", MessageBoxButtons.RetryCancel, MessageBoxIcon.Error);

                if (dr == DialogResult.Retry)
                    AddEntry(entry, overwriteExisting);

                LogMessage($"Не удалось добавить {entry}. Причина: {ex.Message}");
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
                DialogResult dr = MessageBox.Show($"{ex.Message}\n\nПовторить попытку?",
                    "Extended File Handler", MessageBoxButtons.RetryCancel, MessageBoxIcon.Error);

                if (dr == DialogResult.Retry)
                    DeleteEntry(predicate);

                LogMessage($"Не удалось удалить запись. Причина: {ex.Message}");
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
                DialogResult dr = MessageBox.Show($"{ex.Message}\n\nПовторить попытку?",
                    "Extended File Handler", MessageBoxButtons.RetryCancel, MessageBoxIcon.Error);

                if (dr == DialogResult.Retry)
                    DeleteEntry(entry);

                LogMessage($"Не удалось удалить {entry}. Причина: {ex.Message}");
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
                DialogResult dr = MessageBox.Show($"{ex.Message}\n\nПовторить попытку?",
                    "Extended File Handler", MessageBoxButtons.RetryCancel, MessageBoxIcon.Error);

                if (dr == DialogResult.Retry)
                    DeleteEntries(predicate);

                LogMessage($"Не удалось удалить записи. Причина: {ex.Message}");
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
                DialogResult dr = MessageBox.Show($"{ex.Message}\n\nПовторить попытку?",
                    "Extended File Handler", MessageBoxButtons.RetryCancel, MessageBoxIcon.Error);

                if (dr == DialogResult.Retry)
                    DeleteEntries(entries);

                LogMessage($"Не удалось удалить записи. Причина: {ex.Message}");
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
                DialogResult dr = MessageBox.Show($"{ex.Message}\n\nПовторить попытку?",
                    "Extended File Handler", MessageBoxButtons.RetryCancel, MessageBoxIcon.Error);

                if (dr == DialogResult.Retry)
                    EditEntry(entry, action);

                LogMessage($"Не удалось редактировать {entry}.\nПричина: {ex.Message}");
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
                DialogResult dr = MessageBox.Show($"{ex.Message}\n\nПовторить попытку?",
                    "Extended File Handler", MessageBoxButtons.RetryCancel, MessageBoxIcon.Error);

                if (dr == DialogResult.Retry)
                    EditEntry(entry, action);

                LogMessage($"Не удалось редактировать {entry}.\nПричина: {ex.Message}");
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
                DialogResult dr = MessageBox.Show($"{ex.Message}\n\nПовторить попытку?",
                    "Extended File Handler", MessageBoxButtons.RetryCancel, MessageBoxIcon.Error);

                if (dr == DialogResult.Retry)
                    SaveEntry(entry);

                LogMessage($"Не удалось сохранить {entry}.\nПричина: {ex.Message}");
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
                DialogResult dr = MessageBox.Show($"{ex.Message}\n\nПовторить попытку?",
                    "Extended File Handler", MessageBoxButtons.RetryCancel, MessageBoxIcon.Error);

                if (dr == DialogResult.Retry)
                    ReplaceAll(newEntries);

                LogMessage($"Не удалось заменить записи.\nПричина: {ex.Message}");
            }
        }

        #endregion

        #region Json Handlers

        private async Task<string> ReadDbCacheAsync()
        {
            try
            {
                using var fs = new FileStream(DbFileInfo.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 4096, true);
                var buffer = new byte[fs.Length];
                await fs.ReadAsync(buffer, 0, buffer.Length);
                return Encoding.UTF8.GetString(buffer);
            }
            catch (FileNotFoundException)
            {
                Initialize();
            }
            catch { }

            return string.Empty;
        }

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
                    if (MessageBox.Show(
                        $"An error occurred while serializing the JSON file:\n\n{ex.Message}{ex.StackTrace}",
                        "Extended File Handler",
                        MessageBoxButtons.RetryCancel,
                        MessageBoxIcon.Error) == DialogResult.Retry)
                    {
                        WriteInternal(content);
                    }
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

                    if (!string.IsNullOrWhiteSpace(content))
                        return JsonConvert.DeserializeObject<IEnumerable<T>>(content);
                }
                catch (FileNotFoundException)
                {
                    Initialize();
                }
                catch (Exception ex)
                {
                    DialogResult dr =
                        MessageBox.Show($"An error occurred while deserializing the JSON file:\n\n{ex.Message}{ex.StackTrace}",
                        "Extended File Handler", MessageBoxButtons.RetryCancel, MessageBoxIcon.Error);

                    if (dr == DialogResult.Retry)
                        return ReadInternal();
                }

                return [];
            }
        }

        private async Task<IEnumerable<T>> ReadInternalAsync()
        {
            try
            {
                var dbCache = await ReadDbCacheAsync();

                if (!string.IsNullOrWhiteSpace(dbCache))
                {
                    return JsonConvert.DeserializeObject<IEnumerable<T>>(dbCache);
                }
                else
                {
                    Initialize();
                    return await ReadInternalAsync();
                }
            }
            catch (Exception ex)
            {
                DialogResult dr = MessageBox.Show($"An error occurred while deserializing the JSON file:\n\n{ex.Message}{ex.StackTrace}",
                    "Extended File Handler", MessageBoxButtons.RetryCancel, MessageBoxIcon.Error);

                return dr == DialogResult.Retry
                    ? await ReadInternalAsync()
                    : ([]);
            }
        }

        #endregion
    }
}
