using HBA.Identity.Application.Abstractions;

namespace HBA.Identity.Infrastructure.Security;

/// <summary>Section « Identity:Registration » de la configuration.</summary>
public sealed class RegistrationOptions
{
    public const string SectionName = "Identity:Registration";

    /// <summary>Inscription publique (app acheteur, site) : approbation requise.</summary>
    public bool RequireApprovalForBuyers { get; set; } = true;

    /// <summary>Comptes créés depuis la console d'administration : approbation requise.</summary>
    public bool RequireApprovalForAdminCreated { get; set; } = false;
}

/// <summary>Expose les options aux handlers, sans les faire dépendre de la config.</summary>
internal sealed class RegistrationPolicy : IRegistrationPolicy
{
    private readonly RegistrationOptions _options;

    public RegistrationPolicy(RegistrationOptions options) => _options = options;

    public bool RequireApprovalForBuyers => _options.RequireApprovalForBuyers;

    public bool RequireApprovalForAdminCreated => _options.RequireApprovalForAdminCreated;
}
