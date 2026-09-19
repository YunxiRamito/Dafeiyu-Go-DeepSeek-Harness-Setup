using System;
using Microsoft.UI.Xaml;

namespace DshInstaller
{
    public partial class App : Application
    {
        private MainWindow _window;

        /// <summary>当前主窗口。页面之间要靠它跳转,所以留个静态入口。</summary>
        internal static MainWindow MainWindowInstance { get; private set; }

        public App()
        {
            InitializeComponent();
        }

        protected override void OnLaunched(LaunchActivatedEventArgs arguments)
        {
            try
            {
                _window = new MainWindow();
                MainWindowInstance = _window;
                _window.Activate();
            }
            catch (Exception exception)
            {
                try
                {
                    string directory = System.IO.Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "DeepSeekHarness");
                    System.IO.Directory.CreateDirectory(directory);
                    System.IO.File.AppendAllText(
                        System.IO.Path.Combine(directory, "installer-crash.log"),
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  [OnLaunched] " + exception
                        + Environment.NewLine + Environment.NewLine,
                        new System.Text.UTF8Encoding(false));
                }
                catch
                {
                }

                throw;
            }
        }
    }
}
