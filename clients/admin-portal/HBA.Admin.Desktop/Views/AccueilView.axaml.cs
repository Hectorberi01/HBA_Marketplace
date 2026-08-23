using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace HBA.Admin.Desktop.Views;

public partial class AccueilView : UserControl
{
    public AccueilView() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
