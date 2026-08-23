using System.Collections.ObjectModel;
using System.Globalization;
using HBA.Admin.Desktop.Services;

namespace HBA.Admin.Desktop.ViewModels;

/// <summary>Les retraits : ce que la plateforme s'apprête à faire sortir.</summary>
public sealed class RetraitsViewModel : ViewModelBase
{
    private readonly ClientApiAdmin _api;
    private readonly IDemandeurDeSaisie _saisie;

    private LigneRetrait? _selection;
    private FileRetraits _file = FileRetraits.PartenairesEnAttente;
    private string? _erreur;
    private string? _confirmation;
    private bool _enCours;

    public RetraitsViewModel(ClientApiAdmin api, IDemandeurDeSaisie saisie)
    {
        _api = api;
        _saisie = saisie;

        Rafraichir = new CommandeAsync(ChargerAsync);
        Choisir = new CommandeAsync<string>(cle =>
        {
            File = cle switch
            {
                "en-cours" => FileRetraits.PartenairesEnCours,
                "clients" => FileRetraits.Clients,
                _ => FileRetraits.PartenairesEnAttente,
            };
            return ChargerAsync();
        });

        Agir = new CommandeAsync<GesteRetrait>(AgirAsync, _ => _selection is not null && !EnCours);

        Rafraichir.Execute(null);
    }

    public ObservableCollection<LigneRetrait> Retraits { get; } = [];

    /// <summary>Les gestes ouverts sur la file courante.</summary>
    public IReadOnlyList<GesteRetrait> Gestes { get; private set; } = GesteRetrait.Pour(FileRetraits.PartenairesEnAttente);

    public FileRetraits File
    {
        get => _file;
        private set
        {
            if (!Definir(ref _file, value))
            {
                return;
            }

            Gestes = GesteRetrait.Pour(value);
            Selection = null;

            Notifier(nameof(Gestes));
            Notifier(nameof(SurPartenairesEnAttente));
            Notifier(nameof(SurPartenairesEnCours));
            Notifier(nameof(SurClients));
            Notifier(nameof(Explication));
            Notifier(nameof(AucunGeste));
        }
    }

    public bool SurPartenairesEnAttente => _file == FileRetraits.PartenairesEnAttente;

    public bool SurPartenairesEnCours => _file == FileRetraits.PartenairesEnCours;

    public bool SurClients => _file == FileRetraits.Clients;

    /// <summary>
    /// Une phrase par file, parce que les trois ne se traitent pas pareil.
    /// </summary>
    /// <remarks>
    /// SANS ELLE, RIEN À L'ÉCRAN NE DIT QUE LE VIREMENT CLIENT EST MANUEL.
    ///
    /// Un administrateur qui clique « marquer comme payé » en croyant déclencher
    /// un virement laisse un client sans son argent, avec une demande close.
    /// C'est l'erreur la plus coûteuse que cet écran permette, et elle se prévient
    /// par une phrase.
    /// </remarks>
    public string Explication => _file switch
    {
        FileRetraits.PartenairesEnAttente =>
            "Vendeurs et livreurs. Approuver engage le virement chez le prestataire.",

        FileRetraits.PartenairesEnCours =>
            "Virements déjà engagés chez le prestataire. Lecture seule : "
            + "rien à décider tant qu'il n'a pas répondu.",

        _ => "Clients. AUCUN VIREMENT N'EST AUTOMATIQUE ICI : exécutez-le chez le "
             + "prestataire, puis saisissez sa référence. « Marquer comme payé » "
             + "n'envoie pas l'argent, il enregistre que vous l'avez envoyé.",
    };

    public bool AucunGeste => Gestes.Count == 0;

    public LigneRetrait? Selection
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

    public string Position => Retraits.Count == 0
        ? "Aucune demande"
        : $"{Retraits.Count} demande(s) · {Total}";

    /// <summary>Le total de la file, dans sa devise.</summary>
    /// <remarks>
    /// IL EST AFFICHÉ PARCE QUE C'EST LA QUESTION QU'ON SE POSE EN ARRIVANT.
    ///
    /// « Combien la plateforme s'apprête-t-elle à faire sortir aujourd'hui ». La
    /// somme n'a de sens que si toutes les lignes partagent la devise — ce qui est
    /// le cas au Bénin, où tout est en XOF. Si une seconde devise apparaissait, le
    /// total serait faux SANS le dire : d'où le refus explicite ci-dessous.
    /// </remarks>
    public string Total
    {
        get
        {
            if (Retraits.Count == 0)
            {
                return string.Empty;
            }

            var devises = Retraits.Select(r => r.Devise).Distinct().ToArray();

            return devises.Length > 1
                ? "total non calculable (plusieurs devises)"
                : Argent.Formater(Retraits.Sum(r => r.Montant), devises[0]);
        }
    }

    public CommandeAsync Rafraichir { get; }

    public CommandeAsync<string> Choisir { get; }

    public CommandeAsync<GesteRetrait> Agir { get; }

    private async Task ChargerAsync()
    {
        EnCours = true;
        Erreur = null;
        Confirmation = null;

        try
        {
            Selection = null;
            Retraits.Clear();

            if (_file == FileRetraits.Clients)
            {
                var clients = await _api.ListerRetraitsAsync<RetraitClient>(_file);

                if (!clients.Reussi || clients.Valeur is null)
                {
                    Erreur = clients.Message ?? "File indisponible.";
                    return;
                }

                foreach (var demande in clients.Valeur)
                {
                    Retraits.Add(LigneRetrait.De(demande));
                }
            }
            else
            {
                var partenaires = await _api.ListerRetraitsAsync<RetraitVendeur>(_file);

                if (!partenaires.Reussi || partenaires.Valeur is null)
                {
                    Erreur = partenaires.Message ?? "File indisponible.";
                    return;
                }

                foreach (var demande in partenaires.Valeur)
                {
                    Retraits.Add(LigneRetrait.De(demande));
                }
            }

            Notifier(nameof(Position));
            Notifier(nameof(Total));
        }
        finally
        {
            EnCours = false;
        }
    }

