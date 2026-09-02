namespace HBA.Controls.Controles;

/// <summary>
/// Toute <c>ProjectReference</c> doit désigner un projet qui existe.
/// </summary>
/// <remarks>
/// ═══════════════════════════════════════════════════════════════════════════
/// CE QUI A PRODUIT CE CONTRÔLE, LE 28 AOÛT 2026.
///
/// `dispatch-service`, `tracking-service` et `proof-of-delivery-service` ont été
/// retirés du dépôt. L'inventaire de retrait couvrait neuf points — la solution,
/// le compose, les autorisations gRPC, les topics Kafka, les scripts, les
/// manifestes — TOUS côté production. Aucun ne regardait `tests/`, précisément
/// parce qu'un projet de test « n'est déployé nulle part ». C'est ce
/// raisonnement qui a laissé trois `ProjectReference` mortes dans
/// `HBA.Delivery.UnitTests`.
///
/// ET LE SYMPTÔME DÉSIGNE LA MAUVAISE CAUSE. MSBuild rend un AVERTISSEMENT
/// MSB9008 — « le projet référencé n'existe pas » — puis compile quand même, et
/// échoue ensuite sur les `using` en CS0234 : « le nom d'espace de noms n'existe
/// pas ». On lit cinq erreurs qui parlent d'espaces de noms, et la ligne qui dit
/// la vraie cause est un warning au milieu.
///
/// POURQUOI LE CONTRÔLE DE SOLUTION NE L'A PAS VU. Il vérifie la cohérence de
/// `HBA.sln`. Or ce projet de test N'EST PAS dans la solution : il n'y avait
/// donc rien à vérifier de son côté. Les deux contrôles sont complémentaires, et
/// c'est l'espace entre eux qui a laissé passer le défaut.
///
/// CE CONTRÔLE NE REGARDE PAS LA SOLUTION. Il part des `.csproj` du disque —
/// tous, y compris ceux qu'aucune solution ne référence.
/// ═══════════════════════════════════════════════════════════════════════════
/// </remarks>
public sealed class ReferencesControle : IControle
{
    /// <inheritdoc/>
    public string Nom => "references";

    /// <inheritdoc/>
    public string Resume => "toute ProjectReference désigne un projet qui existe";

    /// <inheritdoc/>
    public Verdict Executer()
    {
        var fautes = new List<string>();
        var projets = Projets.Tous();
        var total = 0;

        foreach (var csproj in projets)
        {
            IReadOnlyList<(string Brut, string Absolu)> refs;
            try
            {
                refs = Projets.References(csproj).ToList();
            }
            catch (IOException erreur)
            {
                fautes.Add($"{Depot.Relatif(csproj)} : illisible — {erreur.Message}");
                continue;
            }

            foreach (var (brut, absolu) in refs)
            {
                total++;
                if (File.Exists(absolu))
                {
                    continue;
                }

                fautes.Add(
                    $"{Depot.Relatif(csproj)} référence {brut} → "
                    + $"{Depot.Relatif(absolu)} n'existe pas. MSBuild rendra MSB9008 puis "
                    + "échouera sur les `using` en CS0234 : le message d'erreur désignera "
                    + "des espaces de noms, pas cette ligne.");
            }
        }

        return new Verdict(
            fautes,
            [$"{projets.Count} projet(s) examiné(s), {total} référence(s) de projet"],
            ["les `ProjectReference` écrites dans un commentaire XML ou sous un "
             + "`Condition` faux — la lecture est textuelle, elle ne comprend pas MSBuild"]);
    }
}
