using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;

namespace GTSteamVRBodyTracking.Core
{
    public enum LoadStatus
    {
        NotFound,
        Loaded,
        LoadedWithRepairs,
        RecoveredFromBackup,
        Failed
    }

    public struct SaveResult
    {
        public bool Success;
        public string Message;
    }

    /// <summary>
    /// Persists <see cref="ModConfigData"/> as JSON in BepInEx/config.
    /// Writes are atomic: data goes to "file.tmp", is flushed to disk, then replaces the
    /// target while the previous version is kept as "file.bak". An interrupted write therefore
    /// never destroys the last valid configuration.
    /// </summary>
    public sealed class ConfigStore
    {
        private static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            MissingMemberHandling = MissingMemberHandling.Ignore,
            ObjectCreationHandling = ObjectCreationHandling.Replace,
            NullValueHandling = NullValueHandling.Include,
            DefaultValueHandling = DefaultValueHandling.Include
        };

        private readonly Dictionary<string, TrackerSettings> _bySerial = new Dictionary<string, TrackerSettings>(StringComparer.Ordinal);

        public string FilePath { get; }
        public string BackupPath => FilePath + ".bak";
        public string TempPath => FilePath + ".tmp";

        public ModConfigData Data { get; private set; } = new ModConfigData();
        public bool Dirty { get; private set; }
        public List<string> LastLoadMessages { get; } = new List<string>();
        public LoadStatus LastLoadStatus { get; private set; } = LoadStatus.NotFound;

        /// <summary>Incremented on every in-memory change; lets the UI refresh only when needed.</summary>
        public int Revision { get; private set; }

        public ConfigStore(string filePath)
        {
            FilePath = filePath;
        }

        public void MarkDirty()
        {
            Dirty = true;
            Revision++;
        }

        public LoadStatus Load()
        {
            LastLoadMessages.Clear();
            LoadStatus status;

            if (TryRead(FilePath, out ModConfigData data, out string error))
            {
                status = LoadStatus.Loaded;
            }
            else
            {
                bool primaryExists = File.Exists(FilePath);
                if (primaryExists) LastLoadMessages.Add($"Could not read {Path.GetFileName(FilePath)}: {error}");

                if (TryRead(BackupPath, out data, out string backupError))
                {
                    status = LoadStatus.RecoveredFromBackup;
                    LastLoadMessages.Add("Loaded the backup copy (.bak) instead.");
                }
                else if (!primaryExists && !File.Exists(BackupPath))
                {
                    data = new ModConfigData();
                    status = LoadStatus.NotFound;
                    LastLoadMessages.Add("No saved configuration yet; using defaults.");
                }
                else
                {
                    data = new ModConfigData();
                    status = LoadStatus.Failed;
                    if (File.Exists(BackupPath)) LastLoadMessages.Add($"Backup unreadable too: {backupError}");
                    LastLoadMessages.Add("Using defaults. The unreadable file is left untouched until you press Save.");
                }
            }

            var repairs = new List<string>();
            ConfigValidator.Validate(data, repairs);
            if (repairs.Count > 0)
            {
                LastLoadMessages.AddRange(repairs);
                if (status == LoadStatus.Loaded) status = LoadStatus.LoadedWithRepairs;
            }

            Data = data;
            RebuildIndex();
            Dirty = false;
            Revision++;
            LastLoadStatus = status;
            return status;
        }

        public SaveResult Save()
        {
            try
            {
                var warnings = new List<string>();
                ConfigValidator.Validate(Data, warnings);
                RebuildIndex();
                string json = JsonConvert.SerializeObject(Data, JsonSettings);

                // Round-trip check before touching the real file.
                if (!TryParse(json, out _, out string parseError))
                    return new SaveResult { Success = false, Message = "Serialization check failed: " + parseError };

                WriteAllTextAtomic(FilePath, json, BackupPath);
                Dirty = false;
                Revision++;
                return new SaveResult { Success = true, Message = $"Saved {Data.Trackers.Count} tracker(s) to {Path.GetFileName(FilePath)}." };
            }
            catch (Exception e)
            {
                return new SaveResult { Success = false, Message = $"Save failed: {e.GetType().Name}: {e.Message}" };
            }
        }

