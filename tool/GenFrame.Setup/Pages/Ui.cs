using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace GenFrame.Setup.Pages;

// Small helpers so the pages stay short; styles come from App.xaml.
static class Ui
{
    public static TextBlock Heading(string text) =>
        new() { Text = text, Style = (Style)Application.Current.FindResource("Heading") };

    public static TextBlock Muted(string text, double bottom = 12) =>
        new() { Text = text, Style = (Style)Application.Current.FindResource("Muted"), Margin = new Thickness(0, 0, 0, bottom) };

    public static TextBlock Text(string text, Brush? color = null) =>
        new() { Text = text, Foreground = color ?? Brush("TextBrush"), TextWrapping = TextWrapping.Wrap };

    public static Brush Brush(string key) => (Brush)Application.Current.FindResource(key);

    public static Button Button(string text, RoutedEventHandler click, bool primary = false)
    {
        var b = new Button { Content = text };
        if (primary) {
            b.Style = (Style)Application.Current.FindResource("PrimaryButton");
        }
        b.Click += click;
        return b;
    }

    public static Border Card(UIElement content, double width = double.NaN) =>
        new() { Child = content, Width = width, Style = (Style)Application.Current.FindResource("Card") };

    // One line of a list: label left, status (or a button) right.
    public static Border Row(string label, UIElement right)
    {
        var text = Text(label);
        text.VerticalAlignment = VerticalAlignment.Center;
        return Row(text, right);
    }

    public static Border Row(UIElement left, UIElement right)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(left);
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        return new Border {
            Child = grid,
            Padding = new Thickness(0, 8, 0, 8),
            BorderBrush = Brush("BorderBrush"),
            BorderThickness = new Thickness(0, 0, 0, 1),
        };
    }

    public static StackPanel Buttons(params UIElement[] buttons)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
        foreach (var b in buttons) {
            panel.Children.Add(b);
        }
        return panel;
    }

    public static void Error(Exception e) =>
        MessageBox.Show(e.Message, "GEN·FRAME Einrichtung", MessageBoxButton.OK, MessageBoxImage.Warning);
}
