using Avalonia;
using Avalonia.Fonts.Inter;

namespace HBA.Admin.Desktop;

internal static class Program
{
    /// <summary>
    /// `[STAThread]` EST OBLIGATOIRE, ET SON ABSENCE NE SE VOIT QUE SUR WINDOWS.
    ///
    /// Les boîtes de dialogue système (sélecteur de fichier, presse-papiers) sont
    /// des composants COM à cloisonnement mono-thread. Sans cet attribut,
    /// l'application démarre, affiche ses écrans, et lève au premier appel —
    /// c'est-à-dire longtemps après la compilation, sur la machine de quelqu'un
    /// d'autre. Sur Linux et macOS, l'attribut est simplement ignoré.
    /// </summary>
    [STAThread]
    public static void Main(string[] args)
        => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    /// <summary>
    /// Point d'entrée exigé par les outils de conception Avalonia.
    /// </summary>
    /// <remarks>
    /// NE PAS RENOMMER, NE PAS RENDRE PRIVÉE.
    ///
    /// Le prévisualiseur XAML la cherche PAR SON NOM, par réflexion. Renommée,
    /// tout compile et le volet d'aperçu affiche « no XAML preview available »
    /// sans dire pourquoi.
    /// </remarks>
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
