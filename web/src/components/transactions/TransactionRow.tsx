import { useState } from 'react'
import { Link } from 'react-router'
import {
  deleteTransaction,
  sourceFileContentUrl,
  tagReportPath,
  unlinkTransaction,
  type Category,
  type Owner,
  type Tag,
  type TagRef,
  type Transaction,
  type TransactionLink,
} from '../../lib/api'
import { formatAmount, formatDate } from '../../lib/format'
import { RuleOffer } from '../RuleOffer'
import { EditTransactionForm } from './EditTransactionForm'
import { LinkForm } from './LinkForm'
import { HIDDEN_ACCOUNT, toTransactionErrors } from './shared'

/** One Transaction in the list: details, Tags, actions (edit, delete, link/unlink) and the selection checkbox. */
export function TransactionRow({
  transaction: t,
  all,
  owners,
  categories,
  tags,
  canEditFacts,
  selected,
  onSelect,
  onChanged,
}: {
  transaction: Transaction
  all: Transaction[]
  owners: Owner[]
  categories: Category[]
  tags: Tag[]
  canEditFacts: boolean
  selected: boolean
  onSelect: (selected: boolean) => void
  onChanged: () => void
}) {
  const [editing, setEditing] = useState(false)
  const [linking, setLinking] = useState(false)
  const [offerRule, setOfferRule] = useState(false)
  const [error, setError] = useState<string | null>(null)

  async function unlink() {
    setError(null)
    try {
      await unlinkTransaction(t.id)
      onChanged()
    } catch {
      setError('Couldn’t unlink it.')
    }
  }

  async function remove() {
    if (!window.confirm(`Delete “${t.description}”?`)) return
    setError(null)
    try {
      await deleteTransaction(t.id)
      onChanged()
    } catch (err) {
      setError(toTransactionErrors(err).form?.[0] ?? 'Couldn’t delete it.')
    }
  }

  if (editing)
    return (
      <li className="transaction-row">
        <EditTransactionForm
          transaction={t}
          owners={owners}
          categories={categories}
          tags={tags}
          canEditFacts={canEditFacts}
          onCancel={() => setEditing(false)}
          onSaved={categorySet => {
            setOfferRule(categorySet)
            setEditing(false)
            onChanged()
          }}
        />
      </li>
    )

  return (
    <li className={`transaction-row${selected ? ' selected' : ''}`}>
      <input
        type="checkbox"
        className="transaction-select"
        checked={selected}
        aria-label={`Select ${t.displayName ?? t.description}`}
        onChange={e => onSelect(e.target.checked)}
      />
      <div className="transaction-date">{formatDate(t.date)}</div>
      <div className="transaction-main">
        <strong>{t.displayName ?? t.description}</strong>
        {(t.displayName || t.counterparty) && (
          <div className="account-meta transaction-original">
            {t.displayName && <>{t.description}</>}
            {t.displayName && t.counterparty && ' · '}
            {t.counterparty}
          </div>
        )}
        <div className="account-meta">
          {t.type === 'transfer' ? 'Transfer' : (t.category?.path ?? 'Uncategorised')} ·{' '}
          {t.accountName ?? HIDDEN_ACCOUNT} · {t.owner.name}
          {t.link && (
            <>
              {' · '}
              <LinkLabel link={t.link} />
            </>
          )}
          {t.note && <> · {t.note}</>}
          {t.sourceFile && (
            <>
              {' · '}
              <a href={sourceFileContentUrl(t.sourceFile.id)} title="Download the Source file">
                {t.sourceFile.fileName}
              </a>
            </>
          )}
        </div>
        <TagChips tags={t.tags} />
        {offerRule && <RuleOffer transactionId={t.id} onDismiss={() => setOfferRule(false)} />}
        <div className="transaction-actions">
          <button type="button" className="button-link" onClick={() => setEditing(true)}>
            Edit
          </button>
          {canEditFacts && (
            <button type="button" className="button-link" onClick={() => void remove()}>
              Delete
            </button>
          )}
          {t.link ? (
            <button type="button" className="button-link" onClick={() => void unlink()}>
              Unlink
            </button>
          ) : (
            !linking && (
              <button type="button" className="button-link" onClick={() => setLinking(true)}>
                Link…
              </button>
            )
          )}
        </div>
        {linking && !t.link && (
          <LinkForm
            transaction={t}
            all={all}
            onCancel={() => setLinking(false)}
            onLinked={() => {
              setLinking(false)
              onChanged()
            }}
          />
        )}
        {error && (
          <p className="form-error" role="alert">
            {error}
          </p>
        )}
      </div>
      <div className={`amount transaction-amount ${t.type}`}>
        {formatAmount(t.amount)} {t.currency}
        <OriginalAmount transaction={t} />
        {t.currency !== 'CHF' && (
          <div className="transaction-chf" title="CHF equivalent at the rate of the Transaction's date">
            {t.chfAmount === null ? 'CHF rate pending' : `≈ ${formatAmount(t.chfAmount)} CHF`}
          </div>
        )}
      </div>
    </li>
  )
}

/** The Tags on a Transaction; automatically applied ones (date range, Rule) are drawn dashed. Each opens its Tag report. */
function TagChips({ tags }: { tags: TagRef[] }) {
  if (tags.length === 0) return null
  return (
    <div className="tag-list">
      {tags.map(tag => {
        const auto = !tag.sources.includes('manual')
        return (
          <Link
            key={tag.id}
            to={tagReportPath(tag.id)}
            className={`tag-chip${auto ? ' auto' : ''}`}
            title={`${auto ? 'Applied automatically' : 'Added by hand'} · open the Tag report`}
          >
            {tag.name}
          </Link>
        )
      })}
    </div>
  )
}

/** "Transfer with Savings"; the other leg may sit in an Account the viewer can't see. */
function LinkLabel({ link }: { link: TransactionLink }) {
  return (
    <span className="transaction-link">
      {link.kind === 'transfer' ? 'Transfer' : 'Contribution'} with {link.accountName ?? HIDDEN_ACCOUNT}
    </span>
  )
}

/** A foreign-currency purchase: what was paid, next to what the Account was charged. */
function OriginalAmount({ transaction: t }: { transaction: Transaction }) {
  if (t.originalAmount === null || t.originalCurrency === null) return null
  return (
    <div className="account-meta transaction-original-amount">
      {formatAmount(t.originalAmount)} {t.originalCurrency}
    </div>
  )
}
