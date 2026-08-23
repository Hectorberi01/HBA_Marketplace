using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace HBA.Admin.Desktop.Views;

public partial class RecommandationsView : UserControl
{
    public RecommandationsView() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
