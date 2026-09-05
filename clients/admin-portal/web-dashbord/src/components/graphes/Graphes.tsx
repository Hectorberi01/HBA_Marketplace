import { type ReactNode } from 'react'

/**
 * ═══════════════════════════════════════════════════════════════════════════
 * TROIS GRAPHES, ÉCRITS EN SVG, SANS BIBLIOTHÈQUE.
 *
 * POURQUOI PAS RECHARTS. Le portail pèse 111 ko compressés. Recharts en ajoute
 * une quarantaine, et tire D3 avec lui, pour dessiner ici moins de vingt
 * rectangles. Ce qu'on perd — axes automatiques, animations, échelles
 * logarithmiques — n'est utilisé par aucun de ces trois graphes.
 *
 * LES COULEURS NE SONT PAS CHOISIES À L'ŒIL.
 *
 * La palette de statut vient du référentiel de visualisation et a été passée au
 * validateur contre les DEUX surfaces réelles du portail (#ffffff et #16171d) :
 *
 *   bon        #0ca30c    attention  #fab219
 *   critique   #d03b3b    sérieux    #ec835a
 *
 * L'ORDRE DES SEGMENTS EST UNE CONTRAINTE, PAS UNE PRÉFÉRENCE. Rangés
 * « bon, attention, sérieux, critique » — l'ordre qui vient naturellement —
 * attention et sérieux se touchent, et le validateur refuse : ΔE 13,6 en vision
 * NORMALE, sous le plancher de 15. Deux segments voisins que personne ne
 * distingue. L'ordre retenu, « encaissé, en attente, échoué, remboursé », est
 * aussi celui du récit métier, et il passe : ΔE 11,3 en vision déficiente,
 * 15,7 en vision normale, dans les deux thèmes.
 *
 * DEUX COULEURS RESTENT SOUS 3:1 EN THÈME CLAIR — attention et sérieux. C'est
 * assumé par la palette elle-même, à condition que la couleur ne porte JAMAIS
 * seule le sens : chaque segment est donc étiqueté, et le tableau de valeurs
 * existe sous chaque graphe.
 *
 * LE BLEU DES SÉRIES N'EST PAS L'ACCENT DU PORTAIL. `--accent` (violet) marque
 * ce qui est CLIQUABLE. Peindre les barres avec lui ferait promettre une
 * interaction à des rectangles qui n'en offrent aucune.
 * ═══════════════════════════════════════════════════════════════════════════
 */

export type Part = {
    cle: string
    libelle: string
    valeur: number
    /** Rôle de statut, quand la barre EST un état. Sinon la teinte de série. */
    ton?: 'bon' | 'attention' | 'critique' | 'serieux' | 'neutre'
    /** Ce que la valeur appelle comme geste — jamais porté par la couleur seule. */
    marque?: string
}

const HAUTEUR_BARRE = 22
const ECART = 12
const ECART_SURFACE = 2

function couleur(ton?: Part['ton']): string {
    return ton ? `var(--viz-${ton})` : 'var(--viz-serie)'
}

/**
 * Tableau de valeurs, replié.
 *
 * IL N'EST PAS DÉCORATIF : c'est le recours exigé dès qu'une couleur passe sous
 * 3:1 sur la surface, et c'est aussi la seule lecture possible au lecteur
 * d'écran — un `<svg>` de rectangles ne se lit pas.
 */
function TableauValeurs({
    titre,
    entetes,
    lignes,
}: {
    titre: string
    entetes: string[]
    lignes: (string | number)[][]
}) {
    return (
        <details className="viz__tableau">
            <summary>Voir les valeurs</summary>
            <table className="tableau">
                <caption className="visuellement-cache">{titre}</caption>
                <thead>
                    <tr>
                        {entetes.map(e => (
                            <th scope="col" key={e}>
                                {e}
                            </th>
                        ))}
                    </tr>
                </thead>
                <tbody>
                    {lignes.map((l, i) => (
                        <tr key={i}>
                            {l.map((c, j) => (
                                <td key={j}>{c}</td>
                            ))}
                        </tr>
                    ))}
                </tbody>
            </table>
        </details>
    )
}

/**
 * BARRES HORIZONTALES — comparer des catégories nommées.
 *
 * Horizontales et non verticales : huit statuts en français ne tiennent pas en
 * étiquettes d'axe sous des colonnes sans pivoter le texte, et un libellé
 * pivoté ne se lit pas.
 *
 * UNE SEULE SÉRIE, DONC PAS DE LÉGENDE. Le titre dit ce qui est compté ; une
 * boîte à une pastille le répéterait. Les tons de statut, eux, portent une
 * MARQUE écrite à côté du libellé — la couleur ne dit jamais rien toute seule.
 */
