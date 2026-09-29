using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using DshInstaller.Controls;

namespace DshInstaller.Pages
{
    /// <summary>
    /// 版本页:选 DSH 本体装哪个版本。
    ///
    /// 为什么要有这一页:以前本体版本**写死在代码里**(WellKnown.DshPackageVersion,
    /// 形如 ^0.1.5-rc.1),用户想试预览版只能等我们再发一版安装器。
    /// 现在这条通道交给用户选,和启动器的通道对齐 ——
    /// 启动器那条还没做,所以那一项在界面上是禁用的(写明「暂未开放」)。
    ///
    /// 这里只负责"选",不做任何解析:选出来的值原样交给安装步骤,
    /// 由它拼进 npm install(并再筛一遍字符)。
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
                Localization.IsChinese ? "默认(跟着安装器走)" : "Default (as shipped)",
                SpecDefault);
            AddChoice(
                Localization.IsChinese ? "最新正式版" : "Latest stable",
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
                ? "默认是安装器推荐的那个版本。想尝鲜可以选预览版 / 内测版，或者自己填一个版本号。"
                : "The default is the version this installer recommends. You can pick preview/alpha or type a version.";

            DshVersionBox.PlaceholderText = Localization.IsChinese
                ? "例如 0.2.0-rc.1 或 ^0.1.5"
                : "e.g. 0.2.0-rc.1 or ^0.1.5";

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
                    ? "只能填字母、数字和 . - _ ^ ~ > < = * + —— 版本号会被拼进安装命令，所以别的字符一律不收。"
                    : "Only letters, digits and . - _ ^ ~ > < = * + are allowed, because the value goes into the install command.";
                return;
            }

            if (string.Equals(tag, SpecDefault, System.StringComparison.Ordinal))
            {
                DshHint.Text = Localization.IsChinese
                    ? "用安装器自带的推荐版本，最稳。"
                    : "Use the version this installer ships with. Safest choice.";
                return;
            }

            DshHint.Text = Localization.IsChinese
                ? "会按这个标签去装最新的一版；装出来可能比安装器自带的更新，出问题可以再用「修复安装」退回推荐版本。"
                : "Installs the newest release under this tag. It may be newer than what the installer ships with.";
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
