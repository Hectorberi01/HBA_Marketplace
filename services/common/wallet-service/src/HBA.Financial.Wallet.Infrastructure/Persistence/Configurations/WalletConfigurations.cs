using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using HBA.Financial.Wallet.Domain.Wallets;
using HBA.Shared.Infrastructure.Persistence;

namespace HBA.Financial.Wallet.Infrastructure.Persistence.Configurations;

internal sealed class SellerWalletConfiguration : IEntityTypeConfiguration<SellerWallet>
{
    public void Configure(EntityTypeBuilder<SellerWallet> builder)
    {

        // ═════════════════════════════════════════════════════════════════════
        // VERROU OPTIMISTE — SANS LUI, UN VENDEUR PEUT SE FAIRE VERSER 5× SON SOLDE.
        //
        // `Withdraw()` lit le solde, le vérifie, le soustrait. Cinq requêtes de retrait
        // en parallèle lisent toutes le MÊME solde, passent toutes le contrôle, et
        // créent CINQ demandes complètes. À l'approbation, cinq versements partent pour
        // un seul solde. C'est de l'argent réel, et le bug est trivial à déclencher.
        //
        // (`UsePostgresRowVersion` — l'API Npgsql `UseXminAsConcurrencyToken` est dépréciée
        //  et casse la build en « warnings = errors » ; notre extension fait exactement
        //  ce qu'elle faisait. Voir ConcurrencyTokenExtensions.)
        //
        // `xmin` est une colonne SYSTÈME de PostgreSQL : elle existe déjà sur chaque
        // ligne et porte le numéro de la transaction qui l'a écrite en dernier. On ne
        // l'ajoute pas, on la LIT. Rien à changer dans le modèle de domaine.
        //
        // EF l'inclut désormais dans la clause WHERE de chaque UPDATE. Si une autre
        // transaction a modifié la ligne entre-temps, l'UPDATE touche 0 ligne et EF
        // lève `DbUpdateConcurrencyException` — traduite en 409 (voir
        // ServiceExceptionMiddleware).
        //
        // AUCUN RETRY AUTOMATIQUE, ET C'EST DÉLIBÉRÉ.
        //
        // ModuleDbContext dispatche les événements de domaine AVANT
        // base.SaveChangesAsync, et draine les événements d'intégration vers l'outbox.
        // Rejouer la commande dans le MÊME scope re-dispatcherait ces événements et
        // dupliquerait les messages d'outbox. On échoue donc franchement en 409 ; le
        // client rejoue avec une requête neuve (les PSP le font d'eux-mêmes sur leurs
        // webhooks).
        // ═════════════════════════════════════════════════════════════════════
        builder.UsePostgresRowVersion();
        builder.ToTable("seller_wallets");
        builder.HasKey(w => w.Id);
        builder.Property(w => w.Id)
            .HasConversion(id => id.Value, value => new SellerWalletId(value))
            .ValueGeneratedNever();

        builder.Property(w => w.SellerId).IsRequired();
        builder.Property(w => w.Currency).HasMaxLength(3).IsRequired();
        builder.Property(w => w.PendingBalance).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(w => w.AvailableBalance).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(w => w.CreatedAtUtc).IsRequired();
        builder.Property(w => w.UpdatedAtUtc).IsRequired();

        builder.HasIndex(w => w.SellerId).IsUnique();

        builder.Ignore(w => w.DomainEvents);
    }
}

internal sealed class DriverWalletConfiguration : IEntityTypeConfiguration<DriverWallet>
{
    public void Configure(EntityTypeBuilder<DriverWallet> builder)
    {
        // ═════════════════════════════════════════════════════════════════════
        // VERROU OPTIMISTE — même raison que pour le vendeur, et le risque est ici
        // PLUS élevé, pas moins.
        //
        // Un livreur enchaîne les courses : plusieurs remises peuvent aboutir dans
        // la même seconde, et l'outbox les traite en parallèle. Sans verrou, deux
        // crédits simultanés lisent le même solde et le second ÉCRASE le premier —
        // une course payée qui disparaît, sans erreur nulle part.
        //
        // (`UsePostgresRowVersion` : voir l'encadré de SellerWalletConfiguration.)
        // ═════════════════════════════════════════════════════════════════════
        builder.UsePostgresRowVersion();
        builder.ToTable("driver_wallets");
        builder.HasKey(w => w.Id);
        builder.Property(w => w.Id)
            .HasConversion(id => id.Value, value => new DriverWalletId(value))
            .ValueGeneratedNever();

        builder.Property(w => w.DriverId).IsRequired();
        builder.Property(w => w.Currency).HasMaxLength(3).IsRequired();
        builder.Property(w => w.AvailableBalance).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(w => w.LifetimeEarned).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(w => w.CreatedAtUtc).IsRequired();
        builder.Property(w => w.UpdatedAtUtc).IsRequired();

        // UN SEUL portefeuille par livreur. Le handler crée le portefeuille quand il
        // n'en trouve pas ; sous concurrence, deux premières courses simultanées le
        // créeraient deux fois, et le solde se scinderait en silence.
        builder.HasIndex(w => w.DriverId).IsUnique();

        builder.Ignore(w => w.DomainEvents);
    }
}

