using System;
using System.Runtime.InteropServices;

namespace BlitzFS.Bridge
{
    /// <summary>
    /// BlitzFS.Core.dll C-ABI 原生接口宣告 (P/Invoke)
    /// </summary>
    public static class NativeMethods
    {
        public const string DllName = "BlitzFS.Core.dll";

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr BlitzFS_CreateEngine();

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void BlitzFS_DestroyEngine(IntPtr engineHandle);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        [return: MarshalAs(UnmanagedType.I1)]
        public static extern bool BlitzFS_ScanVolume(
            IntPtr engineHandle,
            char driveLetter,
            ScanProgressCallback? callback,
            IntPtr userData
        );

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern uint BlitzFS_GetNodeCount(IntPtr engineHandle);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern ulong BlitzFS_GetRootFileId(IntPtr engineHandle);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        public static extern ulong BlitzFS_FindNodeByPath(IntPtr engineHandle, string path);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern uint BlitzFS_GetChildren(
            IntPtr engineHandle,
            ulong parentId,
            out IntPtr outNodeIndices
        );

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr BlitzFS_GetNodeByIndex(
            IntPtr engineHandle,
            uint nodeIndex
        );

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr BlitzFS_GetNodeById(
            IntPtr engineHandle,
            ulong fileId
        );

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr BlitzFS_GetAllNodes(
            IntPtr engineHandle,
            out uint outCount
        );

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr BlitzFS_GetStringPoolPointer(IntPtr engineHandle);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.I1)]
        public static extern bool BlitzFS_ResolvePath(
            IntPtr engineHandle,
            ulong fileId,
            [Out] char[] outPathBuffer,
            uint maxLen
        );

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.I1)]
        public static extern bool BlitzFS_StartTransfer(
            IntPtr engineHandle,
            string srcPath,
            string dstPath,
            [MarshalAs(UnmanagedType.I1)] bool isMove,
            TransferProgressCallback? callback,
            IntPtr userData
        );

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void BlitzFS_CancelTransfer(IntPtr engineHandle);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void BlitzFS_PauseTransfer(IntPtr engineHandle);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void BlitzFS_ResumeTransfer(IntPtr engineHandle);
    }
}
