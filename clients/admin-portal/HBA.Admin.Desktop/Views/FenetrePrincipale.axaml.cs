using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using HBA.Admin.Desktop.ViewModels;

namespace HBA.Admin.Desktop.Views;

public partial class FenetrePrincipale : Window
{
    public FenetrePrincipale() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    /// <summary>
    /// UN GESTIONNAIRE D'ÉVÉNEMENT, PAS UNE COMMANDE, ET C'EST ASSUMÉ.
    ///
    /// La déconnexion ne fait rien d'asynchrone et n'a aucune condition. Lui
    /// donner une `ICommand` ajouterait une propriété à la vue-modèle pour
    /// appeler une méthode qui s'y trouve déjà. Le reste de l'application passe
    /// par des commandes parce que le reste attend le réseau.
    /// </summary>
    private void SurDeconnexion(object? expediteur, RoutedEventArgs args)
    {
        if (DataContext is FenetrePrincipaleViewModel modele)
        {
            modele.Deconnecter();
        }
    }
}
