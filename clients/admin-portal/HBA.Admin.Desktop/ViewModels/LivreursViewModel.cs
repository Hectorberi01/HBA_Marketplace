using System.Collections.ObjectModel;
using System.Globalization;
using HBA.Admin.Desktop.Services;

namespace HBA.Admin.Desktop.ViewModels;

/// <summary>Les dossiers de livreur : vérifier, rejeter, suspendre.</summary>
public sealed class LivreursViewModel : ViewModelBase
{
    /// <summary>
    /// Le plafond demandé à l'amont.
    /// </summary>
    /// <remarks>
    /// 200 PLUTÔT QUE LES 100 PAR DÉFAUT, ET L'ÉCRAN DIT QUAND IL L'ATTEINT.
    ///
    /// La route ne pagine pas et ne rend aucun total : demander plus recule la
    /// borne sans la supprimer. Ce qui compte est de ne jamais faire passer un
    /// plancher pour un compte exact — d'où `Plafonne` ci-dessous.
    /// </remarks>
    private const int Plafond = 200;

    private readonly ClientApiAdmin _api;
    private readonly IDemandeurDeSaisie _saisie;

    private LigneLivreur? _selection;
    private string _statut = "UnderReview";
    private string? _erreur;
    private string? _confirmation;
    private bool _enCours;

    public LivreursViewModel(ClientApiAdmin api, IDemandeurDeSaisie saisie)
    {
        _api = api;
        _saisie = saisie;

        Rafraichir = new CommandeAsync(ChargerAsync);
        Choisir = new CommandeAsync<string>(statut =>
        {
            Statut = statut;
            return ChargerAsync();
        });

        Agir = new CommandeAsync<GesteLivreur>(AgirAsync, Applicable);

        Rafraichir.Execute(null);
    }

    public ObservableCollection<LigneLivreur> Livreurs { get; } = [];

    public IReadOnlyList<GesteLivreur> Gestes { get; } = GesteLivreur.Tous;

    public string Statut
    {
        get => _statut;
        private set
        {
            if (Definir(ref _statut, value))
            {
                Selection = null;
                Notifier(nameof(SurEnAttente));
                Notifier(nameof(SurIncomplets));
                Notifier(nameof(SurVerifies));
                Notifier(nameof(SurSuspendus));
            }
        }
    }

    public bool SurEnAttente => _statut == "UnderReview";

    public bool SurIncomplets => _statut == "PendingDocuments";

    public bool SurVerifies => _statut == "Verified";

    public bool SurSuspendus => _statut == "Suspended";

    public LigneLivreur? Selection
    {
        get => _selection;
        set
        {
            if (Definir(ref _selection, value))
            {
                Notifier(nameof(ADesSelection));
                Agir.Reevaluer();
            }
        }
    }

    public bool ADesSelection => _selection is not null;

    public string? Erreur
    {
        get => _erreur;
        private set { if (Definir(ref _erreur, value)) Notifier(nameof(EnErreur)); }
    }

    public bool EnErreur => !string.IsNullOrEmpty(_erreur);

    public string? Confirmation
    {
        get => _confirmation;
        private set { if (Definir(ref _confirmation, value)) Notifier(nameof(AConfirmation)); }
    }

    public bool AConfirmation => !string.IsNullOrEmpty(_confirmation);

    public bool EnCours
    {
        get => _enCours;
        private set { if (Definir(ref _enCours, value)) Agir.Reevaluer(); }
    }

    /// <summary>La liste a-t-elle atteint la borne de l'amont ?</summary>
    public bool Plafonne { get; private set; }

    public string Position => Livreurs.Count == 0
        ? "Aucun dossier"
        : Plafonne
            ? $"{Livreurs.Count}+ dossier(s) — la route plafonne, le reste n'est pas rendu"
            : $"{Livreurs.Count} dossier(s)";

    public CommandeAsync Rafraichir { get; }

    public CommandeAsync<string> Choisir { get; }

