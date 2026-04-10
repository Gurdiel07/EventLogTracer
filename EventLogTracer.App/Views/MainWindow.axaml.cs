using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using EventLogTracer.App.ViewModels;

namespace EventLogTracer.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private async void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
            return;

        var isCtrlPressed = e.KeyModifiers.HasFlag(KeyModifiers.Control);

        if (isCtrlPressed)
        {
            switch (e.Key)
            {
                case Key.D1:
                    vm.NavigateToPage("Dashboard");
                    e.Handled = true;
                    return;
                case Key.D2:
                    vm.NavigateToPage("EventViewer");
                    e.Handled = true;
                    return;
                case Key.D3:
                    vm.NavigateToPage("Timeline");
                    e.Handled = true;
                    return;
                case Key.D4:
                    vm.NavigateToPage("Alerts");
                    e.Handled = true;
                    return;
                case Key.D5:
                    vm.NavigateToPage("Search");
                    e.Handled = true;
                    return;
                case Key.D6:
                    vm.NavigateToPage("Settings");
                    e.Handled = true;
                    return;
                case Key.M:
                    vm.ToggleMonitoringCommand.Execute(null);
                    e.Handled = true;
                    return;
                case Key.F:
                    e.Handled = FocusCurrentSearchBox(vm);
                    return;
                case Key.E:
                    await vm.QuickExportCurrentEventsAsync();
                    e.Handled = true;
                    return;
            }
        }

        switch (e.Key)
        {
            case Key.F5:
                await vm.RefreshCurrentPageAsync();
                e.Handled = true;
                break;
            case Key.Escape:
                vm.ClearCurrentSelectionOrClosePanels();
                e.Handled = true;
                break;
        }
    }

    private bool FocusCurrentSearchBox(MainWindowViewModel vm)
    {
        var controlName = vm.CurrentPage switch
        {
            EventViewerViewModel => "EventViewerFilterTextBox",
            SearchViewModel      => "SearchQueryTextBox",
            _                    => null
        };

        if (string.IsNullOrWhiteSpace(controlName))
            return false;

        var textBox = this.GetVisualDescendants()
            .OfType<TextBox>()
            .FirstOrDefault(tb => tb.Name == controlName);

        if (textBox is null)
            return false;

        var focused = textBox.Focus();
        if (focused)
            textBox.SelectAll();

        return focused;
    }
}
