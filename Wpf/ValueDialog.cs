using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace KillWind.Wpf
{
    internal sealed class ValueDialog : Window
    {
        private readonly TextBox newValue;
        private readonly int detectedValue;

        public string CurrentValue { get { return detectedValue.ToString(System.Globalization.CultureInfo.InvariantCulture); } }
        public string NewValue { get { return newValue.Text.Trim(); } }

        public ValueDialog(ScreenPoint point, int detected, string recognizedText)
        {
            detectedValue = detected;
            Title = "屏幕取值";
            Width = 460; Height = 245; ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = new SolidColorBrush(Color.FromRgb(32, 38, 45));
            Foreground = new SolidColorBrush(Color.FromRgb(228, 232, 236));
            var stack = new StackPanel { Margin = new Thickness(18) };
            stack.Children.Add(new TextBlock { Text = "已识别画面坐标 (" + point.x + ", " + point.y + ")", Foreground = Foreground, Margin = new Thickness(0, 0, 0, 6) });
            stack.Children.Add(new TextBlock { Text = "当前值：" + detectedValue.ToString(System.Globalization.CultureInfo.InvariantCulture), Foreground = new SolidColorBrush(Color.FromRgb(89, 211, 198)), FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
            stack.Children.Add(new TextBlock { Text = "OCR 识别：" + (String.IsNullOrWhiteSpace(recognizedText) ? "（数字）" : recognizedText.Replace("\r", " ").Replace("\n", " ")), Foreground = new SolidColorBrush(Color.FromRgb(157, 167, 177)), FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 0, 10) });
            newValue = Field(stack, "要修改为（只需输入目标值）");
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
            var cancel = new Button { Content = "取消", Width = 76, Margin = new Thickness(0, 0, 8, 0) };
            var apply = new Button { Content = "自动定位并修改", Width = 132, IsDefault = true };
            cancel.Click += (sender, args) => { DialogResult = false; Close(); };
            apply.Click += (sender, args) => { DialogResult = true; Close(); };
            buttons.Children.Add(cancel); buttons.Children.Add(apply); stack.Children.Add(buttons);
            Content = stack;
            Loaded += (sender, args) => newValue.Focus();
        }

        private static TextBox Field(Panel parent, string label)
        {
            parent.Children.Add(new TextBlock { Text = label, Foreground = new SolidColorBrush(Color.FromRgb(157, 167, 177)), FontSize = 11 });
            var field = new TextBox { Height = 28, Background = new SolidColorBrush(Color.FromRgb(20, 25, 30)), Foreground = new SolidColorBrush(Color.FromRgb(228, 232, 236)), Margin = new Thickness(0, 3, 0, 8), Padding = new Thickness(6, 3, 6, 3) };
            parent.Children.Add(field); return field;
        }
    }
}
