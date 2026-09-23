using System.Collections.Generic;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace DshInstaller.Controls
{
    /// <summary>
    /// 顶部步骤条。7 步:范围 / 检测 / 组件 / DSH位置 / 启动器位置 / 确认 / 安装。
    /// 它只表达"走到哪了",不负责跳转。
    /// </summary>
    public sealed partial class StepIndicator : UserControl
    {
        /// <summary>安装流程的默认格数。卸载流程会用 SetCount 换成 3 格。</summary>
        private const int DefaultStepCount = 9;
        private const double DotSize = 8;
        private const double ActiveWidth = 24;
        private const double ActiveHeight = 8;

        private readonly List<Border> _dots = new List<Border>();
        private int _step;
        private int _count = DefaultStepCount;

        public StepIndicator()
        {
            InitializeComponent();
            Build();
            ActualThemeChanged += delegate
            {
                Refresh();
            };
        }

        /// <summary>
        /// 重新画成 count 格。安装 7 格、卸载 3 格 —— 让步骤条如实反映当前流程,
        /// 不然卸载页上还挂着安装流程的 7 个点,用户会以为后面还有一堆步骤。
        /// </summary>
        public void SetCount(int count)
        {
            if (count < 1 || count == _count)
            {
                return;
            }

            _count = count;
            Host.Children.Clear();
            _dots.Clear();
            Build();
        }

        private void Build()
        {
            for (int index = 0; index < _count; index++)
            {
                Border dot = new Border
                {
                    Width = DotSize,
                    Height = DotSize,
                    CornerRadius = new CornerRadius(DotSize / 2),
                    VerticalAlignment = VerticalAlignment.Center,
                };

                _dots.Add(dot);
                Host.Children.Add(dot);
            }

            Refresh();
        }

        public void SetStep(int index)
        {
            if (index < 0)
            {
                index = 0;
            }

            if (index > _count - 1)
            {
                index = _count - 1;
            }

            _step = index;
            Refresh();
        }

        private void Refresh()
        {
            for (int index = 0; index < _dots.Count; index++)
            {
                Border dot = _dots[index];
                if (index == _step)
                {
                    dot.Width = ActiveWidth;
                    dot.Height = ActiveHeight;
                    dot.CornerRadius = new CornerRadius(ActiveHeight / 2);
                    dot.Background = Brush("AccentBrush", Windows.UI.Color.FromArgb(255, 77, 107, 254));
                }
                else if (index < _step)
                {
                    dot.Width = DotSize;
                    dot.Height = DotSize;
                    dot.CornerRadius = new CornerRadius(DotSize / 2);
                    dot.Background = Brush("SecondaryTextBrush", Windows.UI.Color.FromArgb(255, 92, 99, 110));
                    dot.Opacity = 0.55;
                }
                else
                {
                    dot.Width = DotSize;
                    dot.Height = DotSize;
                    dot.CornerRadius = new CornerRadius(DotSize / 2);
                    dot.Background = Brush("TertiaryTextBrush", Windows.UI.Color.FromArgb(255, 138, 144, 153));
                    dot.Opacity = 0.3;
                }
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
