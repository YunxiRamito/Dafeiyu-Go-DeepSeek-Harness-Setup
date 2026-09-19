using System;

namespace DshInstaller.Shared
{
    /// <summary>
    /// 共享库里的文案选词。
    ///
    /// 共享库不该依赖 UI 的 Localization,但组件检测结果(名称、状态说明)是要显示给用户的,
    /// 所以在这儿放一个最小的开关:UI 启动或切语言时设 <see cref="UseChinese"/>,这里按它选词。
    /// </summary>
    public static class SharedText
    {
        /// <summary>当前是不是中文界面。</summary>
        public static bool UseChinese { get; set; } = true;

        /// <summary>按当前语言二选一。</summary>
        public static string T(string chinese, string english)
        {
            return UseChinese ? chinese : english;
        }
    }
}
