using System;
using System.Collections.Generic;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;
using Windows.Graphics;
using WinRT;
using WinRT.Interop;

namespace DshInstaller
{
    /// <summary>向导页的标识,顺序就是流程顺序。</summary>
    internal enum WizardPage
    {
        Welcome = 0,
        Source,
        Scope,
        Detect,

        /// <summary>先问 DSH 装哪 —— 组件目录的默认值是它的子目录,所以它得排在前面。</summary>
        DshLocation,
        Components,
        RecommendedPlugins,
        LauncherLocation,
        Confirm,
        Progress,
        Done,

        /// <summary>卸载确认页(卸载模式才用)。</summary>
        Uninstall,

        /// <summary>安装未完成页(失败或取消,且已回滚)。</summary>
        Failed,

        /// <summary>卸载第二页:选择要删哪些、留哪些。</summary>
        UninstallItems,
    }

    /// <summary>
    /// 主窗口。
    ///
    /// 刻意用纯代码搭界面,不走 XAML:
    /// 这个 SDK 版本下,XAML 编译器会把 Window 根元素的 xaml 同时算进 XamlApplications 与 XamlPages,
    /// 结果根本不给它生成代码(g.cs 是空的),排查成本远高于手写这点布局。
    /// </summary>
    public sealed class MainWindow : Window
    {
        private sealed class PageInfo
        {
            public Type Type;
            public bool ShowBack = true;
            public bool ShowNext = true;
            public string NextText = "下一步";
            public string Hint = string.Empty;

            /// <summary>点"下一步"时跳哪。默认下一页,可被页面覆盖。</summary>
            /// <summary>
        /// 注册时就定死的"下一页"。**不会**用一次就失效 ——
        /// 否则用户"下一步 → 上一步 → 下一步"时第二次就会退化成按序号 +1,
        /// 跳到毫不相干的页面(实测:卸载确认页会跳到失败页)。
        /// </summary>
        public WizardPage? NextOverride;

        /// <summary>
        /// 运行时动态决定的"下一页",用一次即失效(检测页按结果跳页用)。
        /// </summary>
        public WizardPage? DynamicNext;

        /// <summary>
        /// "上一步"的目标。不填就按枚举序号 -1 算 ——
        /// 但枚举里 Failed / UninstallItems 排在后面,序号对不上,
        /// 于是"上一步"会跳到失败页去(实测踩过)。卸载流程必须显式指定。
        /// </summary>
        public WizardPage? BackOverride;
        }

        private readonly Dictionary<WizardPage, PageInfo> _pages =
            new Dictionary<WizardPage, PageInfo>();

        private Frame _contentFrame;
        private Button _backButton;
        private Button _nextButton;
        private TextBlock _footerHint;
        private TextBlock _languageLabel;
        private Button _languageButton;
        private Button _actionButton;
        private IWizardPageFooterAction _footerAction;

        private bool _dragging;
        private int _dragStartCursorX;
        private int _dragStartCursorY;
        private int _dragStartWindowX;
        private int _dragStartWindowY;
        private Grid _titleBarGrid;
        private Grid _dragArea;
        private Grid _captionRow;
        private Button _minimizeButton;
        private Button _closeButton;
        private FontIcon _minimizeIcon;
        private FontIcon _closeIcon;
        private TextBlock _titleText;
        private Border _brandBadge;
        private Path _brandMark;
        private FontIcon _languageIcon;
        private Border _footerBorder;
        private Grid _shellRoot;

        private MicaController _micaController;
        private DesktopAcrylicController _acrylicController;
        private SystemBackdropConfiguration _backdropConfiguration;
        private bool _backdropClosed;

        private WizardPage _current = WizardPage.Welcome;
        private bool _navigating;

        public MainWindow()
        {
            RegisterPages();
            BuildUi();
            ConfigureWindow();
            WizardPage start = WizardPage.Welcome;
            if (DevOptions.StartPage.HasValue)
            {
                start = (WizardPage)DevOptions.StartPage.Value;
            }
            else if (DevOptions.Uninstall)
            {
                start = WizardPage.Uninstall;
            }

            Navigate(start, null);
        }

        private void RegisterPages()
        {
            _pages[WizardPage.Welcome] = new PageInfo
            {
                Type = typeof(Pages.WelcomePage),
                ShowBack = false,
                NextText = "开始安装",
            };
            _pages[WizardPage.Source] = new PageInfo
            {
                Type = typeof(Pages.SourcePage),
                NextText = "继续",
            };
            _pages[WizardPage.Scope] = new PageInfo { Type = typeof(Pages.ScopePage) };
            _pages[WizardPage.Detect] = new PageInfo
            {
                Type = typeof(Pages.DetectPage),
                NextText = "继续",
            };
            _pages[WizardPage.Components] = new PageInfo { Type = typeof(Pages.ComponentsPage) };
            _pages[WizardPage.DshLocation] = new PageInfo { Type = typeof(Pages.DshLocationPage) };
            _pages[WizardPage.RecommendedPlugins] = new PageInfo
            {
                Type = typeof(Pages.RecommendedPluginsPage),
            };
            _pages[WizardPage.LauncherLocation] = new PageInfo { Type = typeof(Pages.LauncherLocationPage) };
            _pages[WizardPage.Confirm] = new PageInfo
            {
                Type = typeof(Pages.ConfirmPage),
                NextText = "开始安装",
            };
            _pages[WizardPage.Progress] = new PageInfo
            {
                Type = typeof(Pages.ProgressPage),
                ShowBack = false,
                ShowNext = false,
            };
            _pages[WizardPage.Done] = new PageInfo
            {
                Type = typeof(Pages.DonePage),
                ShowBack = false,
                NextText = "完成",
            };

            // 安装未完成页:回滚之后落到这里。只有一个"关闭",不给"下一步"。
            _pages[WizardPage.Failed] = new PageInfo
            {
                Type = typeof(Pages.FailedPage),
                ShowBack = false,
                ShowNext = true,
                NextText = "关闭",
            };

            // 卸载流程三页:确定卸载 -> 选择删哪些 -> (进度) -> 卸载完成
            _pages[WizardPage.Uninstall] = new PageInfo
            {
                Type = typeof(Pages.UninstallPage),
                ShowBack = false,
                NextText = "下一步",

                // 必须显式指定:默认"下一页"是按枚举序号 +1 算的,
                // 而 Failed / UninstallItems 排在枚举后面,序号对不上。
                NextOverride = WizardPage.UninstallItems,
            };

            _pages[WizardPage.UninstallItems] = new PageInfo
            {
                Type = typeof(Pages.UninstallItemsPage),
                ShowBack = true,
                NextText = "卸载",
                NextOverride = WizardPage.Progress,

                // 显式指回去:否则"上一步"会按序号减一跳到失败页
                BackOverride = WizardPage.Uninstall,
            };
        }