export function BarresHorizontales({
    titre,
    parts,
    unite,
}: {
    titre: string
    parts: Part[]
    unite?: string
}) {
    const max = Math.max(...parts.map(p => p.valeur), 1)
    const largeurLibelle = 190
    const largeurValeur = 64
    const largeur = 640
    const largeurPiste = largeur - largeurLibelle - largeurValeur
    const hauteur = parts.length * (HAUTEUR_BARRE + ECART)

    return (
        <figure className="viz">
            <figcaption className="viz__titre">{titre}</figcaption>

            <svg
                className="viz__toile"
                viewBox={`0 0 ${largeur} ${hauteur}`}
                role="img"
                aria-label={`${titre}. Valeurs détaillées dans le tableau qui suit.`}
                preserveAspectRatio="xMinYMin meet"
            >
                {parts.map((p, i) => {
                    const y = i * (HAUTEUR_BARRE + ECART)
                    // Zéro ne dessine RIEN. Un rectangle de deux pixels pour une
                    // valeur nulle se lit comme « presque un » à distance.
                    const l = p.valeur === 0 ? 0 : Math.max(3, (p.valeur / max) * largeurPiste)

                    return (
                        <g key={p.cle}>
                            <title>
                                {p.libelle} : {p.valeur.toLocaleString('fr-FR')}
                                {unite ? ` ${unite}` : ''}
                            </title>
                            <text
                                className="viz__etiquette"
                                x={largeurLibelle - 10}
                                y={y + HAUTEUR_BARRE / 2}
                                textAnchor="end"
                                dominantBaseline="central"
                            >
                                {p.libelle}
                            </text>
                            {l > 0 && (
                                <rect
                                    x={largeurLibelle}
                                    y={y}
                                    width={l}
                                    height={HAUTEUR_BARRE}
                                    rx={4}
                                    fill={couleur(p.ton)}
                                />
                            )}
                            <text
                                className="viz__valeur"
                                x={largeurLibelle + l + 8}
                                y={y + HAUTEUR_BARRE / 2}
                                dominantBaseline="central"
                            >
                                {p.valeur.toLocaleString('fr-FR')}
                                {p.marque ? ` · ${p.marque}` : ''}
                            </text>
                        </g>
                    )
                })}
            </svg>

            <TableauValeurs
                titre={titre}
                entetes={['Statut', unite ?? 'Nombre']}
                lignes={parts.map(p => [
                    p.marque ? `${p.libelle} (${p.marque})` : p.libelle,
                    p.valeur.toLocaleString('fr-FR'),
                ])}
            />
        </figure>
    )
}

export type Colonne = {
    cle: string
    libelle: string
    valeur: number
    /** Valeur formatée pour l'affichage — la devise vient de la donnée. */
    texte: string
}

/**
 * COLONNES CHRONOLOGIQUES — une évolution dans le temps.
 *
 * L'ORDRE EST CELUI DU TEMPS, JAMAIS CELUI DES VALEURS. Trier par montant
 * décroissant ferait un classement déguisé en série temporelle : l'œil lirait
 * une baisse là où il n'y a qu'un tri.
 *
 * SEULS DEUX POINTS SONT ÉTIQUETÉS — le plus haut et le dernier. Une valeur sur
 * chaque colonne ne se lit pas, et c'est précisément ce qui fait qu'une
 * étiquette rare se lit.
 */
export function Colonnes({ titre, colonnes }: { titre: string; colonnes: Colonne[] }) {
    const max = Math.max(...colonnes.map(c => c.valeur), 1)
    const indexMax = colonnes.reduce((m, c, i) => (c.valeur > colonnes[m].valeur ? i : m), 0)
    const largeur = 640
    const hauteurTrace = 150
    const hauteurTexte = 42
    const pas = largeur / Math.max(colonnes.length, 1)
    const largeurBarre = Math.min(24, pas - ECART_SURFACE * 2)

    return (
        <figure className="viz">
            <figcaption className="viz__titre">{titre}</figcaption>

            <svg
                className="viz__toile"
                viewBox={`0 0 ${largeur} ${hauteurTrace + hauteurTexte}`}
                role="img"
                aria-label={`${titre}. Valeurs détaillées dans le tableau qui suit.`}
                preserveAspectRatio="xMinYMin meet"
            >
                {/* Ligne de base : une seule, en filet, jamais en pointillés. */}
                <line
                    x1={0}
                    x2={largeur}
                    y1={hauteurTrace}
                    y2={hauteurTrace}
                    className="viz__base"
                />

                {colonnes.map((c, i) => {
                    const h = c.valeur === 0 ? 0 : Math.max(3, (c.valeur / max) * (hauteurTrace - 24))
                    const x = i * pas + (pas - largeurBarre) / 2
                    const etiquette = i === indexMax || i === colonnes.length - 1

                    return (
                        <g key={c.cle}>
                            <title>
                                {c.libelle} : {c.texte}
                            </title>
                            {h > 0 && (
                                <rect
                                    x={x}
                                    y={hauteurTrace - h}
                                    width={largeurBarre}
                                    height={h}
                                    rx={4}
                                    fill="var(--viz-serie)"
                                />
                            )}
                            {etiquette && (
                                <text
                                    className="viz__valeur"
                                    x={x + largeurBarre / 2}
                                    y={hauteurTrace - h - 8}
                                    textAnchor="middle"
                                >
                                    {c.texte}
                                </text>
                            )}
                            <text
                                className="viz__etiquette"
                                x={x + largeurBarre / 2}
                                y={hauteurTrace + 16}
                                textAnchor="middle"
                            >
                                {c.libelle}
                            </text>
                        </g>
                    )
                })}
            </svg>

            <TableauValeurs
                titre={titre}
                entetes={['Période', 'Montant']}
                lignes={colonnes.map(c => [c.libelle, c.texte])}
            />
        </figure>
    )
}