internal sealed class CustomerWalletConfiguration : IEntityTypeConfiguration<CustomerWallet>
{
    public void Configure(EntityTypeBuilder<CustomerWallet> builder)
    {
        // ═════════════════════════════════════════════════════════════════════
        // VERROU OPTIMISTE — SANS LUI, UN REMBOURSEMENT SUR DEUX DISPARAÎT.
        //
        // Ce portefeuille est écrit par un flux CONCURRENT par nature : un
        // remboursement arrive d'un retour, un autre d'une annulation de commande,
        // un troisième d'un geste administratif — les trois passent par
        // `RefundPaymentCommand` et peuvent aboutir dans la même seconde. Sans
        // verrou, deux crédits simultanés lisent le même solde et le second ÉCRASE
        // le premier : un remboursement que le client ne reçoit jamais, sans erreur
        // nulle part, sur de l'argent qu'on lui doit.
        //
        // (`UsePostgresRowVersion` : voir l'encadré de SellerWalletConfiguration —
        //  `xmin` est une colonne SYSTÈME de PostgreSQL, on ne l'ajoute pas, on la
        //  LIT ; l'API Npgsql `UseXminAsConcurrencyToken` est dépréciée et casse la
        //  build en « warnings = errors ».)
        //
        // AUCUN RETRY AUTOMATIQUE, comme partout ailleurs dans ce module : le
        // conflit sort en 409 et l'appelant rejoue avec une requête neuve. Sa clé
        // d'idempotence garantit que le rejeu ne crédite pas deux fois.
        // ═════════════════════════════════════════════════════════════════════
        builder.UsePostgresRowVersion();
        builder.ToTable("customer_wallets");
        builder.HasKey(w => w.Id);
        builder.Property(w => w.Id)
            .HasConversion(id => id.Value, value => new CustomerWalletId(value))
            .ValueGeneratedNever();

        builder.Property(w => w.CustomerId).IsRequired();
        builder.Property(w => w.Currency).HasMaxLength(3).IsRequired();
        builder.Property(w => w.AvailableBalance).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(w => w.LifetimeRefunded).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(w => w.CreatedAtUtc).IsRequired();
        builder.Property(w => w.UpdatedAtUtc).IsRequired();

        // ═════════════════════════════════════════════════════════════════════
        // UN CLIENT, UN PORTEFEUILLE — ET CET INDEX EST LA SEULE CHOSE QUI LE TIENT.
        //
        // `WalletMutations.GetOrCreateCustomerAsync` crée le portefeuille quand il
        // n'en trouve pas. Deux remboursements simultanés sur un client qui n'en a
        // pas encore lisent tous deux « absent » et en créent DEUX : le solde se
        // scinde en silence, et l'un des deux devient invisible — le client voit une
        // partie de son argent, jamais l'autre.
        //
        // Le cache par requête de `WalletMutations` ferme la duplication à
        // l'intérieur d'un `SaveChanges` ; il ne voit rien de ce que fait la requête
        // d'à côté. Seule la base peut fermer cette fenêtre-là.
        // ═════════════════════════════════════════════════════════════════════
        builder.HasIndex(w => w.CustomerId).IsUnique();

        builder.Ignore(w => w.DomainEvents);
    }
}

