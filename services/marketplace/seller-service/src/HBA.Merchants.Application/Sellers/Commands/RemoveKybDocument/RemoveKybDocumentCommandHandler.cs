using HBA.Shared.Application.Abstractions;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;
using HBA.Merchants.Domain.Sellers;
using HBA.Merchants.Application.Abstractions;

namespace HBA.Merchants.Application.Sellers.Commands.RemoveKybDocument;

internal sealed class RemoveKybDocumentCommandHandler : ICommandHandler<RemoveKybDocumentCommand>
{
    private readonly ISellerRepository _sellerRepository;
    private readonly ISellerUnitOfWork _unitOfWork;

    public RemoveKybDocumentCommandHandler(
        ISellerRepository sellerRepository, ISellerUnitOfWork unitOfWork)
    {
        _sellerRepository = sellerRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(RemoveKybDocumentCommand command, CancellationToken cancellationToken)
    {
        var seller = await _sellerRepository.GetByIdAsync(new SellerId(command.SellerId), cancellationToken);
        if (seller is null)
        {
            return Result.Failure(Error.NotFound("sellers.seller.not_found", $"Vendeur {command.SellerId} introuvable."));
        }

        var result = seller.RemoveKybDocument(command.DocumentId);
        if (result.IsFailure)
        {
            return Result.Failure(result.Error);
        }

        // ═════════════════════════════════════════════════════════════════════
        // LE FICHIER EST TOUJOURS EFFACÉ — MAIS PLUS ICI, ET LA GARANTIE A CHANGÉ.
        //
        // CE QUE FAISAIT LA VERSION PRÉCÉDENTE, ET POURQUOI ELLE LE FAISAIT.
        //
        // Elle appelait le stockage AVANT le commit, avec cet argument : si
        // l'effacement échoue, la ligne reste, le vendeur réessaie, et le document
        // demeure retrouvable. Commit d'abord aurait perdu la seule référence au
        // fichier en cas d'échec — une pièce d'identité orpheline dans un bucket
        // privé, que plus rien ne désigne.
        //
        // CE QUI REMPLACE CETTE GARANTIE, ET POURQUOI C'EST AU MOINS AUSSI SÛR.
        //
        // Sellers ne connaît plus le stockage : les pièces vivent dans le service
        // média. L'agrégat lève `KybDocumentRemovedDomainEvent`, qui part par
        // l'OUTBOX TRANSACTIONNEL — écrit dans la MÊME transaction que la
        // suppression de la ligne.
        //
        // La référence n'est donc jamais perdue : elle passe de la ligne KYB au
        // message d'outbox, qui est rejoué jusqu'à ce que l'effacement réussisse.
        // Et le vendeur n'échoue plus parce que le stockage a eu dix secondes
        // d'indisponibilité.
        //
        // CE QUE ÇA COÛTE : l'effacement devient DIFFÉRÉ. Entre le retrait et le
        // passage de l'outbox, le fichier existe encore. Pour une pièce que le
        // service média conserve de toute façon un an après suppression logique
        // (rétention légale), quelques secondes ne changent rien.
        // ═════════════════════════════════════════════════════════════════════
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