        // ---------------------------------------------------------------- 界面

        private void BuildUi()
        {
            _shellRoot = new Grid
            {
                Background = Theme.Brush("ShellBackgroundBrush", Windows.UI.Color.FromArgb(255, 244, 246, 250)),
                RequestedTheme = ElementTheme.Default,
            };
            _shellRoot.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _shellRoot.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            _shellRoot.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            _shellRoot.Children.Add(BuildTitleBar());

            _contentFrame = new Frame();
            Grid.SetRow(_contentFrame, 1);
            _shellRoot.Children.Add(_contentFrame);

            Border footer = BuildFooter();
            Grid.SetRow(footer, 2);
            _shellRoot.Children.Add(footer);

            _shellRoot.ActualThemeChanged += OnShellActualThemeChanged;
            Content = _shellRoot;
        }

        /// <summary>
        /// 自绘标题栏。
        ///
        /// 系统那三个按钮的处理:不去改窗口样式(Win10 1809 上没用,照样画一个灰的最大化),
        /// 而是把它们的**前景和背景一起设成透明** —— 看不见了,但 Alt+F4、任务栏右键关闭
        /// 这些系统行为都还保留着。然后在同一片区域叠上自己画的"最小化 / 关闭"。
        ///
        /// 结构要点:_dragArea 和按钮是**兄弟节点**,按钮叠在上面;
        /// 拖拽交给窗口过程(WM_NCHITTEST 返回 HTCAPTION),按钮所在的右上角要排除在外。
        /// </summary>
        private UIElement BuildTitleBar()
        {
            _titleBarGrid = new Grid { Height = TitleBarHeight };

            // ---- 拖拽区:品牌标识 ----
            _dragArea = new Grid
            {
                Height = TitleBarHeight,
                Padding = new Thickness(14, 0, 0, 0),
            };

            Grid brand = new Grid
            {
                ColumnSpacing = 9,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            brand.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            brand.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _brandMark = Controls.DshBrand.Mark(
                14,
                Theme.Brush("OnAccentBrush", Windows.UI.Color.FromArgb(255, 255, 255, 255)));
            _brandBadge = new Border
            {
                Width = TitleBarContentHeight,
                Height = TitleBarContentHeight,
                CornerRadius = new CornerRadius(6),
                VerticalAlignment = VerticalAlignment.Center,
                Background = Theme.Brush("AccentBrush", Windows.UI.Color.FromArgb(255, 17, 17, 18)),
                Child = _brandMark,
            };
            brand.Children.Add(_brandBadge);

            _titleText = new TextBlock
            {
                Text = Localization.IsChinese ? "大肥鱼Go安装程序" : "Dafeiyu-Go Setup",
                FontSize = 12,
                // 不要设 LineHeight / BlockLineHeight:那个"块行高"会把多余空间塞在基线下方,
                // 把字形往上顶,结果是文字视觉中心和左边的徽章对不齐(实测就是这样)。
                // 让它按自然行高排,再靠 VerticalAlignment 居中,两边中心才会重合。
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Theme.Brush("SecondaryTextBrush", Windows.UI.Color.FromArgb(255, 92, 99, 110)),
            };
            Grid.SetColumn(_titleText, 1);
            brand.Children.Add(_titleText);

            _dragArea.Children.Add(brand);

            // 关键:Grid 的 Background 为 null 时不参与命中测试 ——
            // 所以"只有品牌那两块有底色的地方能拖",中间空白点不到。
            // 给一个透明底(Transparent 不是 null)就整条都能拖了。
            _dragArea.Background = new SolidColorBrush(Colors.Transparent);

            // 拖拽自己实现:
            // 不走 SendMessage(WM_NCLBUTTONDOWN)(那会进入系统模态拖动循环,把 UI 线程堵住,
            // WinUI3 下就是"转圈 + 要再点一下才结束"),也不用 WM_NCHITTEST(WinUI3 收不到,
            // 命中测试在内容岛上)。
            // 用屏幕坐标算绝对目标位置:目标 = 按下时的窗口位置 + (当前鼠标 - 按下时鼠标)。
            // 用绝对值不会有累积误差,窗口跟手。
            _dragArea.PointerPressed += delegate(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs arguments)
            {
                try
                {
                    Microsoft.UI.Input.PointerPoint point = arguments.GetCurrentPoint(_dragArea);
                    if (point == null || !point.Properties.IsLeftButtonPressed)
                    {
                        return;
                    }

                    int cursorX;
                    int cursorY;
                    if (!NativeMethods.TryGetCursorPosition(out cursorX, out cursorY))
                    {
                        return;
                    }

                    AppWindow appWindow = GetAppWindow();
                    if (appWindow == null)
                    {
                        return;
                    }

                    _dragging = true;
                    _dragStartCursorX = cursorX;
                    _dragStartCursorY = cursorY;
                    _dragStartWindowX = appWindow.Position.X;
                    _dragStartWindowY = appWindow.Position.Y;
                    _dragArea.CapturePointer(arguments.Pointer);
                }
                catch
                {
                }
            };

            _dragArea.PointerMoved += delegate(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs arguments)
            {
                if (!_dragging)
                {
                    return;
                }

                try
                {
                    int cursorX;
                    int cursorY;
                    if (!NativeMethods.TryGetCursorPosition(out cursorX, out cursorY))
                    {
                        return;
                    }

                    AppWindow appWindow = GetAppWindow();
                    if (appWindow == null)
                    {
                        return;
                    }

                    appWindow.Move(new PointInt32(
                        _dragStartWindowX + (cursorX - _dragStartCursorX),
                        _dragStartWindowY + (cursorY - _dragStartCursorY)));
                }
                catch
                {
                }
            };

            _dragArea.PointerReleased += delegate(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs arguments)
            {
                EndDrag();
            };

            _dragArea.PointerCaptureLost += delegate
            {
                EndDrag();
            };

            _dragArea.PointerCanceled += delegate
            {
                EndDrag();
            };
            _titleBarGrid.Children.Add(_dragArea);

            // ---- 自绘的两个窗口按钮,叠在拖拽区上面 ----
            // 按钮行的格子数由系统占用区域算出来(见 LayoutCaptionButtons),
            // 这里只把两个按钮造好,位置后面再定
            _captionRow = new Grid
            {
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
            };

            _minimizeButton = CreateCaptionButton("\uE921", false);
            _minimizeIcon = _minimizeButton.Content as FontIcon;
            _minimizeButton.Click += OnMinimizeClick;

            _closeButton = CreateCaptionButton("\uE8BB", true);
            _closeIcon = _closeButton.Content as FontIcon;
            _closeButton.Click += OnCloseClick;

            // 拦住"安装中途被叉掉"。叉号 / Alt+F4 / 任务栏关闭都会走到 AppWindow.Closing。
            //
            // 这个订阅以前被误写进了标题栏的**拖拽处理函数**里 —— 于是只有"拖动过窗口"
            // 之后才装得上拦截,没拖过就直接关掉、回滚根本没机会跑(实测踩过)。
            // 它必须在这儿:搭标题栏的时候一次性装好。
            try
            {
                AppWindow initWindow = GetAppWindow();
                if (initWindow != null)
                {
                    initWindow.Closing += OnAppWindowClosing;
                }
            }
            catch
            {
            }

            // 关键:这里就把按钮挂进视觉树。
            // 之前推到 Activated 里才挂,首帧布局已经过了,结果窗口刚打开时按钮是空的,
            // 要失焦再聚焦触发一次重绘才冒出来。
            _captionRow.Width = CaptionButtonAreaWidth;
            _captionRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _captionRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            _minimizeButton.Width = CaptionButtonWidth;
            _minimizeButton.Height = TitleBarHeight;
            Grid.SetColumn(_minimizeButton, 0);
            _captionRow.Children.Add(_minimizeButton);

            _closeButton.Width = CaptionButtonWidth;
            _closeButton.Height = TitleBarHeight;
            Grid.SetColumn(_closeButton, 1);
            _captionRow.Children.Add(_closeButton);

            _titleBarGrid.Children.Add(_captionRow);

            return _titleBarGrid;
        }

        /// <summary>记一条日志,排查用。</summary>
        private static void WriteLog(string message)
        {
            try
            {
                string directory = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "DeepSeekHarness");
                System.IO.Directory.CreateDirectory(directory);
                System.IO.File.AppendAllText(
                    System.IO.Path.Combine(directory, "installer-ui.log"),
                    DateTime.Now.ToString("HH:mm:ss.fff") + "  " + message + Environment.NewLine,
                    new System.Text.UTF8Encoding(false));
            }
            catch
            {
            }
        }

