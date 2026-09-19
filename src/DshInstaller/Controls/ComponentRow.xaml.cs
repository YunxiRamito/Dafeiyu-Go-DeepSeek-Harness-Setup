using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using DshInstaller.Shared;

namespace DshInstaller.Controls
{
    /// <summary>组件行的状态。</summary>
    public enum RowState
    {
        /// <summary>待检测</summary>
        Pending,
        /// <summary>检查中(转圈)</summary>
        Busy,
        /// <summary>已就绪</summary>
        Ready,
        /// <summary>缺失,需要装</summary>
        Missing,
        /// <summary>可选,不装也能用</summary>
        Optional,
        /// <summary>出问题了</summary>
        Error,
    }

    /// <summary>一行组件展示。</summary>
    public sealed partial class ComponentRow : UserControl
    {
        public ComponentRow()
        {
            InitializeComponent();
        }

        /// <summary>紧凑模式:缩小上下留白,列表长的时候能多塞几行。</summary>
        public void SetCompact(bool compact)
        {
            // 注意:留白在内层 RootGrid 上,不是 UserControl 自己的 Padding。
            // 改错地方的话"紧凑模式"反而会多出一圈空白。
            RootGrid.Padding = compact ? new Thickness(0, 5, 0, 5) : new Thickness(0, 9, 0, 9);
            NameText.FontSize = compact ? 12.5 : 13.5;
            DetailText.FontSize = compact ? 10.5 : 11.5;
            StateText.FontSize = compact ? 11.5 : 12;

            // 说明文字一律单行,超出省略 —— 换行会把整行撑高、两列就对不齐了
            DetailText.TextTrimming = TextTrimming.CharacterEllipsis;
            DetailText.TextWrapping = TextWrapping.NoWrap;
        }