internal sealed class CustomerWithdrawalConfiguration : IEntityTypeConfiguration<CustomerWithdrawal>
{
    public void Configure(EntityTypeBuilder<CustomerWithdrawal> builder)
    {
        // ═════════════════════════════════════════════════════════════════════
        // VERROU OPTIMISTE — IL MANQUAIT ICI, ET NULLE PART AILLEURS (audit 2.6).
        //
        // CE QUI ÉTAIT CASSÉ. `CustomerWithdrawal` était la SEULE entité mutable de
        // ce module sans `UsePostgresRowVersion()`. Les quatre portefeuilles
        // l'avaient, `WalletTransaction` l'avait ; la demande de retrait client, non.
        // Rien ne le signalait : l'absence d'une ligne ne se voit pas.
        //
        // `MarkCustomerWithdrawalPaidCommandHandler` et
        // `RejectCustomerWithdrawalCommandHandler` font tous deux une
        // lecture-modification-écriture, et la garde d'état n'est vérifiée QU'EN
        // MÉMOIRE. Deux opérateurs qui traitent la même demande à quelques secondes
        // d'écart la lisent tous deux au statut `Requested` : l'un marque payé — le
        // virement part —, l'autre rejette, ce qui exécute `wallet.Restore(montant)`
        // et RECRÉDITE le client d'une somme déjà virée.
        //
        // Aucune exception, aucun journal : les deux écritures réussissent, chacune
        // étant correcte de son point de vue.
        //
        // AUCUNE MIGRATION N'EST NÉCESSAIRE, ET C'EST LE POINT QUI SURPREND.
        // `xmin` est une colonne SYSTÈME de PostgreSQL : elle existe déjà sur chaque
        // ligne de `customer_withdrawals` et porte le numéro de la transaction qui
        // l'a écrite en dernier. On ne l'ajoute pas, on la LIT. Seul le snapshot du
        // modèle change — le schéma de la base, lui, ne bouge pas d'un octet.
        //
        // Le raisonnement complet — pourquoi `UsePostgresRowVersion` plutôt que
        // l'API Npgsql dépréciée, et pourquoi AUCUN retry automatique — est dans
        // l'encadré de `SellerWalletConfiguration`. Il vaut mot pour mot ici.
        //
        // CE QUE CE VERROU NE COUVRE PAS. Il fait échouer la SECONDE écriture, il ne
        // dit pas laquelle des deux était la bonne : l'opérateur perdant reçoit un
        // 409 et doit relire la demande. C'est le comportement voulu — la seule
        // alternative serait de choisir à sa place entre « payé » et « rejeté », ce
        // qu'aucune règle ne permet de trancher.
        // ═════════════════════════════════════════════════════════════════════
        builder.UsePostgresRowVersion();
        builder.ToTable("customer_withdrawals");
        builder.HasKey(w => w.Id);
        builder.Property(w => w.Id)
            .HasConversion(id => id.Value, value => new CustomerWithdrawalId(value))
            .ValueGeneratedNever();

        builder.Property(w => w.CustomerId).IsRequired();
        builder.Property(w => w.Amount).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(w => w.Currency).HasMaxLength(3).IsRequired();

        // DESTINATION FIGÉE À LA DEMANDE, ET NON NULLABLE.
        //
        // C'est ce couple, et rien d'autre, que l'administrateur recopie chez le
        // prestataire. `Withdrawal` porte les mêmes colonnes en NULLABLE — une dette,
        // pas un choix : ses demandes antérieures à la colonne n'ont pas de
        // destination. Cette table naît avec les siennes, il n'y a donc aucun repli
        // sur « le compte courant du client » à écrire, et c'est justement le repli
        // qui rouvrait la faille côté vendeur. Voir `CustomerWithdrawal.Msisdn`.
        builder.Property(w => w.Msisdn).HasMaxLength(30).IsRequired();
        builder.Property(w => w.Provider).HasMaxLength(30).IsRequired();

        builder.Property(w => w.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(w => w.RequestedAtUtc).IsRequired();
        builder.Property(w => w.DecidedAtUtc);
        builder.Property(w => w.DecidedByUserId);

        // Référence du virement saisie par l'administrateur : la seule preuve que
        // l'argent est parti. 200 caractères, comme `ProviderRef` sur les retraits —
        // les identifiants de transaction des prestataires y tiennent tous.
        builder.Property(w => w.ExternalReference).HasMaxLength(200);
        builder.Property(w => w.AdminNote).HasMaxLength(500);

        // 180 caractères : la même borne que `customer_refunds` et
        // `payments.payment_refunds`. Trois tables qui portent la clé d'un même
        // en-tête HTTP n'ont aucune raison de la tronquer à trois endroits
        // différents.
        builder.Property(w => w.IdempotencyKey).HasMaxLength(180).IsRequired();

        builder.HasIndex(w => w.CustomerId);

        // La file d'administration lit par statut, tous clients confondus. Sans
        // index, elle balaie toute la table à chaque ouverture de l'écran.
        builder.HasIndex(w => w.Status);

        // ═════════════════════════════════════════════════════════════════════
        // CLÉ D'IDEMPOTENCE — CE QUI EMPÊCHE UN DOUBLE-CLIC DE VIDER LE SOLDE.
        //
        // Le gestionnaire refuse déjà une demande sans clé. Mais entre sa lecture et
        // son écriture, deux requêtes simultanées peuvent toutes deux se croire les
        // premières : une vérification applicative ne ferme JAMAIS cette fenêtre,
        // seule la base le peut. Sans cet index, un double-clic retient DEUX fois le
        // solde et pose deux lignes identiques dans la file — que l'administrateur
        // paierait probablement toutes les deux.
        //
        // PORTÉE GLOBALE ICI, ALORS QUE `customer_refunds` EST SCOPÉ PAR COMMANDE.
        //
        // La différence est réelle et assumée. Là-bas, la clé de deux commandes
        // DIFFÉRENTES pouvait se recouper et bloquer un remboursement dû. Ici, la
        // clé accompagne une demande faite par un humain depuis son application :
        // il n'existe pas de « seconde demande légitime portant la même clé au même
        // instant ». Restreindre la portée au client aurait été possible ; ne pas le
        // faire coûte, au pire, le refus d'une demande d'un AUTRE client qui aurait
        // recyclé exactement le même jeton opaque — le client refait sa demande.
        // Le risque inverse — retenir deux fois un solde — coûte de l'argent.
        //
        // Pas de filtre partiel : la colonne est obligatoire.
        // ═════════════════════════════════════════════════════════════════════
        builder.HasIndex(w => w.IdempotencyKey).IsUnique();

        builder.Ignore(w => w.DomainEvents);
    }
}

internal sealed class PlatformWalletConfiguration : IEntityTypeConfiguration<PlatformWallet>
{
    public void Configure(EntityTypeBuilder<PlatformWallet> builder)
    {

        // ═════════════════════════════════════════════════════════════════════
        // VERROU OPTIMISTE — le portefeuille plateforme est écrit par tous les flux (commission,
        // frais, contre-passations). Sans verrou, deux écritures simultanées s'écrasent :
        // une commission encaissée disparaît des comptes.
        //
        // (`UsePostgresRowVersion` — l'API Npgsql `UseXminAsConcurrencyToken` est dépréciée
        //  et casse la build en « warnings = errors » ; notre extension fait exactement
        //  ce qu'elle faisait. Voir ConcurrencyTokenExtensions.)
        //
        // `xmin` est une colonne SYSTÈME de PostgreSQL : elle existe déjà sur chaque
        // ligne et porte le numéro de la transaction qui l'a écrite en dernier. On ne
        // l'ajoute pas, on la LIT. Rien à changer dans le modèle de domaine.
        //
        // EF l'inclut désormais dans la clause WHERE de chaque UPDATE. Si une autre
        // transaction a modifié la ligne entre-temps, l'UPDATE touche 0 ligne et EF
        // lève `DbUpdateConcurrencyException` — traduite en 409 (voir
        // ServiceExceptionMiddleware).
        //
        // AUCUN RETRY AUTOMATIQUE, ET C'EST DÉLIBÉRÉ.
        //
        // ModuleDbContext dispatche les événements de domaine AVANT
        // base.SaveChangesAsync, et draine les événements d'intégration vers l'outbox.
        // Rejouer la commande dans le MÊME scope re-dispatcherait ces événements et
        // dupliquerait les messages d'outbox. On échoue donc franchement en 409 ; le
        // client rejoue avec une requête neuve (les PSP le font d'eux-mêmes sur leurs
        // webhooks).
        // ═════════════════════════════════════════════════════════════════════
        builder.UsePostgresRowVersion();
        builder.ToTable("platform_wallet");
        builder.HasKey(w => w.Id);
        builder.Property(w => w.Id).ValueGeneratedNever();

        builder.Property(w => w.Currency).HasMaxLength(3).IsRequired();
        builder.Property(w => w.CommissionBalance).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(w => w.ProviderFeeBalance).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(w => w.ShippingBalance).HasColumnType("numeric(18,2)").IsRequired();
        // Total reversé aux clients en remboursements directs. `HasDefaultValue(0)` : les
        // lignes existantes (une seule, le singleton) prennent 0 à l'application de la migration.
        builder.Property(w => w.RefundsBalance).HasColumnType("numeric(18,2)").IsRequired().HasDefaultValue(0m);
        builder.Property(w => w.UpdatedAtUtc).IsRequired();

        builder.Ignore(w => w.DomainEvents);
    }
}

internal sealed class CustomerRefundConfiguration : IEntityTypeConfiguration<CustomerRefund>
{
    public void Configure(EntityTypeBuilder<CustomerRefund> builder)
    {
        builder.ToTable("customer_refunds");
        builder.HorodateLesModifications();
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id)
            .HasConversion(id => id.Value, value => new CustomerRefundId(value))
            .ValueGeneratedNever();

