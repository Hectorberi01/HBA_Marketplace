/**
 * PAGINATION.
 *
 * ELLE DIT OÙ L'ON EST, pas seulement où aller. « 21 à 40 sur 137 » répond à la
 * question qu'on se pose vraiment ; deux flèches nues n'y répondent pas.
 *
 * Les bornes sont calculées à partir du total rendu par le serveur, jamais du
 * nombre de lignes reçues : la dernière page en contient moins, et compter les
 * lignes ferait reculer le total à l'écran.
 */
export default function Pagination({
    page,
    taille,
    total,
    onPage,
    onTaille,
    desactive,
}: {
    page: number
    taille: number
    total: number
    onPage: (p: number) => void
    onTaille: (t: number) => void
    desactive?: boolean
}) {
    const pages = taille > 0 ? Math.max(1, Math.ceil(total / taille)) : 1
    const debut = total === 0 ? 0 : (page - 1) * taille + 1
    const fin = Math.min(page * taille, total)

    return (
        <div className="pagination">
            <span className="pagination__compte">
                {total === 0 ? 'Aucun élément' : `${debut} à ${fin} sur ${total}`}
            </span>

            <label className="pagination__taille">
                Par page
                <select
                    value={taille}
                    onChange={e => onTaille(Number(e.target.value))}
                    disabled={desactive}
                >
                    {[20, 50, 100].map(t => (
                        <option key={t} value={t}>
                            {t}
                        </option>
                    ))}
                </select>
            </label>

            <div className="pagination__boutons">
                <button
                    type="button"
                    onClick={() => onPage(page - 1)}
                    disabled={desactive || page <= 1}
                >
                    Précédent
                </button>
                <span className="pagination__position">
                    Page {page} sur {pages}
                </span>
                <button
                    type="button"
                    onClick={() => onPage(page + 1)}
                    disabled={desactive || page >= pages}
                >
                    Suivant
                </button>
            </div>
        </div>
    )
}
