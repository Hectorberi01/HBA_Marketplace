using HBA.Merchants.Contracts;
using HBA.Merchants.Domain.Members;

namespace HBA.Merchants.UnitTests;

/// <summary>
/// LE SEUL LIEN ENTRE LE DOMAINE ET LE CONTRAT — ET IL N'EST PAS DANS LE
/// COMPILATEUR.
/// </summary>
public sealed class CapacitesTests
{
    [Fact]
    public void Chaque_constante_du_contrat_designe_une_permission_reelle()
    {
        var inconnues = MerchantCapabilities.All
            .Where(code => MerchantPermissions.Parse(code) is null)
            .ToArray();

        inconnues.Should().BeEmpty(
            "une constante du contrat qui ne correspond à aucune permission ferme "
            + "une route pour tout le monde, sans qu'aucune compilation ne s'en plaigne");
    }

    [Fact]
    public void Chaque_permission_du_domaine_a_sa_constante_dans_le_contrat()
    {
        var absentes = MerchantPermissions.All
            .Select(p => p.ToCode())
            .Where(code => !MerchantCapabilities.All.Contains(code))
            .ToArray();

        absentes.Should().BeEmpty(
            "une permission sans constante obligerait les services appelants à "
            + "écrire son code à la main, ce qui ramène la faute de frappe qu'on évite ici");
    }

    /// <summary>
    /// LE NIVEAU DE RISQUE AUSSI EST RECOPIÉ, DONC LUI AUSSI PEUT DIVERGER.
    ///
    /// `MerchantCapabilities.Critical` est la liste qu'un service appelant consulte
    /// pour savoir s'il doit exiger une authentification récente ; le niveau de
    /// risque, lui, vit dans `MerchantPermissions.Catalogue`, côté domaine.
    ///
    /// ET L'OUBLI EST INVISIBLE À L'ŒIL NU.
    ///
    /// Promouvoir une permission au rang Critique sans l'ajouter au contrat ne
    /// casse RIEN : la route continue de répondre, la permission continue d'être
    /// vérifiée, simplement le step-up ne s'applique pas. C'est le pire genre de
    /// défaut — celui où tout fonctionne, en moins bien gardé, et où rien ne le
    /// signale. Ce test est la seule chose qui l'attrape.
    ///
    /// L'inverse compte tout autant : une capacité déclarée Critique ici mais
    /// Normale dans le catalogue imposerait une ressaisie de mot de passe que
    /// personne n'a décidée, sur un geste banal.
    /// </summary>
    [Fact]
    public void Les_capacites_critiques_du_contrat_sont_celles_du_catalogue()
    {
        var attendues = MerchantPermissions.All
            .Where(p => p.RiskOf() == PermissionRisk.Critical)
            .Select(p => p.ToCode())
            .OrderBy(c => c, StringComparer.Ordinal);

        MerchantCapabilities.Critical.OrderBy(c => c, StringComparer.Ordinal)
            .Should().Equal(
                attendues,
                "une permission promue Critique sans être ajoutée au contrat perdrait "
                + "silencieusement son step-up, et rien d'autre ne le signalerait");
    }
}
