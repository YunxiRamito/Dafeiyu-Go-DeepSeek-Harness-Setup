using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using DshInstaller.Controls;

namespace DshInstaller.Pages
{
    /// <summary>
    /// 版本页:选 DSH 本体装哪个版本。
    ///
    /// 默认跟随官方 latest，用户也可选择 next / alpha 或具体版本。
    /// 版本标签会在安装步骤解析为实际版本；后端使用完整依赖包。
    /// 启动器那条还没做,所以那一项在界面上是禁用的(写明「暂未开放」)。
    ///
    /// 这里只负责"选",不做任何解析:选出来的值原样交给安装步骤,
    /// 安装步骤解析后交给完整本地部署或其它来源的 npm 安装。
    /// </summary>
    public sealed partial class VersionsPage : Page, IWizardPage
    {
        private const string SpecDefault = "";

        private const string SpecLatest = "latest";

        private const string SpecNext = "next";

        private const string SpecAlpha = "alpha";

        private const string SpecCustom = "__custom__";

        public VersionsPage()
        {
            InitializeComponent();
            BuildChoices();
            ApplyText();
            RestoreSelection();
        }

        public bool CanGoNext
        {
            get { return true; }
        }

        public bool OnNext()
        {
            string spec;
            string error;
            if (!TryResolveSpec(out spec, out error))
            {
                // 选了个装不出来的版本就别往下走 —— 与其在进度页报 npm 退出码,
                // 不如在这里把话说清楚。
                DshHint.Text = error;
                return false;
            }

            InstallSession session = InstallSession.Current;
            session.DshVersionSpec = spec;

            // 启动器那条通道还没做:这里明确留空,免得以后有人以为已经生效了
            session.LauncherVersionSpec = string.Empty;
            return true;
        }

        private void BuildChoices()
        {
            AddChoice(
                Localization.IsChinese ? "默认（跟随官方 latest）" : "Default (official latest)",
                SpecDefault);
            AddChoice(
                Localization.IsChinese ? "官方最新版本（latest）" : "Official latest version (latest)",
                SpecLatest);
            AddChoice(
                Localization.IsChinese ? "最新预览版" : "Latest preview",
                SpecNext);
            AddChoice(
                Localization.IsChinese ? "最新内测版" : "Latest alpha",
                SpecAlpha);
            AddChoice(
                Localization.IsChinese ? "自己填版本号" : "Type a version",
                SpecCustom);

            DshChannelBox.SelectedIndex = 0;
        }

        private void AddChoice(string text, string tag)
        {
            ComboBoxItem item = new ComboBoxItem();
            item.Content = text;
            item.Tag = tag;
            DshChannelBox.Items.Add(item);
        }

        private void ApplyText()
        {
            Scaffold.Title = Localization.IsChinese ? "版本" : "Versions";
            Scaffold.Subtitle = Localization.IsChinese
                ? "选择要装的 DSH 本体版本。启动器的版本通道还没做，先留在这里。"
                : "Pick which DSH core version to install. The launcher channel is not implemented yet.";

            // 和"组件"页同一档:都是"装之前选什么"
            Scaffold.SetStep(4);

            DshTitle.Text = Localization.IsChinese ? "DSH 本体" : "DSH core";
            DshDesc.Text = Localization.IsChinese
                ? "默认跟随官方 latest 标签，安装前会解析并显示实际版本。也可选择 next / alpha 或填写版本号。"
                : "The default follows the official latest tag. The actual version is resolved before installation. You can also choose next / alpha or an exact version.";

            DshVersionBox.PlaceholderText = Localization.IsChinese
                ? "例如 0.2.0-rc.2"
                : "e.g. 0.2.0-rc.2";

            LauncherTitle.Text = Localization.IsChinese
                ? "Dafeiyu-Go 启动器"
                : "Dafeiyu-Go launcher";
            LauncherDesc.Text = Localization.IsChinese
                ? "暂未开放：启动器的版本通道还没接上，这一项先禁用。装的时候用安装器自带的版本。"
                : "Not available yet: the launcher version channel is not wired up, so this stays disabled. The installer ships its own version.";

            LauncherApplyButton.Content = Localization.IsChinese ? "应用" : "Apply";

            UpdateHint();
        }

