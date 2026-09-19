using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using DshInstaller.Controls;

namespace DshInstaller.Pages
{
    /// <summary>选择安装范围。用自绘卡片,不用 RadioButton(它自带圆圈会压到卡片上)。</summary>
    public sealed partial class ScopePage : Page, IWizardPage
    {
        private SelectableCard _userCard;
        private SelectableCard _machineCard;

        public ScopePage()
        {
            InitializeComponent();
            BuildCards();
            ApplyText();
        }

        public bool CanGoNext
        {
            get { return true; }
        }

        public bool OnNext()
        {
            InstallSession.Current.Scope = _machineCard != null && _machineCard.IsSelected
                ? InstallScope.AllUsers
                : InstallScope.CurrentUser;
            return true;
        }

        private void BuildCards()
        {
            _userCard = new SelectableCard();
            _userCard.Selected += delegate { SelectCard(_userCard); };

            _machineCard = new SelectableCard();
            _machineCard.Selected += delegate { SelectCard(_machineCard); };

            CardsHost.Children.Add(_userCard);
            CardsHost.Children.Add(_machineCard);
        }

        private void SelectCard(SelectableCard card)
        {
            _userCard.SetSelectedQuietly(ReferenceEquals(card, _userCard));
            _machineCard.SetSelectedQuietly(ReferenceEquals(card, _machineCard));

            bool machine = ReferenceEquals(card, _machineCard);
            AdminHint.Visibility = machine ? Visibility.Visible : Visibility.Collapsed;
            InstallSession.Current.Scope = machine ? InstallScope.AllUsers : InstallScope.CurrentUser;
            ApplyAutostartDefault(machine);
        }

        /// <summary>
        /// 让"开机自启"的默认值跟着范围走:
        ///   给所有用户装 —— 本来就要提权,默认勾上;
        ///   只给当前用户装 —— 不勾,这样"仅为我安装"才是真的**全程零 UAC**。
        ///
        /// 用户已经在启动器那一页做过选择的话就别动他了(来回翻页不该把勾选冲掉)。
        /// </summary>
        private static void ApplyAutostartDefault(bool machine)
        {
            if (InstallSession.Current.AutostartChosen)
            {
                return;
            }

            InstallSession.Current.EnableAutostart = machine;
        }

        private void ApplyText()
        {
            Scaffold.Title = Localization.T("scope.title");
            Scaffold.Subtitle = Localization.T("scope.desc");
            Scaffold.SetStep(0);

            _userCard.CardTitle = Localization.T("scope.user");
            _userCard.Description = Localization.T("scope.user.desc");
            _userCard.CardPath = InstallSession.UserRoot;

            _machineCard.CardTitle = Localization.T("scope.machine");
            _machineCard.Description = Localization.T("scope.machine.desc");
            _machineCard.CardPath = InstallSession.MachineRoot;

            AdminHintText.Text = Localization.IsChinese ? "需要 UAC 权限；如不了解其含义，请在随后出现的系统安全提示中选择“是”。" : "Administrator (UAC) rights are required. If you are unsure what this means, choose “Yes” in the security prompt that follows.";

            // 默认选"仅为我安装"
            bool machine = InstallSession.Current.Scope == InstallScope.AllUsers;
            _userCard.SetSelectedQuietly(!machine);
            _machineCard.SetSelectedQuietly(machine);
            AdminHint.Visibility = machine ? Visibility.Visible : Visibility.Collapsed;
            ApplyAutostartDefault(machine);
        }
    }
}
