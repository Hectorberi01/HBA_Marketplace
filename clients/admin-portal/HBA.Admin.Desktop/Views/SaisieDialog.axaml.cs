using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using HBA.Admin.Desktop.ViewModels;

namespace HBA.Admin.Desktop.Views;

/// <summary>Boîte modale d'une seule ligne : un mot de passe, ou un motif.</summary>
public partial class SaisieDialog : Window
{
    public SaisieDialog()
    {
        InitializeComponent();

        // La saisie prend le focus à l'ouverture. Sans cela, l'administrateur
        // tape dans le vide — et sur un champ masqué, il ne s'en aperçoit qu'au
        // refus.
        Opened += (_, _) => this.FindControl<TextBox>("Saisie")?.Focus();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    /// <summary>Prépare la boîte pour un usage donné.</summary>
    /// <param name="titre">Le geste, tel que l'administrateur l'a demandé.</param>
    /// <param name="invite">Ce qu'on attend de lui.</param>
    /// <param name="masque">Masquer la saisie ? Vrai pour un mot de passe SEULEMENT.</param>
    public void Preparer(string titre, string invite, bool masque)
    {
        var champTitre = this.FindControl<TextBlock>("Titre");
        var champInvite = this.FindControl<TextBlock>("Invite");
        var saisie = this.FindControl<TextBox>("Saisie");

        if (champTitre is not null)
        {
            champTitre.Text = titre;
        }

        if (champInvite is not null)
        {
            champInvite.Text = invite;
        }

        if (saisie is not null)
        {
            // Le caractère de masquage est posé ICI et jamais ailleurs : c'est le
            // seul endroit qui sait laquelle des deux saisies est demandée.
            saisie.PasswordChar = masque ? '•' : '\0';
            saisie.Text = string.Empty;
        }
    }

    private void SurConfirmer(object? expediteur, RoutedEventArgs args)
    {
        var valeur = this.FindControl<TextBox>("Saisie")?.Text;

        // Une saisie vide vaut un renoncement : confirmer avec un champ vide
        // enverrait un motif vide au vendeur, ou un mot de passe vide au serveur.
        Close(string.IsNullOrWhiteSpace(valeur) ? null : valeur);
    }

    private void SurAnnuler(object? expediteur, RoutedEventArgs args) => Close(null);
}

/// <summary>Ouvre la boîte modale au-dessus de la fenêtre principale.</summary>
/// <remarks>
/// L'IMPLÉMENTATION VIT DANS `Views`, OÙ LES FENÊTRES ONT LEUR PLACE.
///
/// C'est la contrepartie de `IDemandeurDeSaisie` : les vues-modèles demandent
/// une saisie, ce type-ci sait comment l'obtenir. Le propriétaire est passé au
/// constructeur — une modale sans propriétaire s'ouvre derrière la fenêtre
/// principale sur certains gestionnaires de fenêtres Linux, et l'application
/// paraît figée.
/// </remarks>
public sealed class DemandeurDeSaisie : IDemandeurDeSaisie
{
    private readonly Window _proprietaire;

    public DemandeurDeSaisie(Window proprietaire) => _proprietaire = proprietaire;

    public Task<string?> MotDePasseAsync(string geste)
        => OuvrirAsync(
            geste,
            "Saisissez votre mot de passe pour confirmer ce geste.",
            masque: true);

    public Task<string?> MotifAsync(string geste)
        => OuvrirAsync(
            geste,
            "Ce motif sera transmis au demandeur. Écrivez-le pour lui.",
            masque: false);

    public Task<string?> ReferenceAsync(string geste)
        => OuvrirAsync(
            geste,
            "Référence du virement, telle que le prestataire l'a rendue. "
            + "Aucun webhook ne confirmera ce versement : c'est la seule preuve "
            + "que l'argent est parti.",
            masque: false);

    private async Task<string?> OuvrirAsync(string titre, string invite, bool masque)
    {
        var boite = new SaisieDialog();
        boite.Preparer(titre, invite, masque);

        return await boite.ShowDialog<string?>(_proprietaire);
    }
}
