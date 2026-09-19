using System;
using System.Threading;

namespace DshInstaller.Shared.Install
{
    /// <summary>单个步骤的状态。</summary>
    public enum InstallStepState
    {
        /// <summary>还没轮到。</summary>
        Pending,

        /// <summary>正在跑。</summary>
        Running,

        /// <summary>成功完成。</summary>
        Done,

        /// <summary>不需要做,跳过(例如组件已经装过)。</summary>
        Skipped,

        /// <summary>失败。</summary>
        Failed,
    }

    /// <summary>一步安装动作。</summary>
    public sealed class InstallStep
    {
        /// <summary>稳定标识,界面按它查本地化标题、日志也按它记录。</summary>
        public string Id { get; set; }

        /// <summary>界面上显示的名字(已本地化,由界面层填)。</summary>
        public string Title { get; set; }

        /// <summary>必需步骤失败就中止整条流程;可选步骤失败只记录、继续往下走。</summary>
        public bool Required { get; set; } = true;

        /// <summary>
        /// 这一步是不是"校验类"步骤。
        /// 补救重试之后需要重新校验一遍,靠这个标记挑出来。
        /// </summary>
        public bool IsVerifier { get; set; }

        /// <summary>实际动作。抛异常 = 这步失败。</summary>
        public Func<InstallContext, CancellationToken, System.Threading.Tasks.Task> Run { get; set; }
    }

    /// <summary>一步跑完的结果。</summary>
    public sealed class InstallStepResult
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public InstallStepState State { get; set; }

        /// <summary>给用户看的一句话说明(成功原因/失败原因)。</summary>
        public string Message { get; set; }

        /// <summary>补救重试了几次(0 = 没重试过)。</summary>
        public int Retries { get; set; }

        /// <summary>这步跑过几轮(主轮 + 补救轮)。</summary>
        public int Attempts { get; set; }
    }

    /// <summary>
    /// 步骤运行时能用的东西:选项、日志、进度上报、取消信号。
    ///
    /// 共享库不做 UI,所以这里的日志/进度都是回调,由界面层决定怎么写。
    /// </summary>
    public sealed class InstallContext
    {
        private readonly Action<string> _log;
        private readonly Action<string, double> _detail;

        public InstallContext(
            InstallOptions options,
            CancellationToken token,
            Action<string> log = null,
            Action<string, double> detail = null)
        {
            Options = options;
            Token = token;
            _log = log ?? delegate { };
            _detail = detail ?? delegate { };
        }

        public InstallOptions Options { get; private set; }

        public CancellationToken Token { get; private set; }

        /// <summary>
        /// 细节上报的落点。由执行器填 —— 它才知道"现在是第几步",
        /// 好把细节和步骤号绑在一起发出去。
        /// </summary>
        internal Action<string, double> DetailSink { get; set; }

        /// <summary>写一行日志(会出现在进度页的日志抽屉里)。</summary>
        public void Log(string message)
        {
            _log(message);
        }

        /// <summary>上报当前步骤的细节与百分比(0..100,-1 表示不确定进度)。</summary>
        public void Report(string detail, double percent = -1)
        {
            Action<string, double> sink = DetailSink;
            if (sink != null)
            {
                sink(detail, percent);
                return;
            }

            _detail(detail, percent);
        }

        // ---------------------------------------------------------------- 回滚线索
        // 取消安装时要能"把已经做过的撤回去"。关键是只能撤自己造的,
        // 用户原本就存在的目录不能动 —— 所以这里记的是"本安装创建的那些"。

        /// <summary>本次安装创建出来的顶层目录(原本不存在的才算)。</summary>
        public System.Collections.Generic.List<string> CreatedDirectories
        {
            get { return _createdDirectories; }
        }

        private readonly System.Collections.Generic.List<string> _createdDirectories =
            new System.Collections.Generic.List<string>();

        /// <summary>本次安装写进 PATH 的目录。</summary>
        public System.Collections.Generic.List<string> AddedPathEntries
        {
            get { return _addedPathEntries; }
        }

        private readonly System.Collections.Generic.List<string> _addedPathEntries =
            new System.Collections.Generic.List<string>();

        /// <summary>本次安装是否创建了快捷方式 / 注册了自启。</summary>
        public bool CreatedShortcut { get; set; }

        public bool RegisteredAutostart { get; set; }

        /// <summary>记一个"本安装创建的目录"。已存在的目录不要记,否则回滚会误删用户的东西。</summary>
        public void NoteCreatedDirectory(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                return;
            }

            string normalized = directory.TrimEnd('\\');
            for (int i = 0; i < _createdDirectories.Count; i++)
            {
                if (string.Equals(_createdDirectories[i], normalized, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            _createdDirectories.Add(normalized);
        }

        /// <summary>记一个写进 PATH 的目录。</summary>
        public void NotePathEntry(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                return;
            }

            _addedPathEntries.Add(directory.TrimEnd('\\'));
        }

        /// <summary>取消请求了就抛这个,执行器会认得。</summary>
        public void ThrowIfCancelled()
        {
            Token.ThrowIfCancellationRequested();
        }
    }

    /// <summary>整条流程的结果。</summary>
    public sealed class InstallReport
    {
        public bool Succeeded { get; set; }

        /// <summary>被取消(不算失败,但也没装完)。</summary>
        public bool Cancelled { get; set; }

        /// <summary>失败说明(整体性的,例如前置检查不过)。</summary>
        public string Error { get; set; }

        public System.Collections.Generic.List<InstallStepResult> Steps { get; set; } =
            new System.Collections.Generic.List<InstallStepResult>();

        /// <summary>成功/跳过/失败的步骤数,给完成页做小结。</summary>
        public int CountOf(InstallStepState state)
        {
            int n = 0;
            for (int i = 0; i < Steps.Count; i++)
            {
                if (Steps[i].State == state)
                {
                    n++;
                }
            }

            return n;
        }
    }
}
