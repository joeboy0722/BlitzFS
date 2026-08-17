using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Windows;

namespace BlitzFS.UI.Services
{
    public enum ClipboardOperation
    {
        None,
        Copy,
        Cut
    }

    /// <summary>
    /// 專業剪貼簿管理服務 (支援本地磁碟與便攜式手機 MTP 虛擬路徑，並與 Windows 剪貼簿完全相容)
    /// </summary>
    public static class AppClipboardService
    {
        private const string PreferredDropEffectFormat = "Preferred DropEffect";
        private const int DROPEFFECT_COPY = 1;
        private const int DROPEFFECT_MOVE = 2;

        private static ClipboardOperation _currentOperation = ClipboardOperation.None;
        private static readonly List<string> _internalFiles = new();
        private static readonly HashSet<string> _cutFiles = new(StringComparer.OrdinalIgnoreCase);

        public static ClipboardOperation CurrentOperation => _currentOperation;

        /// <summary>
        /// 判斷特定檔案是否處於「剪下」狀態 (用於 UI 呈現半透明效果)
        /// </summary>
        public static bool IsFileCut(string fullPath)
        {
            return _currentOperation == ClipboardOperation.Cut && _cutFiles.Contains(fullPath);
        }

        /// <summary>
        /// 執行複製 (Ctrl+C / 右鍵複製)
        /// </summary>
        public static void SetCopy(IEnumerable<string> paths)
        {
            _cutFiles.Clear();
            _internalFiles.Clear();
            _currentOperation = ClipboardOperation.Copy;

            var stringCollection = new StringCollection();
            foreach (var p in paths)
            {
                if (string.IsNullOrEmpty(p)) continue;
                _internalFiles.Add(p);

                if (File.Exists(p) || Directory.Exists(p))
                {
                    stringCollection.Add(p);
                }
            }

            if (_internalFiles.Count == 0) return;

            try
            {
                var dataObject = new DataObject();
                if (stringCollection.Count > 0)
                {
                    dataObject.SetFileDropList(stringCollection);
                }

                // 寫入 Windows Shell 標準 Preferred DropEffect (Copy = 1)
                byte[] copyEffect = new byte[] { (byte)DROPEFFECT_COPY, 0, 0, 0 };
                using var ms = new MemoryStream(copyEffect);
                dataObject.SetData(PreferredDropEffectFormat, ms);

                Clipboard.SetDataObject(dataObject, true);
            }
            catch {}
        }

        /// <summary>
        /// 執行剪下 (Ctrl+X / 右鍵剪下)
        /// </summary>
        public static void SetCut(IEnumerable<string> paths)
        {
            _cutFiles.Clear();
            _internalFiles.Clear();
            _currentOperation = ClipboardOperation.Cut;

            var stringCollection = new StringCollection();
            foreach (var p in paths)
            {
                if (string.IsNullOrEmpty(p)) continue;
                _internalFiles.Add(p);
                _cutFiles.Add(p);

                if (File.Exists(p) || Directory.Exists(p))
                {
                    stringCollection.Add(p);
                }
            }

            if (_internalFiles.Count == 0) return;

            try
            {
                var dataObject = new DataObject();
                if (stringCollection.Count > 0)
                {
                    dataObject.SetFileDropList(stringCollection);
                }

                // 寫入 Windows Shell 標準 Preferred DropEffect (Move = 2)
                byte[] moveEffect = new byte[] { (byte)DROPEFFECT_MOVE, 0, 0, 0 };
                using var ms = new MemoryStream(moveEffect);
                dataObject.SetData(PreferredDropEffectFormat, ms);

                Clipboard.SetDataObject(dataObject, true);
            }
            catch {}
        }

        /// <summary>
        /// 獲取剪貼簿中的檔案以及是否為「移動/剪下」操作
        /// </summary>
        public static (List<string> Files, bool IsMove) GetClipboardFiles()
        {
            var list = new List<string>();
            bool isMove = (_currentOperation == ClipboardOperation.Cut);

            // 若內部有跨裝置/手機或本地剪貼檔案，優先使用
            if (_internalFiles.Count > 0)
            {
                list.AddRange(_internalFiles);
                return (list, isMove);
            }

            try
            {
                var dataObject = Clipboard.GetDataObject();
                if (dataObject == null) return (list, false);

                if (dataObject.GetDataPresent(DataFormats.FileDrop))
                {
                    string[]? files = dataObject.GetData(DataFormats.FileDrop) as string[];
                    if (files != null)
                    {
                        list.AddRange(files);
                    }
                }

                // 檢查 Windows 外部複製/剪下所設置的 Preferred DropEffect
                if (dataObject.GetDataPresent(PreferredDropEffectFormat))
                {
                    if (dataObject.GetData(PreferredDropEffectFormat) is MemoryStream ms)
                    {
                        byte[] buffer = new byte[4];
                        if (ms.Read(buffer, 0, 4) > 0)
                        {
                            int effect = BitConverter.ToInt32(buffer, 0);
                            if (effect == DROPEFFECT_MOVE)
                            {
                                isMove = true;
                            }
                        }
                    }
                }
            }
            catch {}

            return (list, isMove);
        }

        /// <summary>
        /// 剪下操作在貼上完成後重設剪貼簿狀態
        /// </summary>
        public static void ClearCutStateAfterPaste()
        {
            if (_currentOperation == ClipboardOperation.Cut)
            {
                _cutFiles.Clear();
                _internalFiles.Clear();
                _currentOperation = ClipboardOperation.None;
                try
                {
                    Clipboard.Clear();
                }
                catch {}
            }
            else
            {
                // 若為複製，內部集合依然保留供多次貼上
            }
        }
    }
}
