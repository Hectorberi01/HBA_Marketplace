using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace HBA.Admin.Desktop.Views;

public partial class PortefeuilleView : UserControl
{
    public PortefeuilleView() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
