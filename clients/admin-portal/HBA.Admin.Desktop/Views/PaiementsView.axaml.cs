using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace HBA.Admin.Desktop.Views;

public partial class PaiementsView : UserControl
{
    public PaiementsView() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
