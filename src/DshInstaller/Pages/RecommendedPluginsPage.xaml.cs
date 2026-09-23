using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using DshInstaller.Controls;
using DshInstaller.Shared.Install;

namespace DshInstaller.Pages
{
    public sealed partial class RecommendedPluginsPage : Page, IWizardPage
    {
        private readonly List<CheckBox> _boxes = new List<CheckBox>();
        private bool _loaded;

        public RecommendedPluginsPage()
        {
            InitializeComponent();
            ApplyText();
            Loaded += OnLoaded;
        }

        public bool CanGoNext
        {
            get { return true; }
        }

        public bool OnNext()
        {
            InstallSession session = InstallSession.Current;
            if (!session.PluginToolsAvailable)
            {
                session.RecommendedPluginSpecs.Clear();
                MainWindow window = App.MainWindowInstance;
                if (window != null)
                {
                    window.SetNextOverride(WizardPage.LauncherLocation);
                }

                return true;
            }

            session.RecommendedPluginSpecs.Clear();
            for (int index = 0; index < _boxes.Count; index++)
            {
                if (_boxes[index].IsChecked != true)
                {
                    continue;
                }

                string specifier = _boxes[index].Tag as string;
                if (!String.IsNullOrWhiteSpace(specifier))
                {
                    session.RecommendedPluginSpecs.Add(specifier);
                }
            }

            return true;
        }

        private void ApplyText()
        {
            Scaffold.Title = "推荐插件";
            Scaffold.Subtitle = "从官方推荐列表中勾选需要的插件；不勾选任何项目也可以继续。";
            Scaffold.SetStep(5);
            SelectAllButton.Content = "全部选择";
            SkipAllButton.Content = "全部跳过";
            SummaryText.Text = "正在读取官方推荐列表…";
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (_loaded)
            {
                return;
            }

            _loaded = true;
            List<RecommendedPluginItem> items =
                await RecommendedPluginFeed.LoadAsync(
                    InstallSession.Current.SourcePreference);
            BuildItems(items);
        }

        private void BuildItems(List<RecommendedPluginItem> items)
        {
            PluginsHost.Children.Clear();
            _boxes.Clear();
            LoadingRing.IsActive = false;
            LoadingRing.Visibility = Visibility.Collapsed;
            ListScroller.Visibility = Visibility.Visible;

            if (items == null || items.Count == 0)
            {
                FeedInfoBar.Title = "没有可用的推荐列表";
                FeedInfoBar.Message = "可以跳过这一页，之后在启动器里安装插件。";
                FeedInfoBar.IsOpen = true;
                SummaryText.Text = "推荐插件暂不可用";
                return;
            }

            for (int index = 0; index < items.Count; index++)
            {
                RecommendedPluginItem item = items[index];
                CheckBox box = new CheckBox
                {
                    Content = item.DisplayName,
                    Tag = item.InstallSpecifier,
                    IsChecked = false,
                    MinWidth = 0,
                };
                _boxes.Add(box);

                Border iconBorder = new Border
                {
                    Width = 36,
                    Height = 36,
                    CornerRadius = new CornerRadius(18),
                    Background = (Microsoft.UI.Xaml.Media.Brush)
                        Application.Current.Resources[
                            "CardBorderBrush"],
                    VerticalAlignment = VerticalAlignment.Top
                };
                Image icon = new Image
                {
                    Width = 34,
                    Height = 34,
                    Stretch = Microsoft.UI.Xaml.Media.Stretch.UniformToFill
                };
                string imageUrl = item.ImageUrl;
                if (String.IsNullOrWhiteSpace(imageUrl))
                {
                    imageUrl = "https://github.com/"
                        + item.Owner
                        + ".png?size=80";
                }

                Uri imageUri;
                if (Uri.TryCreate(
                    imageUrl,
                    UriKind.Absolute,
                    out imageUri))
                {
                    icon.Source = new BitmapImage(imageUri);
                }

                iconBorder.Child = icon;

                StackPanel content = new StackPanel
                {
                    Spacing = 3
                };
                content.Children.Add(box);
                TextBlock description = new TextBlock
                {
                    Text = String.IsNullOrWhiteSpace(item.Note)
                        ? item.Description
                        : item.Note + " · " + item.Description,
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(30, 0, 0, 0),
                    Foreground = (Microsoft.UI.Xaml.Media.Brush)
                        Application.Current.Resources[
                            "SecondaryTextBrush"]
                };
                content.Children.Add(description);

                Grid itemGrid = new Grid
                {
                    ColumnSpacing = 10
                };
                itemGrid.ColumnDefinitions.Add(
                    new ColumnDefinition { Width = GridLength.Auto });
                itemGrid.ColumnDefinitions.Add(
                    new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                Grid.SetColumn(iconBorder, 0);
                Grid.SetColumn(content, 1);
                itemGrid.Children.Add(iconBorder);
                itemGrid.Children.Add(content);

                Border card = new Border
                {
                    Padding = new Thickness(14, 10, 14, 10),
                    CornerRadius = (CornerRadius)
                        Application.Current.Resources[
                            "CardCornerRadius"],
                    Background = (Microsoft.UI.Xaml.Media.Brush)
                        Application.Current.Resources[
                            "CardBackgroundBrush"],
                    BorderBrush = (Microsoft.UI.Xaml.Media.Brush)
                        Application.Current.Resources[
                            "CardBorderBrush"],
                    BorderThickness = new Thickness(1),
                    Child = itemGrid
                };
                PluginsHost.Children.Add(card);
            }

            UpdateSummary();
        }

        private void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            SetAll(true);
        }

        private void SkipAll_Click(object sender, RoutedEventArgs e)
        {
            SetAll(false);
        }

        private void SetAll(bool selected)
        {
            for (int index = 0; index < _boxes.Count; index++)
            {
                _boxes[index].IsChecked = selected;
            }

            UpdateSummary();
        }

        private void UpdateSummary()
        {
            int selected = 0;
            for (int index = 0; index < _boxes.Count; index++)
            {
                if (_boxes[index].IsChecked == true)
                {
                    selected++;
                }
            }

            SummaryText.Text = "已选择 " + selected + " 个插件；"
                + "未勾选的插件会自动跳过。安装推荐插件需要 Git 和 pnpm，"
                + "安装器会在需要时自动加入。";
        }
    }
}
