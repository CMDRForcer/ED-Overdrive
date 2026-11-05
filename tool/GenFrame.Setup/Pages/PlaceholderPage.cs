using System.Windows;
using System.Windows.Controls;

namespace GenFrame.Setup.Pages;

// Pages planned in docs/plan.md but not built yet.
static class PlaceholderPage
{
    public static UIElement Build(string title, string plan)
    {
        var page = new StackPanel();
        page.Children.Add(Ui.Heading(title));
        page.Children.Add(Ui.Muted(plan));
        page.Children.Add(Ui.Muted("Noch nicht umgesetzt – folgt nach dem Grundgerüst."));
        return page;
    }
}
