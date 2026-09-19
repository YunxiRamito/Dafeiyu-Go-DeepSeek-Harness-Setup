using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using DshInstaller.Controls;
using DshInstaller.Shared;
using DshInstaller.Shared.Install;

namespace DshInstaller.Pages
{
    /// <summary>
    /// 卸载第一步:确定卸载吗。
    ///
    /// 这一页只做一件事 —— 让用户确认。删什么、留什么放到下一页,
    /// 免得他还没决定要不要卸,就先被一堆勾选框问住。
    /// </summary>
    public sealed partial class UninstallPage : Page, IWizardPage
    {
        public UninstallPage()
        {
            InitializeComponent();
            ApplyText();
        }

        public bool CanGoNext
        {
            get { return true; }
        }

        public bool OnNext()
        {
            InstallSession.Current.Mode = SessionMode.Uninstall;
            return true;
        }

        private void ApplyText()
        {
            Scaffold.Title = Localization.T("uninstall.title");
            Scaffold.Subtitle = Localization.T("uninstall.confirm.desc");
            // 卸载流程只有三步:确定 -> 选择内容 -> 完成。步骤条也跟着换成 3 格。
            Scaffold.SetSteps(3, 0);

            QuestionText.Text = Localization.T("uninstall.confirm.question");
            DetailText.Text = Localization.T("uninstall.confirm.detail");
            KeepLabel.Text = Localization.T("uninstall.confirm.keep");
            KeepText.Text = Localization.T("uninstall.confirm.keep.desc");
        }
    }
}
