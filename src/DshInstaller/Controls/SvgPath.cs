using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace DshInstaller.Controls
{
    /// <summary>
    /// SVG 路径迷你语法的解析器。
    ///
    /// 为什么自己写:WinUI 没有公开的路径字符串解析入口。
    ///  - XamlReader.Load 造出来的 Geometry 属于另一个 namescope,赋给别的 Path 会抛异常;
    ///  - XamlBindingHelper.ConvertValue 能造出来,但丢了 viewBox 信息,Stretch 会按错误的
    ///    包围盒缩放,图案偏到一边。
    /// 自己解析成 PathGeometry,坐标系和 bounds 都是我们说了算。
    ///
    /// 支持绝对与相对命令:M L H V C S Q T A Z(大小写)。
    /// </summary>
    internal static class SvgPath
    {
        public static PathGeometry Parse(string data)
        {
            PathGeometry geometry = new PathGeometry();
            geometry.FillRule = FillRule.EvenOdd;

            if (string.IsNullOrEmpty(data))
            {
                return geometry;
            }

            int index = 0;
            char command = '\0';
            char previousCommand = '\0';

            double cx = 0;
            double cy = 0;
            double startX = 0;
            double startY = 0;

            // 上一个三次/二次贝塞尔的控制点,给 S/T 的镜像用
            double lastControlX = 0;
            double lastControlY = 0;

            PathFigure figure = null;

            while (index < data.Length)
            {
                SkipSeparators(data, ref index);
                if (index >= data.Length)
                {
                    break;
                }

                if (char.IsLetter(data[index]))
                {
                    command = data[index];
                    index++;
                }
                else if (command == '\0')
                {
                    // 没有命令直接来数字,按上一个命令重复处理
                    break;
                }

                bool relative = char.IsLower(command);
                char upper = char.ToUpperInvariant(command);

                if (upper == 'Z')
                {
                    if (figure != null)
                    {
                        figure.IsClosed = true;
                        figure = null;
                    }

                    cx = startX;
                    cy = startY;
                    previousCommand = command;
                    continue;
                }

                // 一个命令后面可以跟多组参数
                bool first = true;
                while (true)
                {
                    SkipSeparators(data, ref index);
                    if (index >= data.Length)
                    {
                        break;
                    }

                    // 遇到新命令字母就停,交回外层
                    if (char.IsLetter(data[index]) && first == false)
                    {
                        break;
                    }

                    if (ContainsLetter(data, index))
                    {
                        break;
                    }

                    double x1, y1, x2, y2, x, y;

                    switch (upper)
                    {
                        case 'M':
                            if (!ReadPoint(data, ref index, out x, out y))
                            {
                                return geometry;
                            }

                            if (relative)
                            {
                                x += cx;
                                y += cy;
                            }

                            if (first)
                            {
                                figure = new PathFigure
                                {
                                    StartPoint = new Point(x, y),
                                    IsClosed = false,
                                    IsFilled = true,
                                };
                                geometry.Figures.Add(figure);
                                startX = x;
                                startY = y;
                            }
                            else
                            {
                                // 后续坐标对按 L 处理
                                if (figure != null)
                                {
                                    figure.Segments.Add(new LineSegment { Point = new Point(x, y) });
                                }
                            }

                            cx = x;
                            cy = y;
                            break;

                        case 'L':
                            if (!ReadPoint(data, ref index, out x, out y))
                            {
                                return geometry;
                            }

                            if (relative)
                            {
                                x += cx;
                                y += cy;
                            }

                            if (figure == null)
                            {
                                figure = NewFigure(cx, cy, geometry);
                            }

                            figure.Segments.Add(new LineSegment { Point = new Point(x, y) });
                            cx = x;
                            cy = y;
                            break;

                        case 'H':
                            if (!ReadNumber(data, ref index, out x))
                            {
                                return geometry;
                            }

                            if (relative)
                            {
                                x += cx;
                            }

                            if (figure == null)
                            {
                                figure = NewFigure(cx, cy, geometry);
                            }

                            figure.Segments.Add(new LineSegment { Point = new Point(x, cy) });
                            cx = x;
                            break;

                        case 'V':
                            if (!ReadNumber(data, ref index, out y))
                            {
                                return geometry;
                            }

                            if (relative)
                            {
                                y += cy;
                            }

                            if (figure == null)
                            {
                                figure = NewFigure(cx, cy, geometry);
                            }

                            figure.Segments.Add(new LineSegment { Point = new Point(cx, y) });
                            cy = y;
                            break;

                        case 'C':
                            if (!ReadPoint(data, ref index, out x1, out y1)
                                || !ReadPoint(data, ref index, out x2, out y2)
                                || !ReadPoint(data, ref index, out x, out y))
                            {
                                return geometry;
                            }

                            if (relative)
                            {
                                x1 += cx;
                                y1 += cy;
                                x2 += cx;
                                y2 += cy;
                                x += cx;
                                y += cy;
                            }

                            if (figure == null)
                            {
                                figure = NewFigure(cx, cy, geometry);
                            }

                            figure.Segments.Add(new BezierSegment
                            {
                                Point1 = new Point(x1, y1),
                                Point2 = new Point(x2, y2),
                                Point3 = new Point(x, y),
                            });

                            lastControlX = x2;
                            lastControlY = y2;
                            cx = x;
                            cy = y;
                            break;

                        case 'S':
                            if (!ReadPoint(data, ref index, out x2, out y2)
                                || !ReadPoint(data, ref index, out x, out y))
                            {
                                return geometry;
                            }

                            // 第一个控制点取上一个控制点关于当前点的镜像
                            if (previousCommand == 'C' || previousCommand == 'c'
                                || previousCommand == 'S' || previousCommand == 's')
                            {
                                x1 = 2 * cx - lastControlX;
                                y1 = 2 * cy - lastControlY;
                            }
                            else
                            {
                                x1 = cx;
                                y1 = cy;
                            }

                            if (relative)
                            {
                                x2 += cx;
                                y2 += cy;
                                x += cx;
                                y += cy;
                            }

                            if (figure == null)
                            {
                                figure = NewFigure(cx, cy, geometry);
                            }

                            figure.Segments.Add(new BezierSegment
                            {
                                Point1 = new Point(x1, y1),
                                Point2 = new Point(x2, y2),
                                Point3 = new Point(x, y),
                            });

                            lastControlX = x2;
                            lastControlY = y2;
                            cx = x;
                            cy = y;
                            break;

                        case 'Q':
                            if (!ReadPoint(data, ref index, out x1, out y1)
                                || !ReadPoint(data, ref index, out x, out y))
                            {
                                return geometry;
                            }

                            if (relative)
                            {
                                x1 += cx;
                                y1 += cy;
                                x += cx;
                                y += cy;
                            }

                            if (figure == null)
                            {
                                figure = NewFigure(cx, cy, geometry);
                            }

                            figure.Segments.Add(new QuadraticBezierSegment
                            {
                                Point1 = new Point(x1, y1),
                                Point2 = new Point(x, y),
                            });

                            lastControlX = x1;
                            lastControlY = y1;
                            cx = x;
                            cy = y;
                            break;

                        case 'T':
                            if (!ReadPoint(data, ref index, out x, out y))
                            {
                                return geometry;
                            }

                            if (previousCommand == 'Q' || previousCommand == 'q'
                                || previousCommand == 'T' || previousCommand == 't')
                            {
                                x1 = 2 * cx - lastControlX;
                                y1 = 2 * cy - lastControlY;
                            }
                            else
                            {
                                x1 = cx;
                                y1 = cy;
                            }

                            if (relative)
                            {
                                x += cx;
                                y += cy;
                            }

                            if (figure == null)
                            {
                                figure = NewFigure(cx, cy, geometry);
                            }

                            figure.Segments.Add(new QuadraticBezierSegment
                            {
                                Point1 = new Point(x1, y1),
                                Point2 = new Point(x, y),
                            });

                            lastControlX = x1;
                            lastControlY = y1;
                            cx = x;
                            cy = y;
                            break;

                        case 'A':
                            {
                                double rx, ry, rotation, largeArc, sweep;
                                if (!ReadNumber(data, ref index, out rx)
                                    || !ReadNumber(data, ref index, out ry)
                                    || !ReadNumber(data, ref index, out rotation)
                                    || !ReadNumber(data, ref index, out largeArc)
                                    || !ReadNumber(data, ref index, out sweep)
                                    || !ReadPoint(data, ref index, out x, out y))
                                {
                                    return geometry;
                                }

                                if (relative)
                                {
                                    x += cx;
                                    y += cy;
                                }

                                if (figure == null)
                                {
                                    figure = NewFigure(cx, cy, geometry);
                                }

                                figure.Segments.Add(new ArcSegment
                                {
                                    Size = new Size(rx, ry),
                                    RotationAngle = rotation,
                                    IsLargeArc = largeArc != 0,
                                    SweepDirection = sweep != 0
                                        ? SweepDirection.Clockwise
                                        : SweepDirection.Counterclockwise,
                                    Point = new Point(x, y),
                                });

                                cx = x;
                                cy = y;
                                break;
                            }

                        default:
                            // 不认识的命令:跳过它的参数直到下一个命令字母
                            while (index < data.Length && !char.IsLetter(data[index]))
                            {
                                index++;
                            }

                            break;
                    }

                    previousCommand = command;
                    first = false;

                    // M 之后的多组坐标按 L 处理
                    if (upper == 'M')
                    {
                        upper = relative ? 'l' : 'L';
                        command = upper;
                    }
                }
            }

            return geometry;
        }

        private static PathFigure NewFigure(double x, double y, PathGeometry geometry)
        {
            PathFigure figure = new PathFigure
            {
                StartPoint = new Point(x, y),
                IsClosed = false,
                IsFilled = true,
            };
            geometry.Figures.Add(figure);
            return figure;
        }

        private static bool ContainsLetter(string data, int index)
        {
            return index < data.Length && char.IsLetter(data[index]);
        }

        private static void SkipSeparators(string data, ref int index)
        {
            while (index < data.Length
                && (char.IsWhiteSpace(data[index]) || data[index] == ','))
            {
                index++;
            }
        }

        private static bool ReadPoint(string data, ref int index, out double x, out double y)
        {
            x = 0;
            y = 0;
            return ReadNumber(data, ref index, out x) && ReadNumber(data, ref index, out y);
        }

        /// <summary>读一个数字,支持负号、小数点和科学计数法。</summary>
        private static bool ReadNumber(string data, ref int index, out double value)
        {
            value = 0;
            SkipSeparators(data, ref index);

            int start = index;
            if (index < data.Length && (data[index] == '-' || data[index] == '+'))
            {
                index++;
            }

            while (index < data.Length && char.IsDigit(data[index]))
            {
                index++;
            }

            if (index < data.Length && data[index] == '.')
            {
                index++;
                while (index < data.Length && char.IsDigit(data[index]))
                {
                    index++;
                }
            }

            // 科学计数法:1e-5
            if (index < data.Length && (data[index] == 'e' || data[index] == 'E'))
            {
                int save = index;
                index++;
                if (index < data.Length && (data[index] == '-' || data[index] == '+'))
                {
                    index++;
                }

                if (index < data.Length && char.IsDigit(data[index]))
                {
                    while (index < data.Length && char.IsDigit(data[index]))
                    {
                        index++;
                    }
                }
                else
                {
                    index = save;
                }
            }

            if (index == start)
            {
                return false;
            }

            return double.TryParse(
                data.Substring(start, index - start),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out value);
        }
    }
}
