using HBA.Shared.Application.Abstractions;
using HBA.Shared.Application.Messaging;
using HBA.Shared.Domain.Results;
using HBA.Merchants.Domain.Sellers;
using HBA.Merchants.Application.Abstractions;

namespace HBA.Merchants.Application.Sellers.Commands.DeleteSeller;

internal sealed class DeleteSellerCommandHandler : ICommandHandler<DeleteSellerCommand>
{
    private readonly ISellerRepository _sellerRepository;
    private readonly ISellerUnitOfWork _unitOfWork;

    public DeleteSellerCommandHandler(ISellerRepository sellerRepository, ISellerUnitOfWork unitOfWork)
    {
        _sellerRepository = sellerRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(DeleteSellerCommand command, CancellationToken cancellationToken)
    {
        var seller = await _sellerRepository.GetByIdAsync(new SellerId(command.SellerId), cancellationToken);
        if (seller is null)
        {
            return Result.Failure(Error.NotFound("sellers.seller.not_found", $"Vendeur {command.SellerId} introuvable."));
        }

        // ═════════════════════════════════════════════════════════════════════
        // LES PIÈCES D'IDENTITÉ SONT TOUJOURS EFFACÉES — PAR ÉVÉNEMENT DÉSORMAIS.
        //
        // CE QUE FAISAIT LA VERSION PRÉCÉDENTE, ET CE QU'ELLE PROTÉGEAIT.
        //
        // Elle appelait le stockage en boucle AVANT le retrait, et REFUSAIT la
        // suppression du vendeur si un seul fichier résistait. L'argument était
        // juste : se contenter d'un log aurait fait croire l'effacement fait,
        // tandis que les pièces seraient restées — désormais introuvables, faute
        // de toute ligne pour les désigner.
        //
        // CE QUI REMPLACE CETTE GARANTIE.
        //
        // `MarkForDeletion` lève un `KybDocumentRemovedDomainEvent` PAR PIÈCE, et
        // ces messages partent par l'outbox — écrits dans la MÊME transaction que
        // la suppression. La référence au fichier n'est donc jamais perdue : elle
        // passe de la ligne KYB au message d'outbox, rejoué jusqu'à réussite, et un
        // message en souffrance nomme exactement le fichier qui résiste.
        //
        // CE QUE ÇA CHANGE : la suppression du vendeur ne peut plus ÉCHOUER
        // parce que le stockage a hoqueté. C'est voulu — un admin bloqué par une
        // indisponibilité de dix secondes finit par contourner, et c'est ainsi
        // qu'on invente les suppressions manuelles en base.
        // ═════════════════════════════════════════════════════════════════════

        // Émet l'événement de purge AVANT le retrait : le dispatch des domain events
        // lit le ChangeTracker avant le SaveChanges, l'agrégat (encore tracké, à
        // l'état Deleted) porte donc bien son événement.
        seller.MarkForDeletion();
        _sellerRepository.Remove(seller);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