        private void RestoreSelection()
        {
            string existing = InstallSession.Current.DshVersionSpec;
            if (string.IsNullOrWhiteSpace(existing))
            {
                return;
            }

            string[] tags = { SpecLatest, SpecNext, SpecAlpha };
            for (int index = 0; index < tags.Length; index++)
            {
                if (string.Equals(tags[index], existing, System.StringComparison.OrdinalIgnoreCase))
                {
                    DshChannelBox.SelectedIndex = index + 1;
                    return;
                }
            }

            // 不是 tag,那就是用户自己填的版本号
            DshChannelBox.SelectedIndex = DshChannelBox.Items.Count - 1;
            DshVersionBox.Text = existing;
        }

        private void DshChannelBox_SelectionChanged(
            object sender,
            SelectionChangedEventArgs args)
        {
            ComboBoxItem item = DshChannelBox.SelectedItem as ComboBoxItem;
            string tag = item == null ? SpecDefault : (item.Tag as string ?? SpecDefault);
            bool custom = string.Equals(tag, SpecCustom, System.StringComparison.Ordinal);

            DshVersionBox.IsEnabled = custom;
            if (custom && string.IsNullOrWhiteSpace(DshVersionBox.Text))
            {
                DshVersionBox.Focus(FocusState.Programmatic);
            }

            UpdateHint();
        }

        private void UpdateHint()
        {
            ComboBoxItem item = DshChannelBox.SelectedItem as ComboBoxItem;
            string tag = item == null ? SpecDefault : (item.Tag as string ?? SpecDefault);

            if (string.Equals(tag, SpecCustom, System.StringComparison.Ordinal))
            {
                DshHint.Text = Localization.IsChinese
                    ? "后端完整包请填写具体版本号；其它下载源也支持 ^ / ~ 等版本范围。"
                    : "For a backend bundle, enter an exact version. Other sources also support ranges such as ^ / ~.";
                return;
            }

            if (string.Equals(tag, SpecDefault, System.StringComparison.Ordinal))
            {
                DshHint.Text = Localization.IsChinese
                    ? "安装官方 latest 标签当前对应的版本；后端源直接下载含依赖的完整本地包。"
                    : "Install the version currently tagged latest. The backend source downloads a complete dependency bundle.";
                return;
            }

            DshHint.Text = Localization.IsChinese
                ? "安装前解析此标签的实际版本。后端需要该版本的 Windows 完整包；暂未提供时会明确提示。"
                : "Resolve the actual version before installation. The backend needs a Windows bundle for that version and reports clearly when it is unavailable.";
        }

        /// <summary>把界面选的东西变成一个 npm 版本说明(空 = 用默认)。</summary>
        private bool TryResolveSpec(out string spec, out string error)
        {
            spec = SpecDefault;
            error = null;

            ComboBoxItem item = DshChannelBox.SelectedItem as ComboBoxItem;
            string tag = item == null ? SpecDefault : (item.Tag as string ?? SpecDefault);

            if (!string.Equals(tag, SpecCustom, System.StringComparison.Ordinal))
            {
                spec = tag;
                return true;
            }

            string typed = (DshVersionBox.Text ?? string.Empty).Trim();
            if (typed.Length == 0)
            {
                error = Localization.IsChinese
                    ? "选了「自己填版本号」，但框里是空的。填一个，或者改回默认。"
                    : "You picked “type a version” but the box is empty.";
                return false;
            }

            string cleaned = Sanitize(typed);
            if (cleaned == null)
            {
                error = Localization.IsChinese
                    ? "版本号里有不认识的字符。只支持字母、数字和 . - _ ^ ~ > < = * +"
                    : "The version contains unsupported characters. Only letters, digits and . - _ ^ ~ > < = * + are allowed.";
                return false;
            }

            spec = cleaned;
            return true;
        }

        private static string Sanitize(string value)
        {
            string trimmed = value.Trim();
            for (int index = 0; index < trimmed.Length; index++)
            {
                char c = trimmed[index];
                bool allowed = char.IsLetterOrDigit(c)
                    || c == '.' || c == '-' || c == '_'
                    || c == '^' || c == '~' || c == '>' || c == '<'
                    || c == '=' || c == '*' || c == '+';
                if (!allowed)
                {
                    return null;
                }
            }

            return trimmed;
        }
    }
}
