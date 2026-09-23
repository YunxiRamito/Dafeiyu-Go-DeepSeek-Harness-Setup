using System;
using Microsoft.UI.Xaml;

namespace DshInstaller
{
    internal static class PlatformStyle
    {
        private static readonly bool _isWindows11 =
            ResolveWindows11();

        internal static bool IsWindows11
        {
            get { return _isWindows11; }
        }

        internal static CornerRadius ControlRadius
        {
            get { return new CornerRadius(_isWindows11 ? 4 : 0); }
        }

        internal static CornerRadius CardRadius
        {
            get { return new CornerRadius(_isWindows11 ? 8 : 0); }
        }

        internal static CornerRadius BadgeRadius
        {
            get { return new CornerRadius(_isWindows11 ? 4 : 0); }
        }

        internal static void ApplyApplicationResources(
            ResourceDictionary resources)
        {
            if (resources == null)
            {
                return;
            }

            resources["ControlCornerRadius"] = ControlRadius;
            resources["OverlayCornerRadius"] = CardRadius;
            resources["CardCornerRadius"] = CardRadius;
            resources["ButtonCornerRadius"] = ControlRadius;
        }

        private static bool ResolveWindows11()
        {
            if (String.Equals(
                DevOptions.PreviewStyle,
                "win10",
                StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (String.Equals(
                DevOptions.PreviewStyle,
                "win11",
                StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return Environment.OSVersion.Version.Build >= 22000;
        }
    }
}
