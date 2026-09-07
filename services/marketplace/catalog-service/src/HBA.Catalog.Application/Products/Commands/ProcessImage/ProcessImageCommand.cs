using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;
using HBA.Catalog.Application.Abstractions;

namespace HBA.Catalog.Application.Products.Commands.ProcessImage;

/// <summary>
/// Traite une image produit (détourage IA + fond blanc) AVANT création du produit,
/// pour permettre au vendeur de valider le rendu.
/// </summary>
public sealed record ProcessImageCommand(
    string FileName, string ContentType, byte[] Content) : ICommand<ProcessedImage>;

internal sealed class ProcessImageCommandHandler : ICommandHandler<ProcessImageCommand, ProcessedImage>
{
    private readonly IImageProcessor _processor;

    public ProcessImageCommandHandler(IImageProcessor processor) => _processor = processor;

    public Task<Result<ProcessedImage>> Handle(ProcessImageCommand command, CancellationToken cancellationToken)
        => _processor.RemoveBackgroundWhiteAsync(
            command.FileName, command.ContentType, command.Content, cancellationToken);
}
