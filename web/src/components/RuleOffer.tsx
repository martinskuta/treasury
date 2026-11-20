import { Link } from 'react-router'

/**
 * Shown right after a Member corrected a Transaction's Category by hand: offers a Rule pre-filled from that
 * Transaction (Settings › Rules opens with the draft from GET /api/rules/suggestion).
 */
export function RuleOffer({ transactionId, onDismiss }: { transactionId: string; onDismiss: () => void }) {
  return (
    <p className="status status-good rule-offer" role="status">
      Categorise Transactions like this automatically?{' '}
      <Link to={`/settings/rules?fromTransaction=${encodeURIComponent(transactionId)}`}>Create a Rule…</Link>{' '}
      <button type="button" className="button-link" onClick={onDismiss}>
        No thanks
      </button>
    </p>
  )
}