        /// <summary>标题栏里图标和文字共用的高度,保证两者视觉中心一致。</summary>
        private const double TitleBarContentHeight = 20;

        /// <summary>自绘标题栏的高度(逻辑像素)。没有系统标题栏了,高度自己定。</summary>
        private const double TitleBarHeight = 34;

        /// <summary>右上角两个自绘按钮占的宽度(逻辑像素),这块不参与拖拽。</summary>
        private const double CaptionButtonAreaWidth = 92;

        /// <summary>单个窗口按钮的宽度。</summary>
        private const double CaptionButtonWidth = 46;

        /// <summary>一个自绘的窗口按钮:宽 46、高 32,和系统那套手感一致。</summary>
        private Button CreateCaptionButton(string glyph, bool isClose)
        {
            FontIcon icon = new FontIcon
            {
                Glyph = glyph,
                FontSize = 10,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };

            Button button = new Button
            {
                MinWidth = 0,
                Padding = new Thickness(0),
                CornerRadius = new CornerRadius(0),
                BorderThickness = new Thickness(0),
                Background = new SolidColorBrush(Colors.Transparent),
                BorderBrush = new SolidColorBrush(Colors.Transparent),
                UseSystemFocusVisuals = false,
                Content = icon,
            };

            // 只清边框资源;不要动 ButtonForeground,那会把图标一起改掉
            SolidColorBrush clearBrush = new SolidColorBrush(Colors.Transparent);
            button.Resources["ButtonBorderBrush"] = clearBrush;
            button.Resources["ButtonBorderBrushPointerOver"] = clearBrush;
            button.Resources["ButtonBorderBrushPressed"] = clearBrush;
            button.Resources["ButtonBorderBrushDisabled"] = clearBrush;

            // 关键:底色的"权威来源"是下面这几个资源键,不是 button.Background。
            //
            // Button 模板里有 PointerOver / Pressed 视觉状态,它们会用
            // ButtonBackgroundPointerOver / ButtonBackgroundPressed 这些**主题资源**
            // 去改 ContentPresenter 的 Background,优先级高于从外面赋的 Background。
            // 所以只设 button.Background 的话,鼠标一放上去就被状态机盖回去,
            // 表现就是"反馈完全没变"。
            // 资源查找是从元素往上找的,把键挂到按钮自己的 Resources 上就能生效。
            // 别去动 ButtonForeground —— 那会把图标前景一起改掉(之前踩过,图标变白)。
            bool dark = _shellRoot != null
                && _shellRoot.ActualTheme == ElementTheme.Dark;
            ApplyCaptionButtonBrushes(button, isClose, dark);

            // 兜底:万一某个版本的模板不走状态机
            button.PointerEntered += delegate { button.Background = CaptionBrush(isClose, false); };
            button.PointerExited += delegate { button.Background = CaptionBrush(isClose, false, true); };
            button.PointerCanceled += delegate { button.Background = CaptionBrush(isClose, false, true); };
            button.PointerCaptureLost += delegate { button.Background = CaptionBrush(isClose, false, true); };
            button.PointerPressed += delegate { button.Background = CaptionBrush(isClose, true); };
            button.PointerReleased += delegate { button.Background = CaptionBrush(isClose, false); };

            return button;
        }

