using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DshInstaller.Shared.Backup.Official;

internal static class OfficialDshRestore
{
    [STAThread]
    private static int Main(string[] args)
    {
        string self = Assembly.GetExecutingAssembly().Location;
        if (args.Contains("--verify") || args.Contains("--restore"))
        {
            Outcome result;
            try
            {
                string archive = Value(args, "--archive") ?? self;
                if (args.Contains("--verify")) { Protocol.Validate(archive, CancellationToken.None); result = new Outcome { Ok = true, Summary = "All payload paths and hashes verified." }; }
                else result = Migration.Restore(archive, Value(args, "--home"), Value(args, "--agents"), Value(args, "--official-root"),
                    (Value(args, "--groups") ?? "").Split(','), args.Contains("--merge-plugins"), args.Contains("--confirm-closed"), null, CancellationToken.None);
            }
            catch (Exception ex) { result = new Outcome { Error = ex.Message }; }
            string report = Value(args, "--report");
            if (report != null) { Protocol.Plain(report); File.WriteAllText(report, "Ok=" + result.Ok + "\nCanceled=" + result.Canceled + "\n" + result.Summary + "\n" + result.Error + "\n" + String.Join("\n", result.Warnings)); }
            return result.Ok ? 0 : result.Canceled ? 2 : 1;
        }
        try { var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose }; var window = new RestoreWindow(self); app.MainWindow = window; app.Run(window); return 0; }
        catch (Exception ex) { MessageBox.Show(ex.Message, "官方 DSH 数据还原", MessageBoxButton.OK, MessageBoxImage.Error); return 1; }
    }
    private static string Value(string[] args, string option)
    { int index = Array.IndexOf(args, option); return index >= 0 && index + 1 < args.Length ? args[index + 1] : null; }

    private sealed class RestoreWindow : Window
    {
        private readonly string self;
        private readonly TextBox home, agents, official, details;
        private readonly TextBlock status;
        private readonly ProgressBar progress;
        private readonly Button restore, cancel;
        private readonly Button homeBrowse, agentsBrowse, officialBrowse;
        private readonly CheckBox closed, merge;
        private readonly StackPanel groups;
        private readonly Dictionary<string, CheckBox> selections = new Dictionary<string, CheckBox>();
        private CancellationTokenSource cancellation;
        private bool busy;
        private bool closing;
        private CancellationTokenSource loadCancellation;
        internal RestoreWindow(string self)
        {
            this.self = self;
            Window loadedTemplate;
            using (Stream resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("OfficialDshRestore.Window.xaml"))
            {
                var template = (Window)XamlReader.Load(resource);
                loadedTemplate = template;
                object content = template.Content;
                template.Content = null;
                Title = template.Title; Width = template.Width; Height = template.Height; MinWidth = template.MinWidth; MinHeight = template.MinHeight;
                Background = template.Background; Foreground = template.Foreground; FontFamily = template.FontFamily; FontSize = template.FontSize;
                Resources = template.Resources; Content = content;
            }
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            // XamlReader registers names on the Window namescope, not on its child Grid.
            var scope = loadedTemplate;
            home = (TextBox)scope.FindName("Home"); agents = (TextBox)scope.FindName("Agents"); official = (TextBox)scope.FindName("Official");
            details = (TextBox)scope.FindName("Details"); status = (TextBlock)scope.FindName("Status"); progress = (ProgressBar)scope.FindName("Progress");
            restore = (Button)scope.FindName("Restore"); cancel = (Button)scope.FindName("Cancel"); closed = (CheckBox)scope.FindName("Closed"); merge = (CheckBox)scope.FindName("Merge"); groups = (StackPanel)scope.FindName("Groups");
            official.Text = Migration.DetectOfficialRoot() ?? "";
            List<string> installs = Migration.DetectOfficialRoots();
            if (installs.Count > 1) { official.Text = ""; official.ToolTip = "检测到多个官方安装，请选择或填写目标：\n" + String.Join("\n", installs); }
            string env = Environment.GetEnvironmentVariable("DSH_HOME"); home.Text = String.IsNullOrWhiteSpace(env) ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dsh") : env;
            agents.Text = Migration.AgentsHome();
            restore.IsEnabled = false; restore.Click += Start; cancel.Click += delegate { if (cancellation != null) { cancellation.Cancel(); status.Text = "正在取消；已完成的数据会保留。"; } else Close(); };
            homeBrowse = (Button)scope.FindName("HomeBrowse"); agentsBrowse = (Button)scope.FindName("AgentsBrowse"); officialBrowse = (Button)scope.FindName("OfficialBrowse");
            homeBrowse.Content = FolderIcon.Create();
            agentsBrowse.Content = FolderIcon.Create();
            officialBrowse.Content = FolderIcon.Create();
            loadedTemplate.Close();
            homeBrowse.Click += delegate { Browse(home); }; agentsBrowse.Click += delegate { Browse(agents); }; officialBrowse.Click += delegate { Browse(official); };
            Closing += delegate(object sender, System.ComponentModel.CancelEventArgs e)
            {
                if (loadCancellation != null) loadCancellation.Cancel();
                if (busy) { e.Cancel = true; if (cancellation != null) cancellation.Cancel(); return; }
                if (!closing)
                {
                    closing = true; e.Cancel = true;
                    BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(140)) { EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseIn } });
                    var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
                    timer.Tick += delegate { timer.Stop(); Close(); }; timer.Start();
                }
            };
            Loaded += LoadPayload;
        }
        private static void Browse(TextBox box)
        {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog { SelectedPath = Directory.Exists(box.Text) ? box.Text : "", ShowNewFolderButton = true })
                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK) box.Text = dialog.SelectedPath;
        }
        private async void LoadPayload(object sender, RoutedEventArgs e)
        {
            loadCancellation = new CancellationTokenSource();
            try
            {
                status.Text = "正在完整校验数据包…";
                PayloadManifest manifest = await Task.Run(() => Protocol.Validate(self, loadCancellation.Token), loadCancellation.Token);
                if (closing || !IsLoaded || loadCancellation.IsCancellationRequested) return;
                foreach (var group in manifest.Files.GroupBy(f => f.Group))
                {
                    bool sensitive = group.Key == "official-credentials" || group.Key == "official-compatibility" || group.Key == "profiles";
                    var check = new CheckBox { Content = new TextBlock { Text = GroupName(group.Key) + "  ·  " + group.Count().ToString("N0") + " 文件  ·  " + (group.Sum(f => f.Length) / 1048576d).ToString("0.0") + " MB", TextWrapping = TextWrapping.Wrap }, IsChecked = !sensitive, Margin = new Thickness(0, 3, 0, 3), HorizontalContentAlignment = HorizontalAlignment.Stretch };
                    System.Windows.Automation.AutomationProperties.SetName(check, GroupName(group.Key)); selections.Add(group.Key, check); groups.Children.Add(check);
                }
                details.Text = String.Join(Environment.NewLine, manifest.Warnings ?? new List<string>()); status.Text = "数据包校验通过"; restore.IsEnabled = true;
            }
            catch (OperationCanceledException) { if (!closing) status.Text = "数据包校验已取消"; }
            catch (Exception ex) { if (!closing) { status.Text = "数据包不可还原"; details.Text = ex.Message; } }
        }
        private async void Start(object sender, RoutedEventArgs e)
        {
            try
            {
                if (closed.IsChecked != true) { status.Text = "请正常退出官方 DSH，然后确认已关闭。"; closed.Focus(); return; }
                List<string> selected = selections.Where(p => p.Value.IsChecked == true).Select(p => p.Key).ToList();
                if (selected.Count == 0) { status.Text = "请选择还原内容。"; return; }
                string target = Protocol.Full(home.Text), shared = Protocol.Full(agents.Text), install = Protocol.Full(official.Text);
                if (Migration.OfficialRunning(install)) { status.Text = "官方 DSH 仍在运行，请正常退出后重试。"; return; }
                bool pluginMerge = merge.IsChecked == true;
                string note = "目标 DSH_HOME：" + target + "\n共享 Skill：" + shared + "\n\n已有会话、附件、Skill、配置及凭据保留并跳过。";
                if (pluginMerge) note += "\n仅合并 desktop 清单中缺失的第三方依赖和 bundles；原清单保留备份。已有依赖与官方 bundles 保持原值。";
                if (selected.Contains("official-credentials") || selected.Contains("profiles") || selected.Contains("official-compatibility")) note += "\n已选择可能包含 API key、token 或账号信息的敏感数据。";
                note += "\n插件 npm 依赖需要官方 CLI 重建；版本、原生模块及配置冲突可能需要人工处理。\n\n继续还原？";
                if (MessageBox.Show(this, note, "确认还原内容", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;
                busy = true; cancellation = new CancellationTokenSource(); SetBusy(true);
                Action<string, double> report = delegate(string text, double percent) { Dispatcher.Invoke(delegate { status.Text = text; if (!Double.IsNaN(percent)) progress.Value = percent; }); };
                Outcome result = await Task.Run(() => Migration.Restore(self, target, shared, install, selected, pluginMerge, true, report, cancellation.Token));
                status.Text = result.Ok ? "还原完成" : result.Canceled ? "已取消" : "还原未完成";
                details.Text = result.Summary + Environment.NewLine + result.Error + Environment.NewLine + String.Join(Environment.NewLine, result.Warnings);
                if (result.Ok) progress.Value = 100;
            }
            catch (Exception ex) { status.Text = "还原失败"; details.Text = ex.Message; }
            finally { busy = false; SetBusy(false); if (cancellation != null) { cancellation.Dispose(); cancellation = null; } }
        }
        private void SetBusy(bool value)
        {
            restore.IsEnabled = !value; home.IsEnabled = agents.IsEnabled = official.IsEnabled = !value;
            homeBrowse.IsEnabled = agentsBrowse.IsEnabled = officialBrowse.IsEnabled = !value;
            closed.IsEnabled = merge.IsEnabled = !value;
            foreach (CheckBox check in selections.Values) check.IsEnabled = !value;
        }
        private static string GroupName(string id)
        {
            switch (id) { case "sessions": return "会话、存储与附件"; case "official-attachments": return "附件"; case "skills": return "用户与共享 Skill"; case "official-shared-skills": return "共享 Skill";
                case "plugins": return "插件源码与清单"; case "profiles": return "配置（可能包含敏感数据）"; case "official-credentials": return "账号配置（API key / token，敏感数据）"; case "official-compatibility": return "Dafeiyu 兼容偏好（官方不直接读取，敏感数据）"; default: return id; }
        }
    }

    private static class FolderIcon
    {
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private struct ShellFileInfo
        {
            public IntPtr Icon;
            public int IconIndex;
            public uint Attributes;
            [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst = 260)] public string DisplayName;
            [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst = 80)] public string TypeName;
        }

        [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern IntPtr SHGetFileInfo(string path, uint attributes, out ShellFileInfo info, uint size, uint flags);
        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr icon);

        internal static FrameworkElement Create()
        {
            ShellFileInfo info;
            const uint fileAttributeDirectory = 0x10;
            const uint getIcon = 0x100;
            const uint smallIcon = 0x1;
            const uint useFileAttributes = 0x10;
            SHGetFileInfo("folder", fileAttributeDirectory, out info, (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(ShellFileInfo)), getIcon | smallIcon | useFileAttributes);
            if (info.Icon == IntPtr.Zero) return new TextBlock { Text = "选择", FontSize = 11 };
            try
            {
                BitmapSource source = Imaging.CreateBitmapSourceFromHIcon(info.Icon, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(18, 18));
                source.Freeze();
                return new Image { Source = source, Width = 18, Height = 18, Stretch = Stretch.Uniform };
            }
            finally { DestroyIcon(info.Icon); }
        }
    }
}
