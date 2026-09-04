/**
 * RÉPARTITION PAR STATUT, telle que le serveur la rend dans `facets`.
 *
 * Elle sert de filtre ET de vue d'ensemble : voir « UnderReview 3 » sur une
 * console de commandes est l'information qui déclenche le geste, avant même
 * qu'on ait cherché quoi que ce soit.
 *
 * ELLE N'EST PAS RECALCULÉE CÔTÉ NAVIGATEUR. Compter les lignes de la page
 * courante donnerait la répartition des vingt lignes affichées, présentée comme
 * celle de la plateforme entière — un chiffre faux qui a l'air juste.
 */
export default function Facettes({
    facettes,
    actif,
    onChoisir,
    libelle,
}: {
    facettes: Record<string, number> | null
    actif: string | null
    onChoisir: (statut: string | null) => void
    libelle: (statut: string) => string
}) {
    if (!facettes || Object.keys(facettes).length === 0) return null

    const total = Object.values(facettes).reduce((s, n) => s + n, 0)
    const entrees = Object.entries(facettes).sort((a, b) => b[1] - a[1])

    return (
        <div className="facettes" role="group" aria-label="Filtrer par statut">
            <button
                type="button"
                className={`facette ${actif === null ? 'is-active' : ''}`}
                aria-pressed={actif === null}
                onClick={() => onChoisir(null)}
            >
                Tous <span className="facette__compte">{total}</span>
            </button>
            {entrees.map(([statut, compte]) => (
                <button
                    key={statut}
                    type="button"
                    className={`facette ${actif === statut ? 'is-active' : ''}`}
                    aria-pressed={actif === statut}
                    onClick={() => onChoisir(actif === statut ? null : statut)}
                >
                    {libelle(statut)} <span className="facette__compte">{compte}</span>
                </button>
            ))}
        </div>
    )
}