        private SolidColorBrush CaptionBrush(
            bool isClose,
            bool pressed,
            bool idle = false)
        {
            if (idle)
            {
                return new SolidColorBrush(Colors.Transparent);
            }

            if (isClose)
            {
                return new SolidColorBrush(
                    pressed
                        ? Windows.UI.Color.FromArgb(255, 190, 12, 28)
                        : Windows.UI.Color.FromArgb(255, 232, 17, 35));
            }

            bool dark = _shellRoot != null
                && _shellRoot.ActualTheme == ElementTheme.Dark;
            if (dark)
            {
                return new SolidColorBrush(
                    Windows.UI.Color.FromArgb(
                        pressed ? (byte)48 : (byte)32,
                        255,
                        255,
                        255));
            }

            return new SolidColorBrush(
                pressed
                    ? Windows.UI.Color.FromArgb(255, 196, 196, 200)
                    : Windows.UI.Color.FromArgb(255, 222, 222, 225));
        }
        private void EndDrag()
        {
            _dragging = false;
        }

        /// <summary>
        /// 窗口要关了。安装进行中就拦下来:先禁用关闭按钮,再触发取消 + 回滚,
        /// 回滚干净后由进度页直接退出程序。
        ///
        /// 为什么不能"干脆不让关":用户会以为程序卡死了。别的安装器也是
        /// "拦一下、显示正在回滚、滚完自己退"。
        /// </summary>
        private void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
        {
            IWizardPageCloseGuard guard = _contentFrame == null
                ? null
                : _contentFrame.Content as IWizardPageCloseGuard;

            if (guard == null || guard.CanClose)
            {
                return;
            }

            args.Cancel = true;

            // 先禁用,免得用户连点 —— 状态马上会变成"正在回滚"
            if (_closeButton != null)
            {
                _closeButton.IsEnabled = false;
            }

            guard.OnCloseRequested();
        }

        private AppWindow GetAppWindow()
        {
            try
            {
                IntPtr handle = WindowNative.GetWindowHandle(this);
                WindowId windowId = Win32Interop.GetWindowIdFromWindow(handle);
                return AppWindow.GetFromWindowId(windowId);
            }
            catch
            {
                return null;
            }
        }

        private void OnMinimizeClick(object sender, RoutedEventArgs e)
        {
            try
            {
                IntPtr handle = WindowNative.GetWindowHandle(this);
                WindowId windowId = Win32Interop.GetWindowIdFromWindow(handle);
                AppWindow appWindow = AppWindow.GetFromWindowId(windowId);
                OverlappedPresenter presenter = appWindow.Presenter as OverlappedPresenter;
                if (presenter != null)
                {
                    presenter.Minimize();
                }
            }
            catch
            {
            }
        }

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private Border BuildFooter()
        {
            Grid grid = new Grid
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            Grid outer = new Grid { Padding = new Thickness(28, 12, 28, 16) };
            outer.Children.Add(grid);
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // 语言切换原本在标题栏里,改用系统原生标题栏后放到页脚最左边
            _languageLabel = new TextBlock
            {
                Text = Localization.IsChinese ? "中文" : "English",
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Theme.Brush("SecondaryTextBrush", Windows.UI.Color.FromArgb(255, 92, 99, 110)),
            };

            StackPanel languageContent = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                VerticalAlignment = VerticalAlignment.Center,
            };
            _languageIcon = new FontIcon
            {
                Glyph = "\uE774",
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Theme.Brush("SecondaryTextBrush", Windows.UI.Color.FromArgb(255, 92, 99, 110)),
            };
            languageContent.Children.Add(_languageIcon);
            languageContent.Children.Add(_languageLabel);

            _languageButton = new Button
            {
                Height = 32,
                MinWidth = 0,
                Padding = new Thickness(10, 0, 10, 0),
                Margin = new Thickness(0, 0, 12, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Background = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0),
                Content = languageContent,
            };
            _languageButton.Click += OnLanguageClick;
            Grid.SetColumn(_languageButton, 0);
            grid.Children.Add(_languageButton);

