namespace HBA.Controls.Controles;

/// <summary>Toute <c>ProjectReference</c> doit désigner un projet qui existe.</summary>
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