        builder.Property(r => r.OrderId).IsRequired();
        builder.Property(r => r.BuyerId).IsRequired();
        builder.Property(r => r.Amount).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(r => r.Currency).HasMaxLength(3).IsRequired();
        builder.Property(r => r.Reason).HasMaxLength(500).IsRequired();
        builder.Property(r => r.Msisdn).HasMaxLength(30).IsRequired();
        builder.Property(r => r.Provider).HasMaxLength(30).IsRequired();
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(r => r.ProviderRef).HasMaxLength(200);
        builder.Property(r => r.FailureReason).HasMaxLength(500);
        builder.Property(r => r.CreatedAtUtc).IsRequired();
        builder.Property(r => r.SentToPspAtUtc);
        builder.Property(r => r.CompletedAtUtc);

        // 180 caractères : la même borne que `payments.payment_refunds`. Deux tables
        // qui portent la clé d'un même en-tête HTTP n'ont aucune raison de la tronquer
        // à deux endroits différents.
        builder.Property(r => r.IdempotencyKey).HasMaxLength(180).IsRequired();

        builder.HasIndex(r => r.OrderId);
        builder.HasIndex(r => r.Status);

        // ═════════════════════════════════════════════════════════════════════
        // LA SEULE CHOSE QUI EMPÊCHE UN SECOND VIREMENT.
        //
        // CETTE TABLE N'AVAIT AUCUNE CLÉ D'IDEMPOTENCE.
        //
        // Un `CustomerRefund` est un versement Mobile Money vers un CLIENT. Sans
        // clé, un appel HTTP réessayé ou un rejeu créait une seconde ligne et
        // envoyait un second virement. L'argent est parti deux fois, et rien ne le
        // rattrape : un payout exécuté chez FedaPay ne s'annule pas, et le client
        // n'a aucune raison de signaler qu'il a trop reçu.
        //
        // PORTÉE `(OrderId, IdempotencyKey)`, PAS LA SEULE COLONNE.
        //
        // La clé vient VERBATIM de l'en-tête `Idempotency-Key` : c'est un jeton
        // opaque choisi par le client, qui n'embarque ni la commande ni l'acheteur.
        // Rien dans le code ne la rend globalement unique — exactement la situation
        // de `payment_refunds`, dont la clé peut aussi venir de l'appelant et qui
        // est pour cette raison indexée `(PaymentId, IdempotencyKey)`.
        //
        // Une portée globale aurait un coût réel : deux commandes DIFFÉRENTES dont
        // les clés se recoupent — un client qui recycle un jeton, un générateur mal
        // amorcé — verraient le second remboursement, pourtant légitime, refusé par
        // la contrainte. Ce serait de l'argent dû, bloqué par une collision qui n'a
        // rien à voir avec un rejeu.
        //
        // Le danger qu'on ferme est « le MÊME versement exécuté deux fois ». Deux
        // versements sur deux commandes distinctes n'en sont pas un, même s'ils
        // partagent un en-tête. La portée suit donc la commande.
        //
        // Pas de filtre partiel : la colonne est obligatoire.
        //
        // AUCUN ÉMETTEUR N'ÉCRIT ENCORE DANS CETTE TABLE (ISSUE-009).
        // `InitiateCustomerRefundCommand` n'a aujourd'hui aucun appelant. C'est
        // délibéré et c'est le bon ordre : on rend la table sûre AVANT que quoi que
        // ce soit n'y écrive, pas après le premier double virement.
        // ═════════════════════════════════════════════════════════════════════
        builder.HasIndex(r => new { r.OrderId, r.IdempotencyKey }).IsUnique();

