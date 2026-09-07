using HBA.Merchants.Domain.Members;
using Microsoft.EntityFrameworkCore;

namespace HBA.Merchants.Infrastructure.Persistence;

/// <summary>L'AMORÇAGE DES RÔLES SYSTÈME — IDEMPOTENT, AU DÉMARRAGE, EN C#.</summary>
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
