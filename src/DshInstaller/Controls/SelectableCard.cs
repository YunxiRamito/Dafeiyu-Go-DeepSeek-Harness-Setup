using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;


namespace DshInstaller.Controls
{
    /// <summary>
    /// 可选中的卡片(替代 RadioButton)。
    ///
    /// 为什么不用 RadioButton:它的模板自带一个单选圆圈,即使用
    /// HorizontalContentAlignment=Stretch + Padding=0 把内容铺满,
    /// 那个圆圈仍然会画在左边、压在卡片上(第 1 页就是那样,很乱)。
    /// 自己画一个圆点在卡片内部,位置完全可控。
    ///
    /// 基类只能用 ContentControl —— WinUI3 的 Border 是密封类,不能继承。
    /// </summary>
    internal sealed class SelectableCard : ContentControl
    {
        private readonly Border _surface;
        private readonly Border _dot;
        private readonly Border _dotInner;
        private readonly TextBlock _title;
        private readonly TextBlock _description;
        private readonly TextBlock _path;

        private bool _selected;
        private bool _pointerOver;

        public SelectableCard()
        {
            HorizontalContentAlignment = HorizontalAlignment.Stretch;
            VerticalContentAlignment = VerticalAlignment.Stretch;
            Padding = new Thickness(0);
            BorderThickness = new Thickness(0);
            Background = null;

            _surface = new Border
            {
                CornerRadius = Theme.CornerRadius("CardCornerRadius", 10),
                Padding = new Thickness(16, 13, 16, 13),
                BorderThickness = new Thickness(1),
                Background = Theme.Brush("CardBackgroundBrush", Windows.UI.Color.FromArgb(255, 255, 255, 255)),
            };

            // 圆点用嵌套 Border 画,不用 Ellipse。
            // Ellipse 的描边画在边框中心线上,容器尺寸刚好等于直径时会被裁掉半边;
            // 之前还在容器和圆点上各加了一份 margin,结果渲染成"黑方块 / 缺口圆"。
            // Border + CornerRadius 没这个毛病,而且位置完全可控。
            _dotInner = new Border
            {
                Width = 8,
                Height = 8,
                CornerRadius = new CornerRadius(4),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Collapsed,
            };

            _dot = new Border
            {
                Width = 18,
                Height = 18,
                CornerRadius = new CornerRadius(9),
                BorderThickness = new Thickness(1.5),
                VerticalAlignment = VerticalAlignment.Top,
                Child = _dotInner,
            };

            _title = new TextBlock
            {
                FontSize = 15,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = Theme.Brush("PrimaryTextBrush", Windows.UI.Color.FromArgb(255, 15, 15, 16)),
                TextWrapping = TextWrapping.Wrap,
            };

            _description = new TextBlock
            {
                FontSize = 12.5,
                Margin = new Thickness(0, 3, 0, 0),
                TextWrapping = TextWrapping.Wrap,
                Foreground = Theme.Brush("SecondaryTextBrush", Windows.UI.Color.FromArgb(255, 92, 99, 110)),
            };

            _path = new TextBlock
            {
                FontSize = 11.5,
                Margin = new Thickness(0, 5, 0, 0),
                FontFamily = new FontFamily("Consolas"),
                TextWrapping = TextWrapping.Wrap,
                Foreground = Theme.Brush("TertiaryTextBrush", Windows.UI.Color.FromArgb(255, 138, 144, 153)),
            };

            StackPanel text = new StackPanel();
            text.Children.Add(_title);
            text.Children.Add(_description);
            text.Children.Add(_path);

            Grid layout = new Grid { ColumnSpacing = 12 };
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            // margin 只给外层容器一份,圆点自己不再加(加两次会把它挤出格子)
            _dot.Margin = new Thickness(0, 3, 0, 0);
            layout.Children.Add(_dot);
            Grid.SetColumn(text, 1);
            layout.Children.Add(text);

            _surface.Child = layout;
            Content = _surface;

            Theme.Bind(
                _title,
                TextBlock.ForegroundProperty,
                "PrimaryTextBrush",
                Windows.UI.Color.FromArgb(255, 15, 15, 16));
            Theme.Bind(
                _description,
                TextBlock.ForegroundProperty,
                "SecondaryTextBrush",
                Windows.UI.Color.FromArgb(255, 92, 99, 110));
            Theme.Bind(
                _path,
                TextBlock.ForegroundProperty,
                "TertiaryTextBrush",
                Windows.UI.Color.FromArgb(255, 138, 144, 153));

            ApplyTheme();
            ActualThemeChanged += delegate
            {
                ApplyTheme();
            };

            Tapped += OnTapped;
            PointerEntered += OnPointerEntered;
            PointerExited += OnPointerExited;
        }