        /// <summary>
        /// 单行模式:藏掉说明行、压到最扁。
        /// 进度页有 7 条任务,双行样式一屏放不下,会被裁掉最后两条。
        /// </summary>
        public void SetSingleLine()
        {
            SetCompact(true);
            _singleLine = true;
            RootGrid.Padding = new Thickness(0, 3, 0, 3);
            DetailText.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// 组件名(显示用)。**故意遮蔽** FrameworkElement.Name —— 这个控件是在代码里
        /// new 出来的、从不用 x:Name,所以不需要那个基类属性;命名成 Name 调用处读着最顺。
        /// </summary>
        public new string Name
        {
            get { return NameText.Text; }
            set { NameText.Text = value; }
        }

        /// <summary>说明文字。设成单行模式后不会被显示出来。</summary>
        public string Detail
        {
            get { return DetailText.Text; }
            set
            {
                DetailText.Text = value ?? string.Empty;
                DetailText.Visibility = _singleLine || string.IsNullOrEmpty(value)
                    ? Visibility.Collapsed
                    : Visibility.Visible;
            }
        }

        private bool _singleLine;

        /// <summary>右侧状态文字。</summary>
        /// <summary>
        /// 只更新右侧那行状态文字,**不重建视觉树**。
        ///
        /// 为什么要单独开一个方法:进度页每刷新一次细节(下载字节数之类,一秒好几次)
        /// 如果走 SetState,整行会被重建 —— 那个 ProgressRing 换成新的实例,
        /// 动画从头播一遍,看起来就是"圆圈每隔一秒抽搐一下"(实测踩过)。
        /// </summary>
        public void SetStatusText(string text)
        {
            StateText.Text = text ?? string.Empty;
        }

        public void SetState(RowState state, string text, string version = null)
        {
            Busy.IsActive = false;
            Busy.Visibility = Visibility.Collapsed;
            StateIcon.Visibility = Visibility.Visible;

            string suffix = string.IsNullOrEmpty(version) ? string.Empty : "  " + ShortVersion(version);

            switch (state)
            {
                case RowState.Busy:
                    StateIcon.Visibility = Visibility.Collapsed;
                    Busy.Visibility = Visibility.Visible;
                    Busy.IsActive = true;
                    StateText.Text = text ?? string.Empty;
                    StateText.Foreground = Brush("TertiaryTextBrush", Windows.UI.Color.FromArgb(255, 138, 144, 153));
                    break;

                case RowState.Ready:
                    StateIcon.Glyph = "\uE73E";   // 对勾
                    StateIcon.Foreground = Brush("SuccessBrush", Windows.UI.Color.FromArgb(255, 46, 158, 91));
                    StateText.Text = (text ?? Localization.T("state.ready")) + suffix;
                    StateText.Foreground = Brush("SecondaryTextBrush", Windows.UI.Color.FromArgb(255, 92, 99, 110));
                    break;

                case RowState.Missing:
                    StateIcon.Glyph = "\uE7BA";   // 警告
                    StateIcon.Foreground = Brush("WarningBrush", Windows.UI.Color.FromArgb(255, 217, 138, 0));
                    StateText.Text = text ?? Localization.T("state.missing");
                    StateText.Foreground = Brush("WarningBrush", Windows.UI.Color.FromArgb(255, 217, 138, 0));
                    break;

                case RowState.Optional:
                    StateIcon.Glyph = "\uE738";   // 圆圈
                    StateIcon.Foreground = Brush("TertiaryTextBrush", Windows.UI.Color.FromArgb(255, 138, 144, 153));
                    StateText.Text = text ?? Localization.T("state.optional");
                    StateText.Foreground = Brush("TertiaryTextBrush", Windows.UI.Color.FromArgb(255, 138, 144, 153));
                    break;

                case RowState.Error:
                    StateIcon.Glyph = "\uEA39";   // 叉
                    StateIcon.Foreground = Brush("DangerBrush", Windows.UI.Color.FromArgb(255, 214, 69, 69));
                    StateText.Text = text ?? Localization.T("state.error");
                    StateText.Foreground = Brush("DangerBrush", Windows.UI.Color.FromArgb(255, 214, 69, 69));
                    break;

                default:
                    StateIcon.Glyph = "\uE738";
                    StateIcon.Foreground = Brush("TertiaryTextBrush", Windows.UI.Color.FromArgb(255, 138, 144, 153));
                    StateText.Text = text ?? string.Empty;
                    StateText.Foreground = Brush("TertiaryTextBrush", Windows.UI.Color.FromArgb(255, 138, 144, 153));
                    break;
            }
        }

        /// <summary>版本号去掉冗长的前缀(探测结果里有时是 "git version 2.50.1")。</summary>
        private static string ShortVersion(string version)
        {
            if (string.IsNullOrEmpty(version))
            {
                return version;
            }

            string[] prefixes = { "git version ", "v", "Version " };
            string result = version.Trim();
            for (int index = 0; index < prefixes.Length; index++)
            {
                if (result.StartsWith(prefixes[index], StringComparison.OrdinalIgnoreCase))
                {
                    result = result.Substring(prefixes[index].Length).Trim();
                    break;
                }
            }

            // 只保留 "2.50.1.windows.1" 这种主体,太长的后面砍掉
            if (result.Length > 18)
            {
                result = result.Substring(0, 18) + "…";
            }

            return result;
        }

        /// <summary>把检测结果直接喂进来。</summary>
        public void ApplyStatus(ComponentStatus status)
        {
            if (status == null)
            {
                SetState(RowState.Optional, null);
                return;
            }

            Name = status.DisplayName;
            Detail = status.Notes;

            switch (status.State)
            {
                case DetectState.Ready:
                    SetState(RowState.Ready, null, status.DetectedVersion);
                    break;
                case DetectState.Outdated:
                    SetState(RowState.Missing, Localization.IsChinese ? "版本过低" : "Version too old", status.DetectedVersion);
                    break;
                case DetectState.Missing:
                    SetState(RowState.Missing, null);
                    break;
                case DetectState.Unknown:
                    SetState(RowState.Error, Localization.IsChinese ? "无法检测" : "Cannot detect");
                    break;
                default:
                    SetState(
                        status.Required ? RowState.Missing : RowState.Optional,
                        status.Required ? null : (Localization.IsChinese ? "可选" : "Optional"),
                        status.DetectedVersion);
                    break;
            }
        }

        private static Brush Brush(string key, Windows.UI.Color fallback)
        {
            try
            {
                object value = Application.Current.Resources[key];
                Brush brush = value as Brush;
                if (brush != null)
                {
                    return brush;
                }
            }
            catch
            {
            }

            return new SolidColorBrush(fallback);
        }
    }
}
