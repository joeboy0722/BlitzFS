using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace BlitzFS.Bridge
{
    /// <summary>
    /// BlitzFS C++ 核心引擎之 C# 託管安全封裝類別
    /// </summary>
    public sealed class CoreEngineWrapper : IDisposable
    {
        private IntPtr _engineHandle;
        private bool _disposed;
        private IntPtr _stringPoolPtr;

        public CoreEngineWrapper()
        {
            _engineHandle = NativeMethods.BlitzFS_CreateEngine();
            if (_engineHandle == IntPtr.Zero)
            {
                throw new InvalidOperationException("無法初始化 BlitzFS.Core 原生引擎。");
            }
        }

        public IntPtr Handle => _engineHandle;
        public uint TotalNodeCount => _engineHandle != IntPtr.Zero ? NativeMethods.BlitzFS_GetNodeCount(_engineHandle) : 0;
        public ulong RootFileId => _engineHandle != IntPtr.Zero ? NativeMethods.BlitzFS_GetRootFileId(_engineHandle) : 0;

        public ulong FindNodeByPath(string path)
        {
            ThrowIfDisposed();
            return NativeMethods.BlitzFS_FindNodeByPath(_engineHandle, path);
        }

        /// <summary>
        /// 非同步掃描指定磁碟區並建立極速記憶體樹索引
        /// </summary>
        public Task<bool> ScanVolumeAsync(char driveLetter, IProgress<uint>? progress = null)
        {
            ThrowIfDisposed();

            return Task.Run(() =>
            {
                ScanProgressCallback? callback = null;
                if (progress != null)
                {
                    callback = (scannedCount, _) => progress.Report(scannedCount);
                }

                bool result = NativeMethods.BlitzFS_ScanVolume(_engineHandle, driveLetter, callback, IntPtr.Zero);
                if (result)
                {
                    _stringPoolPtr = NativeMethods.BlitzFS_GetStringPoolPointer(_engineHandle);
                }

                return result;
            });
        }

        /// <summary>
        /// O(1) 瞬時查詢指定目錄下的所有子節點
        /// </summary>
        public unsafe List<CompactNode> GetChildren(ulong parentId)
        {
            ThrowIfDisposed();

            var list = new List<CompactNode>();
            uint count = NativeMethods.BlitzFS_GetChildren(_engineHandle, parentId, out IntPtr indicesPtr);
            if (count == 0 || indicesPtr == IntPtr.Zero)
            {
                return list;
            }

            uint* indices = (uint*)indicesPtr.ToPointer();
            for (uint i = 0; i < count; i++)
            {
                uint nodeIndex = indices[i];
                IntPtr nodePtr = NativeMethods.BlitzFS_GetNodeByIndex(_engineHandle, nodeIndex);
                if (nodePtr != IntPtr.Zero)
                {
                    CompactNode node = Marshal.PtrToStructure<CompactNode>(nodePtr);
                    list.Add(node);
                }
            }

            return list;
        }

        /// <summary>
        /// 從 StringPool 中根據位元組偏移取得檔名字串 (零拷貝直讀)
        /// </summary>
        public unsafe string GetFileName(in CompactNode node)
        {
            if (_stringPoolPtr == IntPtr.Zero)
            {
                _stringPoolPtr = NativeMethods.BlitzFS_GetStringPoolPointer(_engineHandle);
            }

            if (_stringPoolPtr == IntPtr.Zero || node.NameOffset == 0 && node.NameLength == 0)
            {
                return string.Empty;
            }

            byte* basePtr = (byte*)_stringPoolPtr.ToPointer();
            char* strPtr = (char*)(basePtr + node.NameOffset);
            return new string(strPtr, 0, node.NameLength);
        }

        /// <summary>
        /// 根據 File ID 向上追溯重組完整絕對路徑
        /// </summary>
        public string ResolvePath(ulong fileId)
        {
            ThrowIfDisposed();

            char[] buffer = new char[512];
            bool success = NativeMethods.BlitzFS_ResolvePath(_engineHandle, fileId, buffer, (uint)buffer.Length);
            if (success)
            {
                return new string(buffer).TrimEnd('\0');
            }
            return string.Empty;
        }

        /// <summary>
        /// 啟動搬移或複製任務 (多工管線/同磁區瞬時/跨磁區 Direct I/O)
        /// </summary>
        public Task<bool> StartTransferAsync(
            string srcPath,
            string dstPath,
            bool isMove,
            IProgress<TransferProgressInfo>? progress = null,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            return Task.Run(() =>
            {
                using var registration = cancellationToken.Register(() =>
                {
                    CancelTransfer();
                });

                TransferProgressCallback? callback = null;
                if (progress != null)
                {
                    callback = (in TransferProgressInfo info, IntPtr _) =>
                    {
                        progress.Report(info);
                    };
                }

                return NativeMethods.BlitzFS_StartTransfer(_engineHandle, srcPath, dstPath, isMove, callback, IntPtr.Zero);
            }, cancellationToken);
        }

        public void CancelTransfer()
        {
            if (_engineHandle != IntPtr.Zero)
            {
                NativeMethods.BlitzFS_CancelTransfer(_engineHandle);
            }
        }

        public void PauseTransfer()
        {
            if (_engineHandle != IntPtr.Zero)
            {
                NativeMethods.BlitzFS_PauseTransfer(_engineHandle);
            }
        }

        public void ResumeTransfer()
        {
            if (_engineHandle != IntPtr.Zero)
            {
                NativeMethods.BlitzFS_ResumeTransfer(_engineHandle);
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(CoreEngineWrapper));
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                if (_engineHandle != IntPtr.Zero)
                {
                    NativeMethods.BlitzFS_DestroyEngine(_engineHandle);
                    _engineHandle = IntPtr.Zero;
                }
                _disposed = true;
            }
        }

        ~CoreEngineWrapper()
        {
            Dispose();
        }
    }
}