    /// <remarks>
    /// MÊME ORDRE QU'AILLEURS : la saisie d'abord, l'élévation ensuite, le geste
    /// enfin. Renoncer à l'une des deux premières annule tout, sans état à
    /// nettoyer — rien n'est encore parti.
    /// </remarks>
    private async Task AgirAsync(GesteRetrait geste)
    {
        if (_selection is not { } ligne)
        {
            return;
        }

        Erreur = null;
        Confirmation = null;

        string? saisie = null;

        if (geste.Saisie != SaisieRequise.Aucune)
        {
            var invite = $"{geste.Libelle} · {ligne.Montant:N0} {ligne.Devise}";

            saisie = geste.Saisie == SaisieRequise.Reference
                ? await _saisie.ReferenceAsync(invite)
                : await _saisie.MotifAsync(invite);

            if (string.IsNullOrWhiteSpace(saisie))
            {
                return;
            }
        }

        if (geste.Destructeur && !_api.ElevationValide)
        {
            var mot = await _saisie.MotDePasseAsync($"{geste.Libelle} · {ligne.Resume}");

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
            var resultat = await _api.AgirSurRetraitAsync(_file, ligne.Id, geste, saisie);

            if (!resultat.Reussi)
            {
                Erreur = resultat.Message;
                return;
            }

            Confirmation = $"{geste.Libelle} — {ligne.Resume}.";
        }
        finally
        {
            EnCours = false;
        }

        await ChargerAsync();
    }
}

/// <summary>Une demande de retrait, telle que la liste l'affiche.</summary>
/// <remarks>
/// LES DEUX FILES SE PROJETTENT SUR UN SEUL TYPE D'AFFICHAGE.
///
/// Elles n'ont ni les mêmes champs ni les mêmes routes — d'où deux modèles
/// distincts côté service. Mais elles répondent à la même question à l'écran :
/// qui, combien, depuis quand. Deux gabarits XAML pour cela dupliqueraient le
/// formatage de la monnaie, qui est précisément ce qu'il ne faut pas dupliquer.
/// </remarks>
public sealed class LigneRetrait
{
    private LigneRetrait(
        Guid id, string demandeur, decimal montant, string devise,
        string statut, DateTime demandeLe, string? destination)
    {
        Id = id;
        Demandeur = demandeur;
        Montant = montant;
        Devise = devise;
        Statut = statut;
        DemandeLe = demandeLe.ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.CurrentCulture);
        Destination = destination ?? string.Empty;
        ADestination = !string.IsNullOrEmpty(destination);
        MontantAffiche = Argent.Formater(montant, devise);
        Resume = $"{MontantAffiche} · {demandeur}";
    }

    public Guid Id { get; }

    /// <summary>Identifiant du vendeur ou du client, tronqué pour l'affichage.</summary>
    public string Demandeur { get; }

    public decimal Montant { get; }

    public string Devise { get; }

    public string MontantAffiche { get; }

    public string Statut { get; }

    public string DemandeLe { get; }

    /// <summary>Le numéro Mobile Money — vide pour un retrait partenaire.</summary>
    public string Destination { get; }

    public bool ADestination { get; }

    public string Resume { get; }

    public static LigneRetrait De(RetraitVendeur demande)
        => new(demande.Id, Court(demande.SellerId), demande.Amount, demande.Currency,
            demande.Status, demande.CreatedAtUtc, null);

    public static LigneRetrait De(RetraitClient demande)
        => new(demande.Id, Court(demande.CustomerId), demande.Amount, demande.Currency,
            demande.Status, demande.RequestedAtUtc, $"{demande.Msisdn} ({demande.Provider})");

    /// <summary>
    /// Les huit premiers caractères d'un identifiant.
    /// </summary>
    /// <remarks>
    /// UN GUID COMPLET DANS UNE COLONNE REND LA LIGNE ILLISIBLE.
    ///
    /// Huit caractères suffisent à distinguer deux demandes à l'œil et à
    /// retrouver la bonne dans un journal. Le GUID entier reste disponible : c'est
    /// `Id`, et c'est lui que le geste envoie.
    /// </remarks>
    private static string Court(Guid identifiant) => identifiant.ToString("N")[..8];
}

/// <summary>Le formatage de la monnaie, à un seul endroit.</summary>
/// <remarks>
/// ═══════════════════════════════════════════════════════════════════════════
/// LE FRANC CFA N'A PAS DE CENTIMES, ET L'AFFICHER AVEC EN DIRAIT LE CONTRAIRE.
///
/// XOF a zéro décimale : « 12 500 F CFA », jamais « 12 500,00 ». Un montant à
/// deux décimales sur un écran de virement laisse croire à une précision qui
/// n'existe pas, et invite à chercher un arrondi là où il n'y en a pas.
///
/// Les autres devises gardent leurs deux décimales — le jour où il y en aura.
/// ═══════════════════════════════════════════════════════════════════════════
/// </remarks>
public static class Argent
{
    public static string Formater(decimal montant, string devise)
    {
        var sansDecimales = devise is "XOF" or "XAF" or "JPY" or "KMF" or "GNF";

        var nombre = montant.ToString(sansDecimales ? "N0" : "N2", CultureInfo.CurrentCulture);

        return devise == "XOF" ? $"{nombre} F CFA" : $"{nombre} {devise}";
    }
}
