using HBA.Analytics.Domain.RollUps;
using Microsoft.EntityFrameworkCore;

namespace HBA.Analytics.Infrastructure.Persistence.Repositories;

/// <summary>
/// Implémentation EF de <see cref="IRegistreDesRollUps"/>.
/// </summary>
/// <remarks>
/// ═════════════════════════════════════════════════════════════════════════════
/// « OBTENIR OU CRÉER » CHERCHE D'ABORD DANS LE SUIVI, PAS EN BASE.
///
/// Une seule commande confirmée touche N lignes vendeur ET une ligne plateforme,
/// dans le MÊME `SaveChanges`. Une commande à deux lignes du même vendeur, ou
/// deux commandes du même jour traitées dans la même transaction, demanderaient
/// deux fois la même ligne : `FindAsync` rend l'entité déjà suivie sans
/// retourner en base, donc les deux incréments s'appliquent à un seul objet.
///
/// `FirstOrDefaultAsync` NE FERAIT PAS CELA : il exécute toujours la requête et,
/// pour une ligne AJOUTÉE mais pas encore enregistrée, ne trouve rien. On
/// créerait alors une SECONDE entité de même clé, et `SaveChanges` échouerait sur
/// une violation de clé primaire — au mieux. C'est pourquoi ces trois méthodes
/// emploient `FindAsync` et lui seul.
///
/// L'ORDRE DES ARGUMENTS DE `FindAsync` EST CELUI DE LA CLÉ DÉCLARÉE, et il n'y
/// a aucun contrôle de type dessus : intervertir devise et nature compilerait et
/// ne trouverait jamais rien — donc créerait une ligne par événement, sans
/// erreur. Les clés sont écrites ici dans le même ordre que dans
/// `RollUpConfigurations`.
///
/// LA LECTURE EST EN `AsNoTracking` : ces lignes ne seront pas modifiées, et les
/// suivre ferait porter au suivi jusqu'à 366 entités par requête de graphe.
/// ═════════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed class RegistreDesRollUps : IRegistreDesRollUps
{
    private readonly AnalyticsDbContext _contexte;

    public RegistreDesRollUps(AnalyticsDbContext contexte) => _contexte = contexte;

    public async Task<VenteJournaliereVendeur> ObtenirOuCreerVenteAsync(
        Guid sellerId, DateOnly jour, string devise, string nature, CancellationToken cancellationToken = default)
    {
        var existante = await _contexte.SellerDaily
            .FindAsync(new object?[] { sellerId, jour, devise, nature }, cancellationToken);

        if (existante is not null)
        {
            return existante;
        }

        var nouvelle = new VenteJournaliereVendeur
        {
            SellerId = sellerId,
            Day = jour,
            Currency = devise,
            Kind = nature,
            UpdatedAtUtc = DateTime.UtcNow
        };

        _contexte.SellerDaily.Add(nouvelle);
        return nouvelle;
    }

    public async Task<ActiviteJournalierePlateforme> ObtenirOuCreerActiviteAsync(
        DateOnly jour, string nature, string devise, CancellationToken cancellationToken = default)
    {
        var existante = await _contexte.PlatformDaily
            .FindAsync(new object?[] { jour, nature, devise }, cancellationToken);

        if (existante is not null)
        {
            return existante;
        }

        var nouvelle = new ActiviteJournalierePlateforme
        {
            Day = jour,
            Kind = nature,
            Currency = devise,
            UpdatedAtUtc = DateTime.UtcNow
        };

        _contexte.PlatformDaily.Add(nouvelle);
        return nouvelle;
    }

    public async Task<InscriptionJournaliere> ObtenirOuCreerInscriptionAsync(
        DateOnly jour, string nature, CancellationToken cancellationToken = default)
    {
        var existante = await _contexte.SignupDaily
            .FindAsync(new object?[] { jour, nature }, cancellationToken);

        if (existante is not null)
        {
            return existante;
        }

        var nouvelle = new InscriptionJournaliere
        {
            Day = jour,
            Kind = nature,
            UpdatedAtUtc = DateTime.UtcNow
        };

        _contexte.SignupDaily.Add(nouvelle);
        return nouvelle;
    }

    public async Task<IReadOnlyList<VenteJournaliereVendeur>> LireVentesAsync(
        Guid sellerId, DateOnly du, DateOnly au, CancellationToken cancellationToken = default)
        => await _contexte.SellerDaily
            .AsNoTracking()
            .Where(ligne => ligne.SellerId == sellerId && ligne.Day >= du && ligne.Day <= au)
            .OrderBy(ligne => ligne.Day)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ActiviteJournalierePlateforme>> LireActiviteAsync(
        DateOnly du, DateOnly au, CancellationToken cancellationToken = default)
        => await _contexte.PlatformDaily
            .AsNoTracking()
            .Where(ligne => ligne.Day >= du && ligne.Day <= au)
            .OrderBy(ligne => ligne.Day)
            .ToListAsync(cancellationToken);

    public async Task<AnnulationJournaliereVendeur> ObtenirOuCreerAnnulationAsync(
        Guid sellerId, DateOnly jour, string devise, CancellationToken cancellationToken = default)
    {
        var existante = await _contexte.SellerCancellationDaily
            .FindAsync(new object?[] { sellerId, jour, devise }, cancellationToken);

        if (existante is not null)
        {
            return existante;
        }

        var nouvelle = new AnnulationJournaliereVendeur
        {
            SellerId = sellerId,
            Day = jour,
            Currency = devise,
            UpdatedAtUtc = DateTime.UtcNow
        };

        _contexte.SellerCancellationDaily.Add(nouvelle);
        return nouvelle;
    }

    public async Task<PaiementJournalier> ObtenirOuCreerPaiementAsync(
        DateOnly jour, string fournisseur, string devise, string issue,
        CancellationToken cancellationToken = default)
    {
        var existante = await _contexte.PaymentDaily
            .FindAsync(new object?[] { jour, fournisseur, devise, issue }, cancellationToken);

        if (existante is not null)
        {
            return existante;
        }

        var nouvelle = new PaiementJournalier
        {
            Day = jour,
            Provider = fournisseur,
            Currency = devise,
            Outcome = issue,
            UpdatedAtUtc = DateTime.UtcNow
        };

        _contexte.PaymentDaily.Add(nouvelle);
        return nouvelle;
    }

    public async Task<IReadOnlyList<AnnulationJournaliereVendeur>> LireAnnulationsAsync(
        Guid sellerId, DateOnly du, DateOnly au, CancellationToken cancellationToken = default)
        => await _contexte.SellerCancellationDaily
            .AsNoTracking()
            .Where(ligne => ligne.SellerId == sellerId && ligne.Day >= du && ligne.Day <= au)
            .OrderBy(ligne => ligne.Day)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<PaiementJournalier>> LirePaiementsAsync(
        DateOnly du, DateOnly au, CancellationToken cancellationToken = default)
        => await _contexte.PaymentDaily
            .AsNoTracking()
            .Where(ligne => ligne.Day >= du && ligne.Day <= au)
            .OrderBy(ligne => ligne.Day)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<InscriptionJournaliere>> LireInscriptionsAsync(
        DateOnly du, DateOnly au, CancellationToken cancellationToken = default)
        => await _contexte.SignupDaily
            .AsNoTracking()
            .Where(ligne => ligne.Day >= du && ligne.Day <= au)
            .OrderBy(ligne => ligne.Day)
            .ToListAsync(cancellationToken);
}