            _footerHint = new TextBlock
            {
                FontSize = 12,
                Margin = new Thickness(0, 0, 12, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Theme.Brush("TertiaryTextBrush", Windows.UI.Color.FromArgb(255, 138, 144, 153)),
            };
            Grid.SetColumn(_footerHint, 1);
            grid.Children.Add(_footerHint);

            _backButton = new Button
            {
                Content = Localization.T("btn.back"),
                MinWidth = 96,
                Height = 34,
                CornerRadius = Theme.CornerRadius("ButtonCornerRadius", 6),
            };
            _backButton.Click += OnBackClick;

            _nextButton = new Button
            {
                Content = Localization.T("btn.next"),
                MinWidth = 112,
                Height = 34,
                CornerRadius = Theme.CornerRadius("ButtonCornerRadius", 6),
            };
            try
            {
                object style = Application.Current.Resources["AccentButtonStyle"];
                Style accent = style as Style;
                if (accent != null)
                {
                    _nextButton.Style = accent;
                }
            }
            catch
            {
            }

            _nextButton.Click += OnNextClick;

            // 页面自己的动作按钮(目前只有进度页的"取消"用它)。
            // 放在最右,和"下一步"同一列 —— 进度页本来就把 Next 藏起来了,位置正好腾出来。
            _actionButton = new Button
            {
                MinWidth = 112,
                Height = 34,
                Visibility = Visibility.Collapsed,
                CornerRadius = Theme.CornerRadius("ButtonCornerRadius", 6),
            };
            _actionButton.Click += OnActionClick;

            StackPanel actions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Spacing = 8,
            };
            actions.Children.Add(_backButton);
            actions.Children.Add(_nextButton);
            actions.Children.Add(_actionButton);
            Grid.SetColumn(actions, 2);
            grid.Children.Add(actions);

            _footerBorder = new Border
            {
                BorderThickness = new Thickness(0, 1, 0, 0),
                BorderBrush = Theme.Brush("DividerBrush", Windows.UI.Color.FromArgb(18, 0, 0, 0)),
                Child = outer,
            };
            return _footerBorder;
        }

        /// <summary>
        /// 设计尺寸(逻辑像素)。
        /// 故意小一点:很多机器 100% 缩放下工作区只有 900 出头,设计太高会被夹成一条。
        /// </summary>
        private const int DesignWidth = 780;
        private const int DesignHeight = 520;

        private void ConfigureWindow()
        {
            Title = Localization.IsChinese ? "大肥鱼Go安装程序" : "Dafeiyu-Go Setup";

            IntPtr handle = WindowNative.GetWindowHandle(this);
            WindowId windowId = Win32Interop.GetWindowIdFromWindow(handle);
            AppWindow appWindow = AppWindow.GetFromWindowId(windowId);

            appWindow.Title = Title;

            // 固定大小:不允许拉伸,避免出现"内容只有一小撮、下面一片空白"
            if (appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.IsResizable = false;

                // 故意**不**设 IsMaximizable = false。
                // 设了之后系统会画一个"禁用态"的最大化按钮,而禁用态用的是系统内部色,
                // 不受 ButtonForegroundColor 控制 —— 设成透明也压不住它,会留一个灰方块。
                // 保留"可最大化",图标就是普通态、能吃透明前景(眼不见);
                // 真正的禁止靠下面的 RemoveMaximizeButton 去掉 WS_MAXIMIZEBOX,点了也没反应。
            }

            // 接管窗口过程,把非客户区整个抹掉 —— 系统没有绘制目标,那三个按钮就不存在了。
            // 之前靠 ExtendsContentIntoTitleBar + 按钮设透明都留了脏东西(禁用态灰块压不住)。
            int dpi = NativeMethods.GetDpiForWindow(handle);
            double chromeScale = dpi > 0 ? dpi / 96.0 : 1.0;
            WindowChrome.Attach(
                handle,
                (int)Math.Round(TitleBarHeight * chromeScale),
                (int)Math.Round(CaptionButtonAreaWidth * chromeScale));

            // 样式里把框加回来了(为了 DWM 的投影),所以最大化位要再摘一次
            NativeMethods.RemoveMaximizeButton(handle);

            ApplyBackdrop();
            ApplyCaptionButtonTheme();
            ApplyProgrammaticTheme();

            ApplyAdaptiveSize();

            // 窗口真的显示出来以后再用实际 DPI 校正一次,
            // 因为这时才拿得到窗口所在显示器的 DPI(多屏 + 不同缩放时很关键)
            Activated += OnFirstActivated;
            Activated += OnWindowActivated;
            Closed += OnWindowClosed;
        }

        private void ApplyBackdrop()
        {
            if (_backdropClosed || _shellRoot == null)
            {
                return;
            }

            DisposeBackdropControllers();
            SystemBackdrop = null;

            try
            {
                ICompositionSupportsSystemBackdrop target =
                    this.As<ICompositionSupportsSystemBackdrop>();
                _backdropConfiguration ??= new SystemBackdropConfiguration
                {
                    IsInputActive = true
                };
                _backdropConfiguration.Theme = ResolveBackdropTheme(_shellRoot.ActualTheme);

                if (MicaController.IsSupported())
                {
                    _micaController = new MicaController
                    {
                        Kind = MicaKind.BaseAlt
                    };
                    if (_micaController.AddSystemBackdropTarget(target))
                    {
                        _micaController.SetSystemBackdropConfiguration(_backdropConfiguration);
                        SetShellBackdropTransparent();
                        WriteLog("[theme] Mica Alt 已启用，跟随系统主题");
                        return;
                    }

                    DisposeBackdropControllers();
                }

                if (DesktopAcrylicController.IsSupported())
                {
                    _acrylicController = new DesktopAcrylicController
                    {
                        Kind = DesktopAcrylicKind.Thin
                    };
                    if (_acrylicController.AddSystemBackdropTarget(target))
                    {
                        _acrylicController.SetSystemBackdropConfiguration(_backdropConfiguration);
                        SetShellBackdropTransparent();
                        WriteLog("[theme] Mica Alt 不受支持，已回退 Acrylic");
                        return;
                    }

                    DisposeBackdropControllers();
                }
            }
            catch (Exception exception)
            {
                DisposeBackdropControllers();
                WriteLog("[theme] 系统材质启用失败: " + exception.Message);
            }

            ApplyShellFallbackBackground();
        }

        private void OnShellActualThemeChanged(
            FrameworkElement sender,
            object args)
        {
            if (_backdropConfiguration != null)
            {
                _backdropConfiguration.Theme = ResolveBackdropTheme(sender.ActualTheme);
            }

