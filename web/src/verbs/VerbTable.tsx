import type { VerbRow } from '../api/client'
import { masteryBand, masteryPercent } from './mastery'
import './VerbTable.css'

/// Every verb of a set with its three forms, its translation and how well it is known.
/// The bar's length is the level and its colour the band; the check mark is what explains
/// why a verb left its training window.
export function VerbTable({ verbs }: { verbs: VerbRow[] }) {
  return (
    <table className="verb-table">
      <thead>
        <tr>
          <th>Base</th>
          <th>Past</th>
          <th>Participle</th>
          <th>Translation</th>
          <th>Known</th>
        </tr>
      </thead>
      <tbody>
        {verbs.map((verb) => (
          <tr key={verb.v1}>
            <td className="verb-base">
              {verb.passed && (
                <span className="verb-passed" aria-label="Passed">
                  ✓
                </span>
              )}
              {verb.v1}
            </td>
            <td>{verb.v2}</td>
            <td>{verb.v3}</td>
            <td className="verb-translation">{verb.translation}</td>
            <td className="verb-mastery">
              <span className="mastery">
                <span className="mastery-bar">
                  <span
                    className="mastery-fill"
                    data-band={masteryBand(verb.mastery, verb.answers)}
                    style={{ width: `${verb.answers === 0 ? 0 : masteryPercent(verb.mastery)}%` }}
                  />
                </span>
                <span className="num mastery-level">
                  {verb.answers === 0 ? '—' : `${masteryPercent(verb.mastery)}%`}
                </span>
              </span>
            </td>
          </tr>
        ))}
      </tbody>
    </table>
  )
}
