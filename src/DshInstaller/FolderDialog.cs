using System;
using System.Runtime.InteropServices;

namespace DshInstaller
{
    /// <summary>
    /// 选文件夹。
    ///
    /// 为什么不用 WinUI 的 FolderPicker:未打包应用里它得先用 IInitializeWithWindow 绑窗口句柄,
    /// 在 1809 上还时不时直接抛 —— 界面上那句"文件夹选择器出现问题,请稍后重试"就是当初留下的
    /// 占位提示(其实压根没实现)。
    ///
    /// 既然只需要"选个目录",直接调系统的 IFileOpenDialog + FOS_PICKFOLDERS 最稳:
    /// Vista 以后都是同一个对话框,不依赖 Windows App SDK 的任何封装。
    /// </summary>
    internal static class FolderDialog
    {
        private const uint FOS_PICKFOLDERS = 0x00000020;
        private const uint FOS_FORCEFILESYSTEM = 0x00000040;
        private const uint FOS_PATHMUSTEXIST = 0x00000800;

        /// <summary>SIGDN_FILESYSPATH:要"文件系统路径"而不是显示名。</summary>
        private const uint SIGDN_FILESYSPATH = 0x80058000;

        /// <summary>用户点了取消(ERROR_CANCELLED)。</summary>
        private const int Cancelled = unchecked((int)0x800704C7);

        public static string Pick(string title, string currentPath)
        {
            try
            {
                IFileOpenDialog dialog = (IFileOpenDialog)new FileOpenDialog();

                // 只能选目录 + 必须真实存在
                dialog.SetOptions(FOS_PICKFOLDERS | FOS_FORCEFILESYSTEM | FOS_PATHMUSTEXIST);

                if (!string.IsNullOrWhiteSpace(title))
                {
                    dialog.SetTitle(title);
                }

                // 已经有路径的话,把对话框定位到那儿,省得用户从头点
                if (!string.IsNullOrWhiteSpace(currentPath))
                {
                    try
                    {
                        Guid shellItemGuid = typeof(IShellItem).GUID;
                        IShellItem folder = SHCreateItemFromParsingName(currentPath, IntPtr.Zero, ref shellItemGuid);
                        if (folder != null)
                        {
                            dialog.SetFolder(folder);
                        }
                    }
                    catch
                    {
                        // 路径不存在/格式怪 —— 无所谓,让对话框自己决定初始位置
                    }
                }

                IntPtr owner = IntPtr.Zero;
                try
                {
                    if (App.MainWindowInstance != null)
                    {
                        owner = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindowInstance);
                    }
                }
                catch
                {
                }

                int hr = dialog.Show(owner);
                if (hr == Cancelled)
                {
                    return null;
                }

                if (hr != 0)
                {
                    return null;
                }

                IShellItem result = null;
                dialog.GetResult(out result);
                if (result == null)
                {
                    return null;
                }

                IntPtr buffer;
                result.GetDisplayName(SIGDN_FILESYSPATH, out buffer);
                if (buffer == IntPtr.Zero)
                {
                    return null;
                }

                try
                {
                    return Marshal.PtrToStringUni(buffer);
                }
                finally
                {
                    Marshal.FreeCoTaskMem(buffer);
                }
            }
            catch
            {
                return null;
            }
        }

        // ---------------------------------------------------------------- COM 声明

        [ComImport]
        [ClassInterface(ClassInterfaceType.None)]
        [Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")]
        private class FileOpenDialog
        {
        }

        /// <summary>
        /// IFileOpenDialog。**方法顺序不能乱** —— COM 接口是按 vtable 槽位调用的,
        /// 少写一个或者写错位置,后面所有方法都会指到别的地方去(表现是莫名其妙的崩溃)。
        /// 我们只用到 SetOptions / SetTitle / SetFolder / Show / GetResult,
        /// 但前面的一个都不能省。
        /// </summary>
        [ComImport]
        [Guid("42f85136-db7e-439c-85f1-e4075d135fc8")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFileOpenDialog
        {
            [PreserveSig]
            int Show(IntPtr parent);

            void SetFileTypes(uint cFileTypes, IntPtr rgFilterSpec);
            void SetFileTypeIndex(uint iFileType);
            void GetFileTypeIndex(out uint piFileType);
            void Advise(IntPtr pfde, out uint pdwCookie);
            void Unadvise(uint dwCookie);
            void SetOptions(uint fos);
            void GetOptions(out uint pfos);
            void SetDefaultFolder(IShellItem psi);
            void SetFolder(IShellItem psi);
            void GetFolder(out IShellItem ppsi);
            void GetCurrentSelection(out IShellItem ppsi);
            void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string pszName);
            void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
            void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);
            void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
            void GetResult(out IShellItem ppsi);
            void AddPlace(IShellItem psi, int fdap);
            void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);
            void Close(int hr);
            void SetClientGuid(ref Guid guid);
            void ClearClientData();
            void SetFilter(IntPtr pFilter);
        }

        [ComImport]
        [Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItem
        {
            void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
            void GetParent(out IShellItem ppsi);
            void GetDisplayName(uint sigdnName, out IntPtr ppszName);
            void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
            void Compare(IShellItem psi, uint hint, out int piOrder);
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        private static extern IShellItem SHCreateItemFromParsingName(
            [MarshalAs(UnmanagedType.LPWStr)] string pszPath, IntPtr pbc, ref Guid riid);
    }
}
