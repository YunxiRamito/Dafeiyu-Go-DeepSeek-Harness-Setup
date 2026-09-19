using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace DshInstaller
{
    /// <summary>
    /// 取主题资源的小工具。
    /// 页面里到处都要拿颜色和圆角,集中一层,拿不到就给兜底色(别让缺资源把界面搞崩)。
    /// </summary>
    internal static class Theme
    {
        public static Brush Brush(string key, Windows.UI.Color fallback)
        {
            try
            {
                object value = Application.Current.Resources[key];
                Brush brush = value as Brush;
                if (brush != null)
                {
                    return brush;
                }
            }
            catch
            {
            }

            return new SolidColorBrush(fallback);
        }

        public static CornerRadius CornerRadius(string key, double fallback)
        {
            try
            {
                object value = Application.Current.Resources[key];
                if (value is CornerRadius radius)
                {
                    return radius;
                }
            }
            catch
            {
            }

            return new CornerRadius(fallback);
        }

        public static double Value(string key, double fallback)
        {
            try
            {
                object value = Application.Current.Resources[key];
                if (value is double number)
                {
                    return number;
                }
            }
            catch
            {
            }

            return fallback;
        }
    }
}