            ApplyCaptionButtonTheme();
            ApplyProgrammaticTheme();

            if (_micaController == null && _acrylicController == null)
            {
                ApplyShellFallbackBackground();
            }
        }

        private void OnWindowActivated(
            object sender,
            WindowActivatedEventArgs arguments)
        {
            if (_backdropConfiguration != null)
            {
                _backdropConfiguration.IsInputActive =
                    arguments.WindowActivationState
                    != WindowActivationState.Deactivated;
            }
        }

        private void OnWindowClosed(object sender, WindowEventArgs args)
        {
            _backdropClosed = true;
            DisposeBackdropControllers();
        }

        private void DisposeBackdropControllers()
        {
            if (_micaController != null)
            {
                try
                {
                    _micaController.RemoveAllSystemBackdropTargets();
                    _micaController.Dispose();
                }
                catch
                {
                }

                _micaController = null;
            }

            if (_acrylicController != null)
            {
                try
                {
                    _acrylicController.RemoveAllSystemBackdropTargets();
                    _acrylicController.Dispose();
                }
                catch
                {
                }

                _acrylicController = null;
            }
        }

        private void SetShellBackdropTransparent()
        {
            if (_shellRoot != null)
            {
                _shellRoot.Background = new SolidColorBrush(Colors.Transparent);
            }
        }

        private void ApplyShellFallbackBackground()
        {
            if (_shellRoot != null)
            {
                _shellRoot.Background = Theme.Brush(
                    "ShellBackgroundBrush",
                    Windows.UI.Color.FromArgb(255, 244, 246, 250));
            }
        }

        private static SystemBackdropTheme ResolveBackdropTheme(
            ElementTheme theme)
        {
            if (theme == ElementTheme.Light)
            {
                return SystemBackdropTheme.Light;
            }

            if (theme == ElementTheme.Dark)
            {
                return SystemBackdropTheme.Dark;
            }

            return SystemBackdropTheme.Default;
        }

        private void ApplyCaptionButtonTheme()
        {
            bool dark = _shellRoot != null
                && _shellRoot.ActualTheme == ElementTheme.Dark;
            SolidColorBrush iconBrush = new SolidColorBrush(
                dark
                    ? Windows.UI.Color.FromArgb(255, 245, 245, 247)
                    : Windows.UI.Color.FromArgb(255, 24, 24, 27));

            if (_minimizeIcon != null)
            {
                _minimizeIcon.Foreground = iconBrush;
            }

            if (_closeIcon != null)
            {
                _closeIcon.Foreground = iconBrush;
            }

            ApplyCaptionButtonBrushes(_minimizeButton, false, dark);
            ApplyCaptionButtonBrushes(_closeButton, true, dark);
        }

        private void ApplyProgrammaticTheme()
        {
            Brush secondary = Theme.Brush(
                "SecondaryTextBrush",
                Windows.UI.Color.FromArgb(255, 92, 99, 110));
            Brush tertiary = Theme.Brush(
                "TertiaryTextBrush",
                Windows.UI.Color.FromArgb(255, 138, 144, 153));
            Brush accent = Theme.Brush(
                "AccentBrush",
                Windows.UI.Color.FromArgb(255, 17, 17, 18));
            Brush onAccent = Theme.Brush(
                "OnAccentBrush",
                Windows.UI.Color.FromArgb(255, 255, 255, 255));
            Brush divider = Theme.Brush(
                "DividerBrush",
                Windows.UI.Color.FromArgb(18, 0, 0, 0));

            if (_titleText != null)
            {
                _titleText.Foreground = secondary;
            }

            if (_brandBadge != null)
            {
                _brandBadge.Background = accent;
            }

            if (_brandMark != null)
            {
                _brandMark.Fill = onAccent;
            }

            if (_languageLabel != null)
            {
                _languageLabel.Foreground = secondary;
            }

            if (_languageIcon != null)
            {
                _languageIcon.Foreground = secondary;
            }

            if (_footerHint != null)
            {
                _footerHint.Foreground = tertiary;
            }

            if (_footerBorder != null)
            {
                _footerBorder.BorderBrush = divider;
            }
        }