        /// <summary>被选中时通知外部。</summary>
        public event Action<SelectableCard> Selected = delegate { };

        public string CardTitle
        {
            get { return _title.Text; }
            set { _title.Text = value; }
        }

        public string Description
        {
            get { return _description.Text; }
            set { _description.Text = value ?? string.Empty; }
        }

        public string CardPath
        {
            get { return _path.Text; }
            set
            {
                _path.Text = value ?? string.Empty;
                _path.Visibility = string.IsNullOrEmpty(value) ? Visibility.Collapsed : Visibility.Visible;
            }
        }

        public bool IsSelected
        {
            get { return _selected; }
            set
            {
                _selected = value;
                Refresh();
            }
        }

        /// <summary>只用来画边框和圆点,不触发事件。</summary>
        public void SetSelectedQuietly(bool value)
        {
            _selected = value;
            Refresh();
        }

        private void Refresh()
        {
            Brush accent = Theme.Brush("AccentBrush", Windows.UI.Color.FromArgb(255, 17, 17, 18));
            Brush onAccent = Theme.Brush("OnAccentBrush", Windows.UI.Color.FromArgb(255, 255, 255, 255));
            Brush idle = Theme.Brush("TertiaryTextBrush", Windows.UI.Color.FromArgb(255, 138, 144, 153));

            if (_selected)
            {
                _surface.BorderBrush = accent;
                _surface.BorderThickness = new Thickness(1.5);
                _dot.BorderBrush = accent;
                _dot.Background = accent;
                _dotInner.Background = onAccent;
                _dotInner.Visibility = Visibility.Visible;
            }
            else
            {
                _surface.BorderBrush = Theme.Brush("CardBorderBrush", Windows.UI.Color.FromArgb(22, 0, 0, 0));
                _surface.BorderThickness = new Thickness(1);
                _dot.BorderBrush = idle;
                _dot.Background = null;
                _dotInner.Visibility = Visibility.Collapsed;
            }
        }

        private void ApplyTheme()
        {
            _surface.Background = _pointerOver
                ? Theme.Brush("HoverBrush", Windows.UI.Color.FromArgb(15, 0, 0, 0))
                : Theme.Brush("CardBackgroundBrush", Windows.UI.Color.FromArgb(255, 255, 255, 255));
            Refresh();
        }

        private void OnTapped(object sender, TappedRoutedEventArgs e)
        {
            if (!_selected)
            {
                Selected(this);
            }
        }

        private void OnPointerEntered(object sender, PointerRoutedEventArgs e)
        {
            _pointerOver = true;
            if (!_selected)
            {
                _surface.Background = Theme.Brush("HoverBrush", Windows.UI.Color.FromArgb(15, 0, 0, 0));
            }
        }

        private void OnPointerExited(object sender, PointerRoutedEventArgs e)
        {
            _pointerOver = false;
            _surface.Background = Theme.Brush("CardBackgroundBrush", Windows.UI.Color.FromArgb(255, 255, 255, 255));
        }
    }
}
