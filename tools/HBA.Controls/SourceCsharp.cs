namespace HBA.Controls;

/// <summary>Le peu de C# que les contrôles doivent savoir lire.</summary>
public static class SourceCsharp
{
    /// <summary>Retire commentaires de ligne et de bloc, en respectant les chaînes.</summary>
    public static string SansCommentaires(string source)
    {
        var sortie = new System.Text.StringBuilder(source.Length);
        var i = 0;
        var n = source.Length;

        while (i < n)
        {
            var c = source[i];

            if (c == '"')
            {
                // Chaîne textuelle @"…" : le seul échappement est "".
                var verbatim = i > 0 && source[i - 1] == '@';
                sortie.Append(c);
                i++;
                while (i < n)
                {
                    if (verbatim)
                    {
                        if (source[i] == '"')
                        {
                            if (i + 1 < n && source[i + 1] == '"')
                            {
                                sortie.Append("\"\"");
                                i += 2;
                                continue;
                            }

                            break;
                        }
                    }
                    else
                    {
                        if (source[i] == '\\' && i + 1 < n)
                        {
                            sortie.Append(source[i]).Append(source[i + 1]);
                            i += 2;
                            continue;
                        }

                        if (source[i] == '"')
                        {
                            break;
                        }

                        // Chaîne non terminée : on abandonne l'état plutôt que
                        // d'avaler le reste du fichier.
                        if (source[i] == '\n')
                        {
                            break;
                        }
                    }

                    sortie.Append(source[i]);
                    i++;
                }

                if (i < n)
                {
                    sortie.Append(source[i]);
                    i++;
                }

                continue;
            }

            if (c == '\'')
            {
                sortie.Append(c);
                i++;
                while (i < n && source[i] != '\'')
                {
                    if (source[i] == '\\')
                    {
                        sortie.Append(source[i]);
                        i++;
                    }

                    if (i < n)
                    {
                        sortie.Append(source[i]);
                        i++;
                    }
                }

                if (i < n)
                {
                    sortie.Append(source[i]);
                    i++;
                }

                continue;
            }

            if (c == '/' && i + 1 < n && source[i + 1] == '/')
            {
                while (i < n && source[i] != '\n')
                {
                    i++;
                }

                continue;
            }

            if (c == '/' && i + 1 < n && source[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < n && !(source[i] == '*' && source[i + 1] == '/'))
                {
                    i++;
                }

                i += 2;
                continue;
            }

            sortie.Append(c);
            i++;
        }

        return sortie.ToString();
    }

    /// <summary>Tous les fichiers `.cs` du code source du dépôt.</summary>
    public static IEnumerable<string> Fichiers()
    {
        foreach (var racine in new[] { "services", "shared", "bff" })
        {
            foreach (var fichier in Depot.Fichiers(Depot.Dossier(racine), ".cs"))
            {
                yield return fichier;
            }
        }
    }
}
