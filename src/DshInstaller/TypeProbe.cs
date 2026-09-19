using System;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml.Media;

namespace DshInstaller
{
    internal static class TypeProbe
    {
        public static void Check()
        {
            var a = typeof(MicaBackdrop);
            var b = typeof(DesktopAcrylicBackdrop);
            var c = typeof(SolidColorBrush);
            var d = typeof(Microsoft.UI.Xaml.Controls.Button);
            Console.WriteLine(a + " " + b + " " + c + " " + d);
        }
    }
}