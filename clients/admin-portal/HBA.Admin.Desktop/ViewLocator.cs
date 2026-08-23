using Avalonia.Controls;
using Avalonia.Controls.Templates;
using HBA.Admin.Desktop.ViewModels;

namespace HBA.Admin.Desktop;

/// <summary>
/// Associe une vue-modèle à sa vue par convention de nom.
/// </summary>
/// <remarks>
/// LA CONVENTION EST `…ViewModel` → `…View`, ET ELLE N'EST VÉRIFIÉE NULLE PART
///    À LA COMPILATION.
///
/// Une vue mal nommée ne casse pas la construction : elle affiche, à
/// l'exécution, le texte « Vue introuvable : … » à la place de l'écran. C'est
/// délibérément visible — un `return null` afficherait une zone vide, que l'on
/// prendrait pour un écran qui ne charge pas.
/// </remarks>
public sealed class ViewLocator : IDataTemplate
{
    public Control Build(object? param)
    {
        if (param is null)
        {
            return new TextBlock { Text = "Aucune vue-modèle." };
        }

        var nom = param.GetType().FullName!.Replace("ViewModels", "Views", StringComparison.Ordinal);
        nom = nom.EndsWith("ViewModel", StringComparison.Ordinal)
            ? string.Concat(nom.AsSpan(0, nom.Length - "ViewModel".Length), "View")
            : nom;

        var type = Type.GetType(nom);

        return type is not null && Activator.CreateInstance(type) is Control vue
            ? vue
            : new TextBlock { Text = $"Vue introuvable : {nom}" };
    }

    public bool Match(object? data) => data is ViewModelBase;
}
