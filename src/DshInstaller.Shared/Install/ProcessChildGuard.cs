using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DshInstaller.Shared.Install
{
    /// <summary>
    /// 让本进程启动的子进程"陪着一起死"。
    ///
    /// 为什么需要它:Windows **不会**在父进程退出时顺带杀掉子进程。
    /// 安装器被中断(用户关窗口 / 崩溃 / 被 taskkill)时,DSH 那一步已经拉起来的
    /// npm install 会继续在后台跑 —— 实测留下过 5 个 node 同时往同一个目录里写、
    /// 吃掉快 1.4 GB 内存,而且互相踩得一塌糊涂(用户报"后台为什么这么多 node")。
    ///
    /// 作业对象是 Win32 给这件事的标准答案:进程挂进去之后,只要作业句柄一关
    /// (本进程无论怎么死,句柄都会关),整棵树一起被系统收掉。
    /// </summary>
    internal static class ProcessChildGuard
    {
        private const int InfoClassExtendedLimit = 9;
        private const uint JobObjectLimitKillOnJobClose = 0x00002000;

        private static readonly object Gate = new object();
        private static IntPtr _job = IntPtr.Zero;

        /// <summary>把一个刚启动的子进程挂进"陪葬"作业。挂不上也不抛 —— 这只是兜底。</summary>
        public static void Attach(Process process)
        {
            if (process == null)
            {
                return;
            }

            try
            {
                lock (Gate)
                {
                    if (_job == IntPtr.Zero)
                    {
                        _job = CreateJobObject(IntPtr.Zero, null);
                        if (_job == IntPtr.Zero)
                        {
                            return;
                        }

                        JobObjectExtendedLimitInformation info = new JobObjectExtendedLimitInformation();
                        info.BasicLimitInformation.LimitFlags = JobObjectLimitKillOnJobClose;

                        int length = Marshal.SizeOf(typeof(JobObjectExtendedLimitInformation));
                        IntPtr buffer = Marshal.AllocHGlobal(length);
                        try
                        {
                            Marshal.StructureToPtr(info, buffer, false);
                            SetInformationJobObject(_job, InfoClassExtendedLimit, buffer, (uint)length);
                        }
                        finally
                        {
                            Marshal.FreeHGlobal(buffer);
                        }
                    }

                    AssignProcessToJobObject(_job, process.Handle);
                }
            }
            catch
            {
            }
        }

        // ---------------------------------------------------------------- Win32

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateJobObject(IntPtr attributes, string name);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetInformationJobObject(
            IntPtr job, int infoClass, IntPtr info, uint length);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

        [StructLayout(LayoutKind.Sequential)]
        private struct JobObjectBasicLimitInformation
        {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize;
            public UIntPtr MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass;
            public uint SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IoCounters
        {
            public ulong ReadOperationCount;
            public ulong WriteOperationCount;
            public ulong OtherOperationCount;
            public ulong ReadTransferCount;
            public ulong WriteTransferCount;
            public ulong OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JobObjectExtendedLimitInformation
        {
            public JobObjectBasicLimitInformation BasicLimitInformation;
            public IoCounters IoInfo;
            public UIntPtr ProcessMemoryLimit;
            public UIntPtr JobMemoryLimit;
            public UIntPtr PeakProcessMemoryUsed;
            public UIntPtr PeakJobMemoryUsed;
        }
    }
}