    public CommandeAsync<GesteLivreur> Agir { get; }

    private bool Applicable(GesteLivreur geste)
        => _selection is not null && !EnCours && geste.ApplicableA(_selection.Statut);

    private async Task ChargerAsync()
    {
        EnCours = true;
        Erreur = null;
        Confirmation = null;

        try
        {
            var resultat = await _api.ListerLivreursAsync(_statut, Plafond);

            if (!resultat.Reussi || resultat.Valeur is null)
            {
                Erreur = resultat.Message ?? "Liste indisponible.";
                return;
            }

            Selection = null;
            Livreurs.Clear();

            foreach (var livreur in resultat.Valeur)
            {
                Livreurs.Add(new LigneLivreur(livreur));
            }

            Plafonne = Livreurs.Count >= Plafond;

            Notifier(nameof(Position));
        }
        finally
        {
            EnCours = false;
        }
    }

    private async Task AgirAsync(GesteLivreur geste)
    {
        if (_selection is not { } ligne)
        {
            return;
        }

        Erreur = null;
        Confirmation = null;

        string? motif = null;

        if (geste.Saisie == SaisieRequise.Motif)
        {
            motif = await _saisie.MotifAsync($"{geste.Libelle} · {ligne.Nom}");

            if (string.IsNullOrWhiteSpace(motif))
            {
                return;
            }
        }

        if (geste.Destructeur && !_api.ElevationValide)
        {
            var mot = await _saisie.MotDePasseAsync($"{geste.Libelle} · {ligne.Nom}");

            if (string.IsNullOrWhiteSpace(mot))
            {
                return;
            }

            var elevation = await _api.EleverAsync(mot);

            if (!elevation.Reussi)
            {
                Erreur = elevation.Message;
                return;
            }
        }

        EnCours = true;

        try
        {
            var resultat = await _api.AgirSurLivreurAsync(ligne.Id, geste, motif);

            if (!resultat.Reussi)
            {
                Erreur = resultat.Message;
                return;
            }

            Confirmation = $"{geste.Libelle} — {ligne.Nom}.";
        }
        finally
        {
            EnCours = false;
        }

        await ChargerAsync();
    }
}

/// <summary>Un dossier de livreur, tel que la liste l'affiche.</summary>
public sealed class LigneLivreur
{
    public LigneLivreur(LivreurAdmin livreur)
    {
        Id = livreur.DriverId;
        Nom = livreur.FullName;
        Telephone = livreur.Phone;
        Statut = livreur.VerificationStatus;
        Dispatchable = livreur.Dispatchable;
        Motif = livreur.StatusReason ?? string.Empty;
        AMotif = !string.IsNullOrEmpty(livreur.StatusReason);

        var manquants = livreur.MissingDocuments ?? [];
        PiecesManquantes = manquants.Count == 0 ? string.Empty : string.Join(", ", manquants);
        APiecesManquantes = manquants.Count > 0;

        InscritLe = livreur.RegisteredAtUtc.ToLocalTime()
            .ToString("dd/MM/yyyy", CultureInfo.CurrentCulture);
    }

    public Guid Id { get; }

    public string Nom { get; }

    public string Telephone { get; }

    public string Statut { get; }

    /// <summary>Le livreur peut-il recevoir des courses ?</summary>
    /// <remarks>
    /// CE N'EST PAS LA MÊME CHOSE QUE `Verified`.
    ///
    /// `Dispatchable` vient du domaine et croise plusieurs conditions — dossier,
    /// véhicule, disponibilité. Un livreur vérifié mais non dispatchable est un
    /// cas normal, et l'afficher évite de chercher pourquoi il ne reçoit rien.
    /// </remarks>
    public bool Dispatchable { get; }

    public string Motif { get; }

    public bool AMotif { get; }

    public string PiecesManquantes { get; }

    public bool APiecesManquantes { get; }

    public string InscritLe { get; }
}