        private static void ApplyCaptionButtonBrushes(
            Button button,
            bool isClose,
            bool dark)
        {
            if (button == null)
            {
                return;
            }

            SolidColorBrush idle = new SolidColorBrush(Colors.Transparent);
            SolidColorBrush hover = isClose
                ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 232, 17, 35))
                : new SolidColorBrush(
                    dark
                        ? Windows.UI.Color.FromArgb(32, 255, 255, 255)
                        : Windows.UI.Color.FromArgb(255, 222, 222, 225));
            SolidColorBrush pressed = isClose
                ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 190, 12, 28))
                : new SolidColorBrush(
                    dark
                        ? Windows.UI.Color.FromArgb(48, 255, 255, 255)
                        : Windows.UI.Color.FromArgb(255, 196, 196, 200));

            button.Resources["ButtonBackground"] = idle;
            button.Resources["ButtonBackgroundPointerOver"] = hover;
            button.Resources["ButtonBackgroundPressed"] = pressed;
            button.Resources["ButtonBackgroundDisabled"] = idle;
        }

        private bool _sizeCorrected;

        private void OnFirstActivated(object sender, WindowActivatedEventArgs arguments)
        {
            if (_sizeCorrected)
            {
                return;
            }

            _sizeCorrected = true;
            Activated -= OnFirstActivated;
            ApplyAdaptiveSize();
            SyncTitleBarHeight();
        }

        /// <summary>
        /// 按 DPI 微调按钮尺寸,并告诉窗口过程哪块不是拖拽区。
        /// 按钮本身在 BuildTitleBar 里就挂好了,这里不再重建 —— 重建会让它们首帧画不出来。
        /// </summary>
        private void LayoutCaptionButtons(double totalWidth, double height)
        {
            if (_captionRow == null || _minimizeButton == null || _closeButton == null)
            {
                return;
            }

            double slotWidth = totalWidth / 2;
            _captionRow.Width = totalWidth;
            _captionRow.Height = height;
            _minimizeButton.Width = slotWidth;
            _minimizeButton.Height = height;
            _closeButton.Width = slotWidth;
            _closeButton.Height = height;

            // 拖拽区要把按钮那块让出来,否则点按钮会被当成拖窗口
            IntPtr handle = WindowNative.GetWindowHandle(this);
            int dpi = NativeMethods.GetDpiForWindow(handle);
            double scale = dpi > 0 ? dpi / 96.0 : 1.0;
            WindowChrome.UpdateDragArea(
                (int)Math.Round(height * scale),
                (int)Math.Round(totalWidth * scale));
        }
        /// <summary>
        /// 把自绘标题栏和按钮的高度摆正。
        /// 非客户区已经抹掉了,没有"系统标题栏高度"要对齐,直接用自己的设计值。
        /// </summary>
        private void SyncTitleBarHeight()
        {
            try
            {
                if (_titleBarGrid != null)
                {
                    _titleBarGrid.Height = TitleBarHeight;
                }

                if (_dragArea != null)
                {
                    _dragArea.Height = TitleBarHeight;
                }

                LayoutCaptionButtons(CaptionButtonAreaWidth, TitleBarHeight);
            }
            catch
            {
            }
        }
        /// <summary>
        /// 按"窗口真实 DPI + 显示器工作区"算尺寸。
        ///
        /// 之前写死 920x640 逻辑像素:在 150% 缩放的机器上换算成 1380x960 物理像素,
        /// 而工作区只有 912 高,于是被夹到 427 —— 整页内容全挤没了。
        /// 现在以工作区为准:留边距、保下限,不再出现"被夹成一条"。
        /// </summary>
        private void ApplyAdaptiveSize()
        {
            try
            {
                IntPtr handle = WindowNative.GetWindowHandle(this);
                WindowId windowId = Win32Interop.GetWindowIdFromWindow(handle);
                AppWindow appWindow = AppWindow.GetFromWindowId(windowId);

                int dpi = NativeMethods.GetDpiForWindow(handle);
                if (dpi <= 0)
                {
                    dpi = 96;
                }

                double scale = dpi / 96.0;

                DisplayArea area = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
                RectInt32 work = area.WorkArea;

                int width = (int)Math.Round(DesignWidth * scale);
                int height = (int)Math.Round(DesignHeight * scale);

                // 留边距,别顶到屏幕边
                int availableWidth = Math.Max(480, work.Width - 80);
                int availableHeight = Math.Max(360, work.Height - 80);

                if (width > availableWidth)
                {
                    width = availableWidth;
                }

                if (height > availableHeight)
                {
                    height = availableHeight;
                }

                appWindow.Resize(new SizeInt32(width, height));
                appWindow.Move(new PointInt32(
                    work.X + (work.Width - width) / 2,
                    work.Y + (work.Height - height) / 2));
            }
            catch
            {
                try
                {
                    IntPtr handle = WindowNative.GetWindowHandle(this);
                    WindowId windowId = Win32Interop.GetWindowIdFromWindow(handle);
                    AppWindow.GetFromWindowId(windowId).Resize(new SizeInt32(DesignWidth, DesignHeight));
                }
                catch
                {
                }
            }
        }

        /// <summary>当前是不是深色主题(跟随系统)。</summary>
        private static bool IsDarkTheme()
        {
            try
            {
                object value = Application.Current.Resources["ShellBackgroundBrush"];
                SolidColorBrush brush = value as SolidColorBrush;
                if (brush != null)
                {
                    Windows.UI.Color color = brush.Color;
                    // 用亮度判断,省得再去读系统设置
                    double luminance = (0.299 * color.R + 0.587 * color.G + 0.114 * color.B) / 255.0;
                    return luminance < 0.5;
                }
            }
            catch
            {
            }

            return false;
        }

        // ---------------------------------------------------------------- 导航

        internal WizardPage CurrentPage
        {
            get { return _current; }
        }

        /// <summary>让页面自己算出来的下一步生效(例如检测全绿就跳过组件页)。</summary>
        internal void SetNextOverride(WizardPage? next, string buttonText = null)
        {
            PageInfo info = _pages[_current];
            info.NextOverride = next;
            if (!string.IsNullOrEmpty(buttonText))
            {
                info.NextText = buttonText;
            }

            ApplyChrome();
        }

        internal void Navigate(WizardPage page, object parameter)
        {
            if (_navigating)
            {
                return;
            }

            _navigating = true;
            try
            {
                _current = page;
                _contentFrame.Navigate(_pages[page].Type, parameter, new DrillInNavigationTransitionInfo());
                ApplyChrome();
            }
            finally
            {
                _navigating = false;
            }
        }

        /// <summary>页面改了内容(例如填好路径)后调用,重新算按钮可用状态。</summary>
        internal void RefreshChrome()
        {
            ApplyChrome();
        }

        private void ApplyChrome()
        {
            PageInfo info = _pages[_current];

            _backButton.Visibility = info.ShowBack ? Visibility.Visible : Visibility.Collapsed;
            _nextButton.Visibility = info.ShowNext ? Visibility.Visible : Visibility.Collapsed;

            // 这两个按钮的文案也要跟着语言走。
            // 之前只更新了"下一步","上一步"是造按钮时定死的,切语言就不动了。
            _backButton.Content = Localization.T("btn.back");
            _nextButton.Content = LocalizeButton(info.NextText);
            _footerHint.Text = info.Hint;
            _backButton.IsEnabled = _current != WizardPage.Welcome;

            IWizardPage page = _contentFrame.Content as IWizardPage;
            _nextButton.IsEnabled = page == null || page.CanGoNext;

            // 页面自带动作按钮(例如进度页的"取消")就显示出来,盖在 Next 的位置上
            _footerAction = _contentFrame.Content as IWizardPageFooterAction;
            if (_footerAction != null && !string.IsNullOrEmpty(_footerAction.FooterActionText))
            {
                _actionButton.Content = _footerAction.FooterActionText;
                _actionButton.Visibility = Visibility.Visible;
                _actionButton.IsEnabled = _footerAction.FooterActionEnabled;
            }
            else
            {
                _actionButton.Visibility = Visibility.Collapsed;
            }

            // 语言切换**只在各自的首页**给:
            //   安装模式 = 欢迎页;卸载模式 = 卸载确认页。
            //
            // 切语言会重建整套界面,而安装进行中重建 = 进度状态对不上、
            // 后台任务还在跑界面已经换了(实测会出问题)。
            // 首页是唯一"什么都还没开始"的地方,放这儿最安全。
            if (_languageButton != null)
            {
                bool firstPage = _current == WizardPage.Welcome || _current == WizardPage.Uninstall;

                _languageButton.Visibility = firstPage ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        /// <summary>按钮文案走一遍本地化(键在里面就翻,不在就原样)。</summary>
        private static string LocalizeButton(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            switch (text)
            {
                case "下一步":
                    return Localization.T("btn.next");
                case "上一步":
                    return Localization.T("btn.back");
                case "开始安装":
                    return Localization.T("btn.start");
                case "继续":
                    return Localization.T("btn.continue");
                case "完成":
                    return Localization.T("btn.finish");
                default:
                    return text;
            }
        }

        private static WizardPage NextOf(WizardPage page)
        {
            int next = (int)page + 1;
            int last = (int)WizardPage.Done;
            return next > last ? page : (WizardPage)next;
        }

        /// <summary>
        /// "上一步"该去哪。
        ///
        /// 默认按枚举序号 -1,但枚举里 Failed / UninstallItems 排在后面,
        /// 序号并不连续 —— 卸载第二页(11)减一正好是失败页(10),
        /// 于是点"上一步"会莫名跳到"安装失败"(实测踩过)。所以允许页面显式覆盖。
        /// </summary>
        private WizardPage PreviousOf(WizardPage page)
        {
            PageInfo info;
            if (_pages.TryGetValue(page, out info) && info.BackOverride.HasValue)
            {
                return info.BackOverride.Value;
            }

            int previous = (int)page - 1;
            return previous < 0 ? WizardPage.Welcome : (WizardPage)previous;
        }

        // ---------------------------------------------------------------- 事件

        private void OnNextClick(object sender, RoutedEventArgs e)
        {
            PageInfo info = _pages[_current];

            IWizardPage page = _contentFrame.Content as IWizardPage;
            if (page != null && !page.OnNext())
            {
                return;
            }

            // 运行时动态决定的下一页:用一次就失效
            if (info.DynamicNext.HasValue)
            {
                WizardPage target = info.DynamicNext.Value;
                info.DynamicNext = null;
                Navigate(target, null);
                return;
            }

            // 注册时定死的下一页:一直有效,来回翻也还是它
            if (info.NextOverride.HasValue)
            {
                Navigate(info.NextOverride.Value, null);
                return;
            }

            if (_current == WizardPage.Done)
            {
                Close();
                return;
            }

            Navigate(NextOf(_current), null);
        }

        private void OnActionClick(object sender, RoutedEventArgs e)
        {
            IWizardPageFooterAction action = _footerAction;
            if (action != null)
            {
                action.OnFooterAction();
            }
        }

        private void OnBackClick(object sender, RoutedEventArgs e)
        {
            if (_current == WizardPage.Welcome)
            {
                return;
            }

            Navigate(PreviousOf(_current), null);
        }

        private void OnLanguageClick(object sender, RoutedEventArgs e)
        {
            Localization.Current = Localization.IsChinese
                ? Localization.Language.English
                : Localization.Language.Chinese;

            _languageLabel.Text = Localization.IsChinese ? "中文" : "English";
            if (_titleText != null)
            {
                _titleText.Text = Localization.IsChinese
                    ? "大肥鱼Go安装程序"
                    : "Dafeiyu-Go Setup";
            }


            // 重进当前页,让文案立刻切过去
            WizardPage page = _current;
            _navigating = true;
            try
            {
                _contentFrame.Navigate(_pages[page].Type, null, new SuppressNavigationTransitionInfo());
            }
            finally
            {
                _navigating = false;
            }

            // 窗口标题也跟着语言走
            Title = Localization.IsChinese ? "大肥鱼Go安装程序" : "Dafeiyu-Go Setup";

            ApplyChrome();
        }
    }

    /// <summary>页面实现它,向导才知道这页能不能往下走、点下一步要不要拦截。</summary>
    internal interface IWizardPage
    {
        bool CanGoNext { get; }

        /// <summary>返回 false 表示拦住,不跳页。</summary>
        bool OnNext();
    }

    /// <summary>
    /// 页面要在安装期间拦住窗口关闭时实现它。
    /// 别的安装器也是这个思路:装到一半不能被叉掉,但也不能真的关不掉 ——
    /// 拦下来先去回滚,滚干净再退。
    /// </summary>
    internal interface IWizardPageCloseGuard
    {
        /// <summary>现在允不允许关窗口。安装进行中为 false。</summary>
        bool CanClose { get; }

        /// <summary>用户试图关闭窗口时调用(此时窗口已经被拦下)。</summary>
        void OnCloseRequested();
    }

    /// <summary>
    /// 页面想在页脚放一个自己的动作按钮时实现它(目前只有进度页的"取消"用)。
    /// 做成可选接口,别的页不用被迫实现一堆空成员。
    /// </summary>
    internal interface IWizardPageFooterAction
    {
        /// <summary>按钮文案。空字符串 = 不显示按钮。</summary>
        string FooterActionText { get; }

        bool FooterActionEnabled { get; }

        void OnFooterAction();
    }
}
