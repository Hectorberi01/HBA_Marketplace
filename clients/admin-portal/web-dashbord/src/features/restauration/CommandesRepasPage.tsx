import { Link } from 'react-router-dom'

/**
 * COMMANDES REPAS — IL N'Y A RIEN À LIRE, ET C'EST LE SUJET DE CET ÉCRAN.
 *
 * `MapAdminGroup("/api/admin/food/orders")` monte DEUX routes, toutes deux en
 * POST : `review/resume` et `review/refund`. Aucun GET.
 *
 * Les trois requêtes de la couche application sont toutes portées — par
 * identifiant de commande, par acheteur, par restaurant. Aucune ne liste
 * l'ensemble, et il n'existe donc rien à monter.
 *
 * LA ROUTE VOISINE NE COMBLE PAS LE MANQUE. `/api/food/restaurant/orders`
 * appelle `GetStaffMembershipAsync(userId)` et rend 403 quand le compte
 * n'appartient au personnel d'aucun établissement — ce qui est le cas d'un
 * administrateur par construction.
 *
 * POURQUOI CETTE PAGE EXISTE PLUTÔT QUE D'ÊTRE RETIRÉE DU MENU.
 *
 * Retirer l'entrée ferait disparaître la question. Un écran qui explique où
 * sont réellement les commandes de repas, et ce qui manque pour qu'elles aient
 * leur propre file, vaut mieux qu'une absence que quelqu'un redécouvrira dans
 * six mois.
 *
 * UN TABLEAU AURAIT ÉTÉ POSSIBLE, ET IL AURAIT MENTI. `/api/admin/orders` rend
 * les commandes de repas parmi les autres — `OrderSummary.Kind` vaut « Food » —
 * mais n'accepte AUCUN filtre sur ce champ. Filtrer côté navigateur ne trierait
 * que la page affichée : on lirait « 4 commandes de repas » là où la plateforme
 * en a trois cents, et le nombre aurait l'air d'un total.
 */
export default function CommandesRepasPage() {
    return (
        <section className="ecran-liste">
            <header className="ecran-liste__tete">
                <h1>Commandes repas</h1>
            </header>

            <div className="carte-explication">
                <h2>Aucune liste n'est exposée par l'API</h2>
                <p>
                    Le groupe d'administration des commandes de repas ne monte que deux
                    routes, toutes deux en écriture : reprendre une commande en arbitrage et
                    la rembourser. Il n'existe aucune route de lecture, et aucune requête
                    côté service ne liste l'ensemble des commandes — les trois existantes
                    sont portées par commande, par acheteur ou par restaurant.
                </p>

                <h2>Où les trouver en attendant</h2>
                <p>
                    Les commandes de repas apparaissent dans la liste générale des commandes,
                    marquées <strong>Repas</strong> dans la colonne Type. C'est la même
                    donnée : <code>order-service</code> les porte toutes, avec un champ{' '}
                    <code>Kind</code>.
                </p>
                <p className="liens-utiles">
                    <Link to="/commandes">Toutes les commandes</Link>
                    {' · '}
                    {/*
                      * L'ARBITRAGE EST LE SEUL ENDROIT OÙ CES DEUX ROUTES SERVENT.
                      * Une commande de repas devenue inexécutable entre en
                      * « UnderReview » et y reste jusqu'à ce que quelqu'un tranche.
                      * Le lien pointe donc directement sur ce filtre.
                      */}
                    <Link to="/commandes?statut=UnderReview">Commandes en arbitrage</Link>
                </p>

                <h2>Ce qui manque pour que cet écran existe</h2>
                <p>
                    Une requête qui liste les commandes de repas — paginée, filtrable par
                    statut et par restaurant — et un <code>MapGet</code> qui la monte sur{' '}
                    <code>/api/admin/food/orders</code>. Le groupe, la garde de rôle et la
                    route de passerelle existent déjà : c'est la lecture qui manque, pas
                    l'accès.
                </p>
                <p className="indice">
                    Un tableau construit sur <code>/api/admin/orders</code> aurait été
                    possible, mais cette route n'accepte aucun filtre sur le type de
                    commande. Trier côté navigateur n'aurait trié que la page affichée, et le
                    compte obtenu aurait eu l'air d'un total.
                </p>
            </div>
        </section>
    )
}
