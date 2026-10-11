using System.Windows;

namespace ReforgeX;

public partial class ConfirmWindow : Window
{
    public enum Answer { Cancel, Accept, Extra }

    readonly string _fallback = "";
    public Answer Result { get; private set; } = Answer.Cancel;

    public ConfirmWindow(string heading, string message, string detail, string accept, bool danger)
    {
        InitializeComponent();
        WindowTheme.Apply(this);
        Title = heading;
        Heading.Text = heading;
        Message.Text = message;
        if (string.IsNullOrWhiteSpace(detail))
            Detail.Visibility = Visibility.Collapsed;
        else
            Detail.Text = detail;
        AcceptBtn.Content = accept;
        if (danger)
            AcceptBtn.Style = (Style)FindResource("DangerButton");
    }

    internal void OfferFolders(IReadOnlyList<ScanRoot> roots, string? current)
    {
        if (roots.Count <= 1)
        {
            Choice.Visibility = Visibility.Collapsed;
            ChoiceLabel.Visibility = Visibility.Collapsed;
            return;
        }
        ChoiceLabel.Visibility = Visibility.Visible;
        Choice.Visibility = Visibility.Visible;
        Choice.ItemsSource = roots;
        var pick = roots.FirstOrDefault(r => string.Equals(r.Path, current, StringComparison.OrdinalIgnoreCase));
        Choice.SelectedItem = pick ?? roots[0];
    }

    internal string ChosenFolder(IReadOnlyList<ScanRoot> roots)
    {
        if (Choice.Visibility == Visibility.Visible && Choice.SelectedItem is ScanRoot root)
            return root.Path;
        if (roots.Count == 1)
            return roots[0].Path;
        return _fallback;
    }

    internal void ShowExtra(string label)
    {
        ExtraBtn.Visibility = Visibility.Visible;
        ExtraBtn.Content = label;
    }

    void Accept_Click(object sender, RoutedEventArgs e)
    {
        Result = Answer.Accept;
        DialogResult = true;
    }

    void Extra_Click(object sender, RoutedEventArgs e)
    {
        Result = Answer.Extra;
        DialogResult = true;
    }

    void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Result = Answer.Cancel;
        DialogResult = false;
    }
}