/**
 * BARRE EMPILÉE — une composition, lue en une ligne.
 *
 * DEUX PIXELS DE SURFACE SÉPARENT LES SEGMENTS. Sans cet écart, deux teintes
 * voisines se touchent et la frontière disparaît ; avec, elle se voit même en
 * vision déficiente. C'est le blanc qui sépare, pas un contour — un contour
 * ajouterait de l'encre qui n'est pas de la donnée.
 *
 * LA LÉGENDE EST TOUJOURS LÀ. Quatre segments et plus : l'identité ne peut pas
 * reposer sur la seule couleur, et une étiquette à l'intérieur d'un segment
 * étroit serait tronquée.
 */
export function BarreEmpilee({
    titre,
    parts,
    note,
}: {
    titre: string
    parts: Part[]
    note?: ReactNode
}) {
    const total = parts.reduce((s, p) => s + p.valeur, 0)
    const largeur = 640
    const hauteur = 28

    /*
     * LES DÉCALAGES SONT CALCULÉS AVANT LE RENDU, PAS PENDANT.
     *
     * La première version portait un `let curseur` incrémenté dans le `.map()`
     * du JSX. Cela marche au premier rendu et devient faux au second : React
     * peut réexécuter le corps d'un composant sans le remonter, et l'accumulateur
     * repart alors d'une valeur déjà avancée. Le compilateur React le refuse
     * — « reassigning after render has completed can cause inconsistent
     * behavior ». Une segmentation qui glisse d'un rendu à l'autre est
     * exactement le genre de défaut qu'on n'attribue jamais au bon endroit.
     */
    const segments = parts.reduce<{ part: Part; x: number; largeur: number }[]>((acc, p) => {
        const precedent = acc.at(-1)
        const x = precedent ? precedent.x + precedent.largeur : 0
        return [...acc, { part: p, x, largeur: total === 0 ? 0 : (p.valeur / total) * largeur }]
    }, [])

    return (
        <figure className="viz">
            <figcaption className="viz__titre">{titre}</figcaption>

            {total === 0 ? (
                <p className="indice">Aucun paiement enregistré.</p>
            ) : (
                <svg
                    className="viz__toile"
                    viewBox={`0 0 ${largeur} ${hauteur}`}
                    role="img"
                    aria-label={`${titre}. Valeurs détaillées dans le tableau qui suit.`}
                    preserveAspectRatio="none"
                    style={{ height: hauteur }}
                >
                    {segments.map(({ part, x, largeur: l }) =>
                        l <= 0 ? null : (
                            <g key={part.cle}>
                                <title>
                                    {part.libelle} : {part.valeur.toLocaleString('fr-FR')}
                                </title>
                                <rect
                                    x={x}
                                    y={0}
                                    width={Math.max(l - ECART_SURFACE, 1)}
                                    height={hauteur}
                                    rx={4}
                                    fill={couleur(part.ton)}
                                />
                            </g>
                        ),
                    )}
                </svg>
            )}

            <ul className="viz__legende">
                {parts.map(p => (
                    <li key={p.cle}>
                        <span
                            className="viz__pastille"
                            style={{ background: couleur(p.ton) }}
                            aria-hidden="true"
                        />
                        <span>{p.libelle}</span>
                        <strong>{p.valeur.toLocaleString('fr-FR')}</strong>
                    </li>
                ))}
            </ul>

            {note && <p className="indice">{note}</p>}

            <TableauValeurs
                titre={titre}
                entetes={['État', 'Nombre']}
                lignes={parts.map(p => [p.libelle, p.valeur.toLocaleString('fr-FR')])}
            />
        </figure>
    )
}
