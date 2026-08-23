using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using HBA.Admin.Desktop.Services;
using HBA.Admin.Desktop.ViewModels;
using HBA.Admin.Desktop.Views;

namespace HBA.Admin.Desktop;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime bureau)
        {
            // LA VALIDATION `DataAnnotations` D'AVALONIA EST RETIRÉE.
            //
            // Elle s'ajoute à celle des vues-modèles et produit DEUX erreurs pour
            // une seule faute de saisie. Le symptôme est un message dupliqué sous
            // le champ, que l'on cherche longtemps dans le code de la vue.
            foreach (var plugin in BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray())
            {
                BindingPlugins.DataValidators.Remove(plugin);
            }

            // COMPOSITION À LA MAIN, SANS CONTENEUR D'INJECTION.
            //
            // Cette application a QUATRE dépendances, toutes à durée de vie
            // « application ». Un conteneur ajouterait un paquet, un fichier de
            // câblage et une indirection pour remplacer six lignes que l'on peut
            // lire d'un coup — et il rendrait invisible ce que la fenêtre reçoit.
            //
            // Le jour où les écrans se compteront par dizaines, ce sera à
            // revoir. Pas avant.
            var configuration = ConfigurationAdmin.Charger();
            var session = new SessionAdmin();
            var api = new ClientApiAdmin(configuration, session);

            // La fenêtre est construite AVANT sa vue-modèle : `DemandeurDeSaisie`
            // a besoin d'un propriétaire pour ouvrir ses modales, et une modale
            // sans propriétaire s'ouvre derrière la fenêtre principale sur
            // certains gestionnaires de fenêtres — l'application paraît figée.
            var fenetre = new FenetrePrincipale();
            fenetre.DataContext = new FenetrePrincipaleViewModel(
                api, session, new DemandeurDeSaisie(fenetre));

            bureau.MainWindow = fenetre;

            // On dispose le client HTTP à la fermeture : sans cela, les
            // connexions restent ouvertes jusqu'à la fin du processus — sans
            // conséquence ici, mais c'est la fermeture qui purge aussi la
            // session en mémoire (voir `SessionAdmin.Oublier`).
            bureau.ShutdownRequested += (_, _) =>
            {
                session.Oublier();
                api.Dispose();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
