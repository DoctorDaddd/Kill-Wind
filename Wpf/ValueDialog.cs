using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace KillWind.Wpf
{
    internal sealed class ValueDialog : Window
    {
        private readonly TextBox currentValue;
        private readonly TextBox newValue;

        public string CurrentValue { get { return currentValue.Text.Trim(); } }
        public string NewValue { get { return newValue.Text.Trim(); } }

        public ValueDialog(ScreenPoint point)
        {
            Title = "屏幕取值";
            Width = 420; Height = 245; ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = new SolidColorBrush(Color.FromRgb(32, 38, 45));
            Foreground = new SolidColorBrush(Color.FromRgb(228, 232, 236));
            var stack = new StackPanel { Margin = new Thickness(18) };
            stack.Children.Add(new TextBlock { Text = "已捕获游戏画面坐标 (" + point.x + ", " + point.y + ")", Foreground = Foreground, Margin = new Thickness(0, 0, 0, 12) });
            currentValue = Field(stack, "画面上的当前整数");
            newValue = Field(stack, "要修改为");
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
            var cancel = new Button { Content = "取消", Width = 76, Margin = new Thickness(0, 0, 8, 0) };
            var apply = new Button { Content = "查找并修改", Width = 110, IsDefault = true };
            cancel.Click += (sender, args) => { DialogResult = false; Close(); };
            apply.Click += (sender, args) => { DialogResult = true; Close(); };
            buttons.Children.Add(cancel); buttons.Children.Add(apply); stack.Children.Add(buttons);
            Content = stack;
            Loaded += (sender, args) => currentValue.Focus();
        }

        private static TextBox Field(Panel parent, string label)
        {
            parent.Children.Add(new TextBlock { Text = label, Foreground = new SolidColorBrush(Color.FromRgb(157, 167, 177)), FontSize = 11 });
            var field = new TextBox { Height = 28, Background = new SolidColorBrush(Color.FromRgb(20, 25, 30)), Foreground = new SolidColorBrush(Color.FromRgb(228, 232, 236)), Margin = new Thickness(0, 3, 0, 8), Padding = new Thickness(6, 3, 6, 3) };
            parent.Children.Add(field); return field;
        }
    }
}
