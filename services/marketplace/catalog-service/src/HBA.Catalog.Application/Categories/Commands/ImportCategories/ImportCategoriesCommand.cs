using HBA.Shared.Application.Messaging;

namespace HBA.Catalog.Application.Categories.Commands.ImportCategories;

/// <summary>
/// Une ligne du fichier importé : un chemin complet, et l'image du nœud terminal.
/// </summary>
/// <param name="Path">Chemin lisible séparé par « / », ex.</param>
/// <param name="ImageUrl">Appliquée au DERNIER segment uniquement.</param>
public sealed record CategoryImportRow(string Path, string? ImageUrl);

/// <summary>Ce qu'il est advenu d'un nœud de l'arbre pendant l'import.</summary>
/// <param name="Path">Chemin normalisé (slugifié), tel qu'il est stocké.</param>
/// <param name="Label">Chemin lisible, tel qu'il figurait dans le fichier.</param>
/// <param name="Status">« created », « existing » ou « error ».</param>
public sealed record CategoryImportOutcome(string Path, string Label, string Status, string? Message);

/// <summary>Compte rendu global.</summary>
public sealed record CategoryImportReport(
    IReadOnlyList<CategoryImportOutcome> Nodes,
    int Created,
    int Existing,
    int Errors,
    bool DryRun);

/// <summary>
/// Crée une arborescence de catégories à partir de chemins, SANS jamais échouer sur
/// l'existant.
/// </summary>
public sealed record ImportCategoriesCommand(
    IReadOnlyList<CategoryImportRow> Rows,
    bool DryRun = false) : ICommand<CategoryImportReport>;
