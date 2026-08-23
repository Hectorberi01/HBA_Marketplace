using HBA.Merchants.Domain.Members;
using Microsoft.EntityFrameworkCore;

namespace HBA.Merchants.Infrastructure.Persistence;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// L'AMORÇAGE DES RÔLES SYSTÈME — IDEMPOTENT, AU DÉMARRAGE, EN C#.
///
/// NI `InsertData` DANS UNE MIGRATION, NI SCRIPT SQL. C'EST LE MOTIF DU DÉPÔT.
///
/// `IdentityDataSeeder` fait exactement cela pour les rôles de la plateforme, et
/// pour une bonne raison : les permissions par défaut d'un rôle sont du CODE — une
/// liste dans `SystemSellerRoles` — et un `InsertData` en figerait une copie dans
/// une migration déjà appliquée. Le jour où l'on ajoute une permission à
/// ORDER_MANAGER, il faudrait une migration de plus, et les deux sources
/// divergeraient entre l'environnement neuf et l'ancien.
///
/// CE QUE FAIT CET AMORÇAGE, ET CE QU'IL NE FAIT PAS.
///
/// Il crée les rôles absents et met à jour les permissions de ceux qui existent —
/// c'est cette seconde partie qui permet d'ajouter une permission à un rôle système
/// par une simple livraison de code. Il ne touche JAMAIS un rôle personnalisé, et
/// n'en supprime aucun : un rôle système retiré du catalogue reste en base, encore
/// porté par des membres, et sa disparition silencieuse serait une révocation.
///
/// ET IL NE CRÉE AUCUN MEMBRE.
///
/// Le rattachement des propriétaires existants est le travail de la migration :
/// c'est une reprise de données, elle a lieu une fois, et elle doit être visible
/// dans l'historique du schéma.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public static class MerchantsDataSeeder
{
    public static async Task SeedSystemRolesAsync(
        SellersDbContext dbContext, CancellationToken cancellationToken = default)
    {
        var existants = await dbContext.SellerRoles
            .Where(r => r.SellerId == null)
            .ToDictionaryAsync(r => r.Id, cancellationToken);

        var modifie = false;

        foreach (var attendu in SystemSellerRoles.Catalogue)
        {
            if (!existants.TryGetValue(attendu.Id, out var enBase))
            {
                await dbContext.SellerRoles.AddAsync(attendu, cancellationToken);
                modifie = true;
                continue;
            }

            // ON RECALE LES PERMISSIONS, PAS LE NOM.
            //
            // Le nom d'un rôle système est écrit dans des captures d'écran, des
            // procédures et peut-être des jetons ; le renommer par surprise au
            // redémarrage serait pire que le laisser divergent. Les permissions, en
            // revanche, sont la définition même du rôle : elles doivent suivre le
            // code, sans quoi une correction de sécurité ne s'appliquerait qu'aux
            // bases neuves.
            if (!enBase.Permissions.SetEquals(attendu.Permissions))
            {
                enBase.SyncSystemPermissions([.. attendu.Permissions]);
                modifie = true;
            }
        }

        if (modifie)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
