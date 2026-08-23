using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace HBA.Admin.Desktop.Views;

public partial class LivreursView : UserControl
{
    public LivreursView() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
