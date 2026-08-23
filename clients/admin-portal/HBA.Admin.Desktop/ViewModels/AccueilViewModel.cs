using System.Collections.ObjectModel;
using HBA.Admin.Desktop.Services;

namespace HBA.Admin.Desktop.ViewModels;

/// <summary>L'écran d'accueil : par quoi commencer.</summary>
public sealed class AccueilViewModel : ViewModelBase
{
    private readonly ClientApiAdmin _api;

    private string? _erreur;
    private bool _enCours;

    public AccueilViewModel(ClientApiAdmin api)
    {
        _api = api;
        Rafraichir = new CommandeAsync(ChargerAsync);

        // CHARGEMENT LANCÉ SANS ÊTRE ATTENDU — le constructeur d'une
        // vue-modèle ne peut pas être asynchrone, et le rendre bloquant figerait
        // la fenêtre avant son premier affichage. `CommandeAsync` capture déjà
        // les exceptions dans son `finally`.
        Rafraichir.Execute(null);
    }

    /// <summary>Les files, dans l'ordre rendu par la passerelle.</summary>
    public ObservableCollection<TuileFile> Files { get; } = [];

    public string? Erreur
    {
        get => _erreur;
        private set { if (Definir(ref _erreur, value)) Notifier(nameof(EnErreur)); }
    }

    public bool EnErreur => !string.IsNullOrEmpty(_erreur);

    public bool EnCours
    {
        get => _enCours;
        private set => Definir(ref _enCours, value);
    }

    public CommandeAsync Rafraichir { get; }

    private async Task ChargerAsync()
    {
        EnCours = true;
        Erreur = null;

        try
        {
            var resultat = await _api.LireFilesAsync();

            if (!resultat.Reussi || resultat.Valeur?.Data is null)
            {
                Erreur = resultat.Message ?? "Files indisponibles.";
                return;
            }

            Files.Clear();

            foreach (var file in resultat.Valeur.Data.Files)
            {
                Files.Add(new TuileFile(file));
            }
        }
        finally
        {
            EnCours = false;
        }
    }
}

/// <summary>Une file, telle que la tuile l'affiche.</summary>
/// <remarks>
/// LA MISE EN FORME EST ICI, PAS DANS LE XAML.
///
/// Un convertisseur de liaison ferait le même travail, dans un fichier que
/// personne n'ouvre en relisant un écran — et la règle qui suit mérite d'être
/// lue.
/// </remarks>
public sealed class TuileFile
{
    public TuileFile(FileDAttente file)
    {
        Cle = file.Cle;
        Libelle = file.Libelle;
        Indisponible = file.Total is null;

        // TROIS AFFICHAGES POUR TROIS ÉTATS DISTINCTS, ET AUCUN NE SE CONFOND.
        //
        //   • « — »    : le service n'a pas répondu. On ne sait pas.
        //   • « 100+ » : le total est un PLANCHER — l'amont plafonne sa liste et
        //                ne dit pas ce qui reste (voir `admin/drivers`, take=100).
        //   • « 12 »   : un compte exact.
        //
        // Écrire « 0 » dans le premier cas ferait fermer l'application à un
        // administrateur qui a deux cents dossiers en attente ; écrire « 100 »
        // dans le deuxième lui ferait croire la file bornée.
        Valeur = file.Total switch
        {
            null => "—",
            var total when file.Approximatif => $"{total}+",
            var total => total.Value.ToString("N0"),
        };

        Note = Indisponible
            ? "Service indisponible"
            : file.Approximatif ? "au moins" : string.Empty;
    }

    public string Cle { get; }

    public string Libelle { get; }

    public string Valeur { get; }

    public string Note { get; }

    public bool Indisponible { get; }
}