        /// <summary>Replaces all settings with defaults in memory. Nothing is written until Save.</summary>
        public void RestoreDefaults()
        {
            Data = new ModConfigData();
            RebuildIndex();
            MarkDirty();
        }

        public TrackerSettings Find(string serial)
        {
            if (string.IsNullOrEmpty(serial)) return null;
            return _bySerial.TryGetValue(serial, out TrackerSettings s) ? s : null;
        }

        /// <summary>
        /// Returns the saved settings for a serial, creating defaults if needed. Discovery passes
        /// <paramref name="markDirty"/> = false so merely plugging a tracker in is not an "unsaved change".
        /// </summary>
        public TrackerSettings GetOrCreate(string serial, string model, bool markDirty = true)
        {
            serial = TrackerIdentity.Normalize(serial);
            if (!TrackerIdentity.IsReliable(serial)) return null;
            TrackerSettings s = Find(serial);
            if (s != null)
            {
                if (!string.IsNullOrEmpty(model) && s.LastKnownModel != model) s.LastKnownModel = model;
                return s;
            }
            s = new TrackerSettings { Serial = serial, LastKnownModel = model ?? string.Empty };
            Data.Trackers.Add(s);
            _bySerial[serial] = s;
            if (markDirty) MarkDirty();
            else Revision++;
            return s;
        }

        public bool Remove(string serial)
        {
            TrackerSettings s = Find(serial);
            if (s == null) return false;
            Data.Trackers.Remove(s);
            _bySerial.Remove(serial);
            MarkDirty();
            return true;
        }

        private void RebuildIndex()
        {
            _bySerial.Clear();
            foreach (TrackerSettings t in Data.Trackers)
                if (t != null && !_bySerial.ContainsKey(t.Serial)) _bySerial[t.Serial] = t;
        }

        private static bool TryRead(string path, out ModConfigData data, out string error)
        {
            data = null;
            error = null;
            try
            {
                if (!File.Exists(path)) { error = "file not found"; return false; }
                string json = File.ReadAllText(path, Encoding.UTF8);
                return TryParse(json, out data, out error);
            }
            catch (Exception e)
            {
                error = e.GetType().Name + ": " + e.Message;
                return false;
            }
        }

        public static bool TryParse(string json, out ModConfigData data, out string error)
        {
            data = null;
            error = null;
            if (string.IsNullOrWhiteSpace(json)) { error = "file is empty"; return false; }
            try
            {
                data = JsonConvert.DeserializeObject<ModConfigData>(json, JsonSettings);
                if (data == null) { error = "file contained no data"; return false; }
                return true;
            }
            catch (Exception e)
            {
                error = e.GetType().Name + ": " + e.Message;
                return false;
            }
        }

        public static string Serialize(ModConfigData data) => JsonConvert.SerializeObject(data, JsonSettings);

        /// <summary>Write-to-temp, flush, then replace (keeping a backup).</summary>
        public static void WriteAllTextAtomic(string path, string contents, string backupPath)
        {
            string dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            string tmp = path + ".tmp";

            byte[] bytes = new UTF8Encoding(false).GetBytes(contents);
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                fs.Write(bytes, 0, bytes.Length);
                fs.Flush(true);
            }

            if (!File.Exists(path))
            {
                File.Move(tmp, path);
                return;
            }

            try
            {
                File.Replace(tmp, path, backupPath, true);
            }
            catch (Exception e) when (e is PlatformNotSupportedException || e is IOException || e is UnauthorizedAccessException || e is NotImplementedException)
            {
                // Fallback for runtimes without ReplaceFile: keep a backup first so a valid copy always exists.
                File.Copy(path, backupPath, true);
                File.Delete(path);
                File.Move(tmp, path);
            }
        }
    }
}
