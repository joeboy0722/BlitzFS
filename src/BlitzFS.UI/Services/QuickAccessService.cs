using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace BlitzFS.UI.Services
{
    /// <summary>
    /// 快速存取項目持久化資料模型
    /// </summary>
    public class QuickAccessEntry
    {
        public string Title { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public string IconKey { get; set; } = "IconFolder";
        public bool IsCustom { get; set; } = false;
    }

    /// <summary>
    /// 快速存取釘選管理與持久化服務
    /// </summary>
    public class QuickAccessService
    {
        private static readonly Lazy<QuickAccessService> _instance = new(() => new QuickAccessService());
        public static QuickAccessService Instance => _instance.Value;

        private readonly string _configFilePath;
        private readonly List<QuickAccessEntry> _entries = new();

        public event Action? QuickAccessChanged;

        private QuickAccessService()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string configDir = System.IO.Path.Combine(appData, "BlitzFS");
            if (!Directory.Exists(configDir))
            {
                try { Directory.CreateDirectory(configDir); } catch {}
            }
            _configFilePath = System.IO.Path.Combine(configDir, "quick_access.json");
            Load();
        }

        public IReadOnlyList<QuickAccessEntry> GetEntries()
        {
            lock (_entries)
            {
                return new List<QuickAccessEntry>(_entries);
            }
        }

        public bool IsPinned(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            string normalized = path.TrimEnd('\\', '/');
            lock (_entries)
            {
                return _entries.Exists(e => string.Equals(e.Path.TrimEnd('\\', '/'), normalized, StringComparison.OrdinalIgnoreCase));
            }
        }

        public bool PinPath(string path, string? title = null)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                return false;

            string normalized = path.TrimEnd('\\', '/');
            lock (_entries)
            {
                if (IsPinned(normalized))
                    return false;

                string displayTitle = !string.IsNullOrWhiteSpace(title)
                    ? title
                    : new DirectoryInfo(path).Name;

                if (string.IsNullOrWhiteSpace(displayTitle))
                    displayTitle = path;

                _entries.Add(new QuickAccessEntry
                {
                    Title = displayTitle,
                    Path = path,
                    IconKey = "IconFolder",
                    IsCustom = true
                });

                Save();
            }

            QuickAccessChanged?.Invoke();
            return true;
        }

        public bool UnpinPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            string normalized = path.TrimEnd('\\', '/');

            bool removed = false;
            lock (_entries)
            {
                int count = _entries.RemoveAll(e => string.Equals(e.Path.TrimEnd('\\', '/'), normalized, StringComparison.OrdinalIgnoreCase));
                if (count > 0)
                {
                    removed = true;
                    Save();
                }
            }

            if (removed)
            {
                QuickAccessChanged?.Invoke();
            }

            return removed;
        }

        private void Load()
        {
            lock (_entries)
            {
                _entries.Clear();

                if (File.Exists(_configFilePath))
                {
                    try
                    {
                        string json = File.ReadAllText(_configFilePath);
                        var loaded = JsonSerializer.Deserialize<List<QuickAccessEntry>>(json);
                        if (loaded != null && loaded.Count > 0)
                        {
                            foreach (var item in loaded)
                            {
                                if (Directory.Exists(item.Path))
                                {
                                    _entries.Add(item);
                                }
                            }
                            return;
                        }
                    }
                    catch
                    {
                        // 若讀取損毀則回退至預設
                    }
                }

                // 首次執行或無設定檔時載入系統預設常用資料夾
                AddDefaultEntry("桌面", Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "IconDesktop");
                AddDefaultEntry("下載", System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"), "IconDownloads");
                AddDefaultEntry("文件", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "IconDocuments");
                AddDefaultEntry("圖片", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "IconPictures");
                AddDefaultEntry("影片", Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "IconVideos");

                Save();
            }
        }

        private void AddDefaultEntry(string title, string path, string iconKey)
        {
            if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
            {
                _entries.Add(new QuickAccessEntry
                {
                    Title = title,
                    Path = path,
                    IconKey = iconKey,
                    IsCustom = false
                });
            }
        }

        private void Save()
        {
            try
            {
                string json = JsonSerializer.Serialize(_entries, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_configFilePath, json);
            }
            catch {}
        }
    }
}
