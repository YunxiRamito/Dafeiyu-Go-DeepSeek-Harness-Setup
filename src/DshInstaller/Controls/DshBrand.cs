using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace DshInstaller.Controls
{
    /// <summary>官方品牌标识的绘制入口。</summary>
    internal static class DshBrand
    {
        /// <summary>品牌标注解框(与网页 BrandWordmark 一致)。</summary>
        public const double NameAspect = 156.0 / 24.0;

        /// <summary>鲸鱼标注解框(与 favicon 一致)。</summary>
        public const double MarkAspect = 50.0 / 50.0;

        /// <summary>画鲸鱼标。颜色默认跟随主题文字色。</summary>
        public static Path Mark(double size, Brush fill = null)
        {
            return new Path
            {
                Width = size,
                Height = size,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Fill = fill ?? Theme.Brush("PrimaryTextBrush", Windows.UI.Color.FromArgb(255, 15, 15, 16)),
                Data = SvgPath.Parse(DshBrandData.MarkPath),
            };
        }

        /// <summary>
        /// 画 "deepseek HARNESS" 字样。
        /// 这是矢量字形,不是字体 —— 网页上它就是一个 SVG,所以别指望用字体凑出来。
        /// </summary>
        /// <param name="height">字高(逻辑像素)。宽度按 156:24 自动算。</param>
        public static Path Wordmark(double height, Brush fill = null)
        {
            GeometryGroup group = new GeometryGroup { FillRule = FillRule.EvenOdd };
            for (int index = 0; index < DshBrandData.Glyphs.Length; index++)
            {
                group.Children.Add(SvgPath.Parse(DshBrandData.Glyphs[index]));
            }

            return new Path
            {
                Height = height,
                Width = height * NameAspect,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Fill = fill ?? Theme.Brush("PrimaryTextBrush", Windows.UI.Color.FromArgb(255, 15, 15, 16)),
                Data = group,
            };
        }
    }
}
