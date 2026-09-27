using System;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using DshInstaller.Shared;

namespace DshInstaller
{
    /// <summary>
    /// "导出安装日志"按钮的动作。失败页和完成页共用这一份,免得两处各写一遍。
    ///
    /// 三条体验上的死规矩(用户"点一下没反应"就是从这里来的):
    ///   · 点下去**立刻**有字变——打包要几秒,不能像卡死;
    ///   · 结束以后**说出 zip 在哪**,并且给一个"打开文件夹";
    ///   · 失败也说话,不吞。
    /// </summary>
    internal static class LogExportUi
    {
        private static bool _running;

        internal static void Export(FrameworkElement owner, TextBlock status)
        {
            if (owner == null || _running)
            {
                return;
            }

            _running = true;
            SetStatus(status, Localization.T("log.export.working"));

            DispatcherQueue queue = owner.DispatcherQueue;

            Task.Run(delegate
            {
                LogBundleResult result;

                try
                {
                    result = LogBundle.Create(null);
                }
                catch (Exception exception)
                {
                    result = new LogBundleResult { Error = exception.Message };
                }

                if (queue == null)
                {
                    _running = false;
                    return;
                }

                queue.TryEnqueue(delegate
                {
                    _running = false;
                    Finish(owner, status, result);
                });
            });
        }

        private static void Finish(FrameworkElement owner, TextBlock status, LogBundleResult result)
        {
            if (result == null)
            {
                SetStatus(status, Localization.T("log.export.failed"));
                return;
            }

            if (!result.Succeeded)
            {
                SetStatus(status, string.IsNullOrWhiteSpace(result.Error)
                    ? Localization.T("log.export.failed")
                    : result.Error);
                return;
            }

            SetStatus(status, Localization.T("log.export.saved") + " " + result.Path);
            Confirm(owner, result.Path);
        }

        /// <summary>告诉用户存哪了,顺手给个"打开文件夹"。</summary>
        private static void Confirm(FrameworkElement owner, string path)
        {
            try
            {
                ContentDialog dialog = new ContentDialog
                {
                    XamlRoot = owner.XamlRoot,
                    Title = Localization.T("log.export.title"),
                    Content = new TextBlock { Text = path, TextWrapping = TextWrapping.Wrap },
                    PrimaryButtonText = Localization.T("log.export.open"),
                    CloseButtonText = Localization.T("btn.finish"),
                    DefaultButton = ContentDialogButton.Primary,
                };

                dialog.PrimaryButtonClick += delegate { Reveal(path); };
                _ = dialog.ShowAsync();
            }
            catch
            {
            }
        }

        private static void Reveal(string path)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = "/select,\"" + path + "\"",
                    UseShellExecute = true,
                });
            }
            catch
            {
            }
        }

        private static void SetStatus(TextBlock status, string text)
        {
            if (status == null)
            {
                return;
            }

            status.Text = text ?? string.Empty;
            status.Visibility = string.IsNullOrWhiteSpace(text)
                ? Visibility.Collapsed
                : Visibility.Visible;
        }
    }
}