        builder.Ignore(r => r.DomainEvents);
    }
}

internal sealed class WithdrawalConfiguration : IEntityTypeConfiguration<Withdrawal>
{
    public void Configure(EntityTypeBuilder<Withdrawal> builder)
    {
        builder.ToTable("withdrawals");
        builder.HorodateLesModifications();
        builder.HasKey(w => w.Id);
        builder.Property(w => w.Id)
            .HasConversion(id => id.Value, value => new WithdrawalId(value))
            .ValueGeneratedNever();

        builder.Property(w => w.SellerId).IsRequired();
        builder.Property(w => w.Amount).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(w => w.Currency).HasMaxLength(3).IsRequired();
        builder.Property(w => w.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(w => w.ProviderRef).HasMaxLength(200);
        builder.Property(w => w.FailureReason).HasMaxLength(500);
        builder.Property(w => w.CreatedAtUtc).IsRequired();
        builder.Property(w => w.CompletedAtUtc);

        // LA DESTINATION DU VIREMENT, FIGÉE À LA DEMANDE.
        //
        // NULLABLES par nécessité, pas par choix : les demandes créées avant ces
        // colonnes n'ont pas de destination. Les rendre obligatoires ferait
        // échouer la migration sur toute demande déjà en attente. Voir
        // ApproveWithdrawalCommandHandler pour le repli, et son échéance.
        builder.Property(w => w.PayoutProvider).HasMaxLength(30);
        builder.Property(w => w.PayoutAccountNumber).HasMaxLength(40);
        builder.Property(w => w.PayoutAccountName).HasMaxLength(120);

        builder.HasIndex(w => w.SellerId);

        // TROIS REQUÊTES FILTRENT SUR `Status`, ET AUCUN INDEX NE LES SERVAIT.
        //
        // `ListByStatusAsync`, la reprise des retraits en cours
        // (`Status == Processing`) et le compte par statut balayaient toute la
        // table — qui ne décroît jamais, un retrait par demande, indéfiniment.
        // La reprise tourne périodiquement : c'est un balayage complet à chaque
        // tour, pour trouver une poignée de lignes.
        //
        // `customer_withdrawals` L'AVAIT DÉJÀ (voir plus haut). Il y a DEUX
        // tables de retrait dans ce schéma, et seule celle des CLIENTS était
        // servie. L'audit n'en distinguait qu'une ; c'est celle des VENDEURS —
        // l'argent qui sort vers un compte Mobile Money — qui ne l'avait pas.
        builder.HasIndex(w => w.Status);

        // ═════════════════════════════════════════════════════════════════════
        // JETON DE CONCURRENCE — L'ARGENT SORT D'ICI, ET RIEN NE SÉRIALISAIT.
        //
        // Les quatre tables de portefeuille qui ALIMENTENT ce retrait ont toutes
        // un jeton (`seller_wallets`, `driver_wallets`, `customer_wallets`,
        // `platform_wallet`). La table qui décide de faire PARTIR l'argent n'en
        // avait aucun.
        //
        // Le chemin exposé : deux approbations simultanées de la même demande, ou
        // une approbation concurrente d'une réconciliation. `MarkProcessing`,
        // `Complete`, `Fail` et `Reject` écrivent tous `Status` sur la ligne — donc
        // un `UPDATE` est bien émis, et le jeton n'est PAS inerte. C'est la
        // vérification qu'exige l'encadré d'`UsePostgresRowVersion` : un jeton posé
        // sur un agrégat dont le chemin n'écrit que des lignes ENFANTS ne protège
        // rien.
        //
        // AUCUNE COLONNE N'EST CRÉÉE : `xmin` est une colonne système que chaque
        // ligne PostgreSQL porte déjà. La migration qui accompagne ce changement ne
        // touche donc pas cette table.
        //
        // AUCUN REJEU AUTOMATIQUE : le perdant reçoit un 409. Rejouer dans le
        // même scope re-dispatcherait les événements de domaine et dupliquerait
        // l'outbox — on corrigerait un bug d'argent en en créant un autre.
        // ═════════════════════════════════════════════════════════════════════
        builder.UsePostgresRowVersion();

        builder.Ignore(w => w.DomainEvents);
    }
}

internal sealed class WalletTransactionConfiguration : IEntityTypeConfiguration<WalletTransaction>
{
    public void Configure(EntityTypeBuilder<WalletTransaction> builder)
    {
        builder.ToTable("wallet_transactions");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();

        builder.Property(t => t.OwnerId).IsRequired();
        builder.Property(t => t.OwnerType).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(t => t.Account).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(t => t.Direction).HasConversion<string>().HasMaxLength(10).IsRequired();
        builder.Property(t => t.Amount).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(t => t.Currency).HasMaxLength(3).IsRequired();
        builder.Property(t => t.Reason).HasMaxLength(50).IsRequired();
        builder.Property(t => t.ReferenceType).HasMaxLength(30);
        builder.Property(t => t.ReferenceId);
        builder.Property(t => t.CreatedAtUtc).IsRequired();

        // §10.13 : regroupement des écritures d'une opération, et solde résultant.
        //
        // La valeur par défaut sur TransactionId ne sert QU'AUX lignes déjà en base :
        // chacune y devient sa propre opération, ce qui est exact — elles n'étaient
        // effectivement pas groupées.
        builder.Property(t => t.TransactionId).IsRequired().HasDefaultValueSql("gen_random_uuid()");
        builder.Property(t => t.BalanceAfter).HasColumnType("numeric(18,2)");

        builder.HasIndex(t => new { t.OwnerId, t.CreatedAtUtc });

        // Toutes les écritures d'une opération se lisent ensemble : c'est la requête
        // du rapprochement comptable, et sans index elle balaie toute la table.
        builder.HasIndex(t => t.TransactionId).HasDatabaseName("ix_wallet_transactions_transaction");

        // ─────────────────────────────────────────────────────────────────────────
        // INDEX D'IDEMPOTENCE — la seule garantie qui tienne sous concurrence.
        //
        // Le handler de contre-passation vérifie d'abord « ce remboursement a-t-il déjà
        // été passé ? ». Mais entre la lecture et l'écriture, deux rejeux simultanés
        // peuvent tous deux se croire les premiers. Une vérification applicative ne
        // ferme JAMAIS complètement cette fenêtre : seule la base le peut.
        //
        // L'unicité porte sur (référence, propriétaire, compte) : un remboursement
        // produit au plus une écriture par compte et par propriétaire — solde à venir
        // et solde principal du vendeur, commission et frais de la plateforme. Un
        // doublon lève une violation de contrainte, la transaction est annulée, et le
        // vendeur n'est pas débité deux fois.
        //
        // L'index est PARTIEL (`HasFilter`) : il ne s'applique qu'aux écritures de
        // remboursement. Les lignes de commande, elles, sont légitimement multiples
        // pour une même référence.
        //
        // REQUIERT UNE MIGRATION :
        //    dotnet ef migrations add AddRefundReversalIdempotencyIndex \
        //      -p src/Modules/Wallet/HBA.Financial.Wallet.Infrastructure
        // ─────────────────────────────────────────────────────────────────────────
        // Le nom de colonne du filtre est en PascalCase et entre guillemets doubles :
        // ce projet n'applique AUCUNE convention snake_case (vérifié dans le
        // ModelSnapshot). Écrire `reference_type` produirait un index invalide, et
        // l'erreur ne surgirait qu'au moment d'appliquer la migration.
        builder.HasIndex(t => new { t.ReferenceType, t.ReferenceId, t.OwnerId, t.Account })
            .IsUnique()
            .HasFilter("\"ReferenceType\" = 'refund'")
            .HasDatabaseName("ux_wallet_transactions_refund_reversal");

        // ─────────────────────────────────────────────────────────────────────────
        // IDEMPOTENCE DES GAINS DE COURSE — le second verrou du crédit livreur.
        //
        // CreditDriverEarningCommand consulte déjà le grand livre avant d'écrire.
        // Mais entre cette lecture et l'écriture, deux rejeux d'outbox simultanés
        // peuvent tous deux se croire premiers : une vérification applicative ne
        // ferme jamais cette fenêtre, seule la base le peut.
        //
        // Un index SÉPARÉ plutôt qu'un filtre élargi sur celui des remboursements :
        // les deux flux ont des règles différentes — un remboursement produit une
        // écriture PAR COMPTE et par propriétaire, une course en produit UNE, point.
        // Fondre les deux dans un filtre « IN (...) » lierait leur évolution sans
        // qu'aucune ligne ne le dise.
        //
        // Le filtre nomme la colonne en PascalCase entre guillemets doubles : ce
        // projet n'applique AUCUNE convention snake_case (vérifié dans le
        // ModelSnapshot). La chaîne doit rester identique à
        // CreditDriverEarningCommandHandler.DriverEarningReferenceType.
        //
        // « UNE, POINT » N'EST PAS UNE FORMULE : L'INDEX NE PORTE NI LE
        // PROPRIÉTAIRE NI LE COMPTE, CONTRAIREMENT À CELUI DES REMBOURSEMENTS.
        //
        // La sortie du solde livraison de la plateforme, ajoutée après cet index,
        // portait le MÊME type de référence que le crédit du livreur. Deux lignes,
        // une seule clé, un seul SaveChanges : la contrainte sautait dès le PREMIER
        // paiement — livreur non crédité, message rejoué sans fin sur une erreur
        // qui ne passerait jamais. Elle porte désormais `driver_share`.
        //
        // Toute nouvelle écriture rattachée à une course doit donc prendre SON
        // propre type de référence, ou cet index l'interdira.
        // ─────────────────────────────────────────────────────────────────────────
        builder.HasIndex(t => new { t.ReferenceType, t.ReferenceId })
            .IsUnique()
            .HasFilter("\"ReferenceType\" = 'driver_earning'")
            .HasDatabaseName("ux_wallet_transactions_driver_earning");

        // ─────────────────────────────────────────────────────────────────────────
        // IDEMPOTENCE DU CRÉDIT DE REMBOURSEMENT CLIENT (D33).
        //
        // `CreditCustomerRefundCommandHandler` consulte le grand livre avant
        // d'écrire. Entre cette lecture et l'écriture, deux rejeux simultanés
        // peuvent tous deux se croire premiers — payment-service réessaie, et le
        // second crédit rendrait une seconde fois le même argent. Seule la base
        // ferme cette fenêtre.
        //
        // La référence n'est pas un identifiant de dossier : c'est la clé
        // d'idempotence de l'appelant projetée dans l'espace des `Guid`, portée du
        // client comprise. Voir `WalletReference`.
        //
        // UN INDEX SÉPARÉ, ET UN TYPE DE RÉFÉRENCE SÉPARÉ.
        //
        // Un crédit produit UNE écriture, point — comme le gain de course, et
        // contrairement aux remboursements vendeur qui en produisent une par
        // compte. Le type est `customer_refund_credit` et NON `customer_refund`,
        // qui désigne déjà le coût plateforme d'un versement MoMo direct : les
        // confondre ferait entrer deux flux dans la même contrainte, et c'est
        // exactement ce qui a fait sauter `driver_earning` au premier paiement
        // (voir l'encadré ci-dessus).
        //
        // Le filtre nomme la colonne en PascalCase entre guillemets doubles : ce
        // projet n'applique AUCUNE convention snake_case. La chaîne doit rester
        // identique à `WalletMutations.CustomerRefundCreditReferenceType`.
        //
        // Les mouvements de VIREMENT client (`customer_withdrawal`) ne sont pas
        // couverts, et ne doivent pas l'être : une même demande produit deux
        // écritures légitimes — le débit à la demande, le crédit au refus.
        // ─────────────────────────────────────────────────────────────────────────
        // SURCHARGE NOMMÉE `HasIndex(expression, name)`, ET C'EST OBLIGATOIRE ICI.
        //
        // `HasIndex(...)` sans nom REND L'INDEX EXISTANT quand le jeu de colonnes est
        // déjà configuré. Les colonnes sont exactement celles de
        // `ux_wallet_transactions_driver_earning` : la version sans nom n'aurait pas
        // créé un second index, elle aurait ÉCRASÉ le filtre et le nom du premier.
        // Les gains de course auraient alors été indexés sur le filtre des
        // remboursements client — donc plus protégés du tout, en silence, et sans
        // qu'aucune ligne du modèle ne le dise.
        builder.HasIndex(
                t => new { t.ReferenceType, t.ReferenceId },
                "ux_wallet_transactions_customer_refund_credit")
            .IsUnique()
            .HasFilter("\"ReferenceType\" = 'customer_refund_credit'");

        builder.Ignore(t => t.DomainEvents);
    }
}
