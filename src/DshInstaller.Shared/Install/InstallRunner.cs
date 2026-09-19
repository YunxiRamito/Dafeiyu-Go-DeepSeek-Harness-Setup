using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace DshInstaller.Shared.Install
{
    /// <summary>
    /// 安装编排器:按顺序跑一串步骤,把每步的状态和细节报出去。
    ///
    /// 设计取舍:
    ///   - 顺序执行,不并发。步骤之间有依赖(先有 Node 才能 npm 装本体),
    ///     而且写同一个目录并发也会打架。
    ///   - **某步失败不立刻中止**:先把剩下的都跑一遍,能装多少装多少,
    ///     然后进入补救轮 —— 失败的那些重试几轮(网络抖动往往第二三次就好了)。
    ///   - 补救之后如果有变化,把校验类步骤重跑一遍,拿到真实结论。
    ///   - 不碰 UI:进度通过事件出去,由界面层 marshal 到 UI 线程。
    /// </summary>
    public sealed class InstallRunner
    {
        /// <summary>补救阶段最多重试几轮。</summary>
        public int RecoveryRounds = 3;

        private readonly List<InstallStep> _steps;
        private readonly InstallContext _context;

        public InstallRunner(IList<InstallStep> steps, InstallContext context)
        {
            if (steps == null)
            {
                throw new ArgumentNullException("steps");
            }

            if (context == null)
            {
                throw new ArgumentNullException("context");
            }

            _steps = new List<InstallStep>(steps);
            _context = context;
        }

        /// <summary>某一步进入新状态(索引, 新状态, 说明)。</summary>
        public event Action<int, InstallStepState, string> StepStateChanged = delegate { };

        /// <summary>某一步的细节更新(索引, 说明, 0..100)。</summary>
        public event Action<int, string, double> StepDetail = delegate { };

        public int StepCount
        {
            get { return _steps.Count; }
        }

        /// <summary>跑完整条流程。不会抛异常 —— 失败都体现在返回值里。</summary>
        public async Task<InstallReport> RunAsync()
        {
            InstallReport report = new InstallReport { Succeeded = true };

            // 先把所有步骤登记成 Pending,界面一进来就能把列表铺满
            for (int i = 0; i < _steps.Count; i++)
            {
                StepStateChanged(i, InstallStepState.Pending, null);
                report.Steps.Add(new InstallStepResult
                {
                    Id = _steps[i].Id,
                    Title = _steps[i].Title,
                    State = InstallStepState.Pending,
                });
            }

            string validation = _context.Options.Validate();
            if (!string.IsNullOrEmpty(validation))
            {
                report.Succeeded = false;
                report.Error = validation;
                _context.Log("参数检查不过:" + validation);
                return report;
            }

            _context.Log("安装开始,共 " + _steps.Count + " 个步骤"
                + (_context.Options.DryRun ? "(演练模式,不落盘)" : string.Empty));

            // ---------------------------------------------------------- 主轮
            for (int i = 0; i < _steps.Count; i++)
            {
                if (_context.Token.IsCancellationRequested)
                {
                    report.Cancelled = true;
                    report.Succeeded = false;
                    _context.Log("已取消,剩下的步骤不再执行");
                    break;
                }

                InstallStepState state = await RunStepAsync(i, report, null).ConfigureAwait(false);

                if (state == InstallStepState.Failed && _steps[i].Required)
                {
                    // 不立刻放弃:先把后面的跑完,补救轮再回来救它
                    _context.Log("必需步骤失败,先继续跑后面的,补救轮会回来重试:" + _steps[i].Title);
                }
            }

            // ---------------------------------------------------------- 补救轮
            if (!report.Cancelled && !_context.Token.IsCancellationRequested)
            {
                bool changed = await RunRecoveryAsync(report).ConfigureAwait(false);

                if (changed)
                {
                    // 有步骤被救回来了,校验结论就不准了 —— 重跑校验步骤
                    _context.Log("补救有进展,重新校验一遍");
                    for (int i = 0; i < _steps.Count; i++)
                    {
                        if (_steps[i].IsVerifier)
                        {
                            await RunStepAsync(i, report, null).ConfigureAwait(false);
                        }
                    }
                }
            }

            // ---------------------------------------------------------- 结算
            List<string> failedRequired = new List<string>();
            for (int i = 0; i < _steps.Count; i++)
            {
                if (_steps[i].Required && report.Steps[i].State == InstallStepState.Failed)
                {
                    failedRequired.Add(report.Steps[i].Title);
                }
            }

            if (report.Cancelled)
            {
                report.Succeeded = false;
            }
            else if (failedRequired.Count > 0)
            {
                report.Succeeded = false;
                report.Error = SharedText.T("以下必需步骤未能完成:", "These required steps could not be completed: ")
                    + " " + string.Join("、", failedRequired.ToArray());
                _context.Log("结算:有必需步骤没完成 -> " + string.Join("、", failedRequired.ToArray()));
            }
            else
            {
                report.Succeeded = true;
                int skipped = report.CountOf(InstallStepState.Skipped);
                if (skipped > 0)
                {
                    _context.Log("结算:全部必需步骤完成," + skipped + " 个可选步骤跳过");
                }
                else
                {
                    _context.Log("结算:全部完成");
                }
            }

            _context.Log(report.Succeeded ? "安装流程结束:成功" : "安装流程结束:未成功");
            return report;
        }

        /// <summary>
        /// 补救阶段:失败的那些步骤再试几轮。每轮按原顺序重试,一轮没有任何进展就停。
        /// </summary>
        private async Task<bool> RunRecoveryAsync(InstallReport report)
        {
            bool everChanged = false;

            for (int round = 1; round <= RecoveryRounds; round++)
            {
                List<int> failed = new List<int>();
                for (int i = 0; i < _steps.Count; i++)
                {
                    if (report.Steps[i].State == InstallStepState.Failed)
                    {
                        failed.Add(i);
                    }
                }

                if (failed.Count == 0)
                {
                    break;
                }

                if (_context.Token.IsCancellationRequested)
                {
                    break;
                }

                _context.Log("补救第 " + round + "/" + RecoveryRounds + " 轮:重试 " + failed.Count + " 个失败的步骤");

                bool changedThisRound = false;
                for (int k = 0; k < failed.Count; k++)
                {
                    int index = failed[k];

                    if (_context.Token.IsCancellationRequested)
                    {
                        break;
                    }

                    InstallStepState state = await RunStepAsync(index, report, round).ConfigureAwait(false);
                    if (state != InstallStepState.Failed)
                    {
                        changedThisRound = true;
                        everChanged = true;
                        _context.Log("补救成功:" + _steps[index].Title);
                    }
                }

                if (!changedThisRound)
                {
                    _context.Log("补救第 " + round + " 轮没有进展,不再重试");
                    break;
                }
            }

            return everChanged;
        }

        /// <summary>跑一步,并把结果写回 report。</summary>
        private async Task<InstallStepState> RunStepAsync(int i, InstallReport report, int? recoveryRound)
        {
            InstallStep step = _steps[i];
            InstallStepResult result = report.Steps[i];

            result.Attempts++;
            if (recoveryRound.HasValue)
            {
                result.Retries++;
            }

            result.State = InstallStepState.Running;
            StepStateChanged(i, InstallStepState.Running, null);

            if (recoveryRound.HasValue)
            {
                _context.Log("[补救 " + recoveryRound + "] " + step.Title);
            }
            else
            {
                _context.Log("[" + (i + 1) + "/" + _steps.Count + "] " + step.Title);
            }

            // 把"细节上报"接到带步骤号的对外事件上。
            // 不接的话步骤里调 Report() 会掉进空回调 —— 表现就是"只有总进度条在动,
            // 看不到下载的字节数和速度"(实测就是这个现象)。
            int captured = i;
            _context.DetailSink = delegate(string detail, double percent)
            {
                StepDetail(captured, detail, percent);
            };

            try
            {
                if (step.Run == null)
                {
                    result.State = InstallStepState.Skipped;
                    result.Message = "没有动作,按跳过处理";
                }
                else
                {
                    await step.Run(_context, _context.Token).ConfigureAwait(false);

                    if (result.State == InstallStepState.Running)
                    {
                        result.State = InstallStepState.Done;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                result.State = InstallStepState.Failed;
                result.Message = "已取消";
                report.Cancelled = true;
                StepStateChanged(i, result.State, result.Message);
                _context.Log("已取消:" + step.Title);
                return result.State;
            }
            catch (Exception exception)
            {
                result.State = InstallStepState.Failed;
                result.Message = Describe(exception);
            }

            StepStateChanged(i, result.State, result.Message);

            if (result.State == InstallStepState.Failed)
            {
                _context.Log("步骤失败:" + step.Title + " -> " + result.Message);
            }
            else if (result.State == InstallStepState.Skipped)
            {
                _context.Log("跳过:" + step.Title
                    + (string.IsNullOrEmpty(result.Message) ? string.Empty : "(" + result.Message + ")"));
            }

            return result.State;
        }

        private static string Describe(Exception exception)
        {
            if (exception == null)
            {
                return "(无异常信息)";
            }

            System.Text.StringBuilder builder = new System.Text.StringBuilder();
            builder.Append(exception.Message);

            Exception inner = exception.InnerException;
            int depth = 0;
            while (inner != null && depth < 3)
            {
                builder.Append(" <- ").Append(inner.Message);
                inner = inner.InnerException;
                depth++;
            }

            if (string.IsNullOrWhiteSpace(builder.ToString()))
            {
                builder.Append(exception.GetType().Name);
            }

            return builder.ToString();
        }
    }
}
