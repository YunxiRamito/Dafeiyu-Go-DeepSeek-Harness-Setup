using System;
using System.Collections.Generic;

namespace DshInstaller.Shared
{
    /// <summary>单个组件的检测状态。</summary>
    public enum DetectState
    {
        /// <summary>本机已有,版本达标。</summary>
        Ready,

        /// <summary>装了但版本太低,需要处理。</summary>
        Outdated,

        /// <summary>没装。</summary>
        Missing,

        /// <summary>没装但不影响使用(可选组件)。</summary>
        Optional,

        /// <summary>查不了(权限、命令超时等)。</summary>
        Unknown,
    }

    /// <summary>一个组件的检测结果。</summary>
    public sealed class ComponentStatus
    {
        public string Id { get; set; }

        /// <summary>界面显示名,例如 "Node.js"。</summary>
        public string DisplayName { get; set; }

        /// <summary>本机检测到的可执行文件全路径,没有就是 null。</summary>
        public string Path { get; set; }

        /// <summary>检测到的版本原文,例如 "v22.23.2"。</summary>
        public string DetectedVersion { get; set; }

        /// <summary>版本是否达标。</summary>
        public bool VersionOk { get; set; }

        public DetectState State { get; set; }

        /// <summary>是否必须。必需项缺失就进不了下一步。</summary>
        public bool Required { get; set; }

        /// <summary>缺失时安装器能不能自己装。</summary>
        public bool Installable { get; set; }

        /// <summary>给用户看的一句话说明。</summary>
        public string Notes { get; set; }

        /// <summary>
        /// 这个组件是不是"已经满足了"。
        ///
        /// 注意 <see cref="DetectState.Optional"/> 的语义是**没装**(只是不影响使用),
        /// 绝不能算满足 —— 早先把它一并当成满足,结果可选组件没装也被标成"已安装"、
        /// 复选框还被禁用了,用户根本没法选着装(实测踩过)。
        /// </summary>
        public bool IsSatisfied
        {
            get { return State == DetectState.Ready; }
        }

        public override string ToString()
        {
            return DisplayName + " = " + State + " (" + (DetectedVersion ?? "?") + ")";
        }
    }

    /// <summary>整机体检报告。</summary>
    public sealed class ProbeReport
    {
        public int WindowsBuild { get; set; }
        public string WindowsName { get; set; }
        public string WindowsDisplayVersion { get; set; }
        public bool IsWindowsSupported { get; set; }

        /// <summary>当前进程是不是管理员。</summary>
        public bool IsElevated { get; set; }

        public List<ComponentStatus> Components { get; } = new List<ComponentStatus>();

        public ComponentStatus this[string id]
        {
            get
            {
                for (int index = 0; index < Components.Count; index++)
                {
                    if (string.Equals(Components[index].Id, id, StringComparison.OrdinalIgnoreCase))
                    {
                        return Components[index];
                    }
                }

                return null;
            }
        }

        /// <summary>必需组件是否全部就绪。</summary>
        public bool AllRequiredReady
        {
            get
            {
                for (int index = 0; index < Components.Count; index++)
                {
                    if (Components[index].Required && !Components[index].IsSatisfied)
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        /// <summary>必需组件里缺失且能装的清单。</summary>
        public List<ComponentStatus> PendingRequired()
        {
            List<ComponentStatus> pending = new List<ComponentStatus>();
            for (int index = 0; index < Components.Count; index++)
            {
                ComponentStatus item = Components[index];
                if (item.Required && !item.IsSatisfied)
                {
                    pending.Add(item);
                }
            }

            return pending;
        }
    }
}
