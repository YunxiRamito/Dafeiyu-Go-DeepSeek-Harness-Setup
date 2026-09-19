using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;

namespace DshInstaller.Controls
{
    /// <summary>
    /// 向导页的统一外框:步骤条 + 标题 + 副标题 + 内容区。
    ///
    /// 关键点:标了 [ContentProperty("Body")],所以页面 XAML 里写的子元素会直接落到 BodyHost。
    /// 不标的话子元素会变成 UserControl 自己的 Content,把步骤条和标题整个顶掉
    /// (表现为每页都没有标题 —— 这个坑真踩过)。
    /// </summary>
    [ContentProperty(Name = "Body")]
    public sealed partial class PageScaffold : UserControl
    {
        public PageScaffold()
        {
            InitializeComponent();
        }

        public static readonly DependencyProperty TitleProperty =
            DependencyProperty.Register(
                nameof(Title), typeof(string), typeof(PageScaffold),
                new PropertyMetadata(string.Empty, OnTitleChanged));

        public static readonly DependencyProperty SubtitleProperty =
            DependencyProperty.Register(
                nameof(Subtitle), typeof(string), typeof(PageScaffold),
                new PropertyMetadata(string.Empty, OnSubtitleChanged));

        public string Title
        {
            get { return (string)GetValue(TitleProperty); }
            set { SetValue(TitleProperty, value); }
        }

        public string Subtitle
        {
            get { return (string)GetValue(SubtitleProperty); }
            set { SetValue(SubtitleProperty, value); }
        }

        /// <summary>页面内容。XAML 里直接写子元素就会落到这里。</summary>
        public UIElement Body
        {
            get { return BodyHost.Content as UIElement; }
            set { BodyHost.Content = value; }
        }

        /// <summary>步骤条当前处在第几步(0 起)。</summary>
        public void SetStep(int index)
        {
            Steps.SetStep(index);
        }

        /// <summary>
        /// 指定"一共几格、现在第几格"。卸载流程只有三步,用它把顶部的点换成 3 个。
        /// </summary>
        public void SetSteps(int count, int index)
        {
            Steps.SetCount(count);
            Steps.SetStep(index);
        }

        private static void OnTitleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((PageScaffold)d).TitleText.Text = (string)e.NewValue ?? string.Empty;
        }

        private static void OnSubtitleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            PageScaffold scaffold = (PageScaffold)d;
            string text = (string)e.NewValue ?? string.Empty;
            scaffold.SubtitleText.Text = text;
            scaffold.SubtitleText.Visibility = string.IsNullOrEmpty(text)
                ? Visibility.Collapsed
                : Visibility.Visible;
        }
    }
}
