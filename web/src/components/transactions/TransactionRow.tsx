import { Fragment, useState, type ReactNode } from 'react'
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
import { formatDate } from '../../lib/format'
import { RuleOffer } from '../RuleOffer'
import { EditTransactionForm } from './EditTransactionForm'
import { LinkForm } from './LinkForm'
import { KindIcon, RowAmount } from './RowParts'
import { HIDDEN_ACCOUNT, kindClass, toTransactionErrors } from './shared'

/**
 * One Transaction in the list: kind icon, Display name with its Tags, a muted second line (original description,
 * counterparty, Category, Account · Owner, link, note, Source file), the amount, actions (edit, delete, link/unlink)
 * and the selection checkbox. `showDate` puts the date on the second line when the list isn't grouped by day.
 */
export function TransactionRow({
  transaction: t,
  all,
  owners,
  categories,
  tags,
  canEditFacts,
  selected,
  showDate,
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
  showDate: boolean
  onSelect: (selected: boolean) => void
  onChanged: () => void
}) {
  const [editing, setEditing] = useState(false)
  const [linking, setLinking] = useState(false)
  const [offerRule, setOfferRule] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const name = t.displayName ?? t.description

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
      <li className={`tx tx-editing ${kindClass(t)}`}>
        <span className="tx-select" aria-hidden="true" />
        <KindIcon transaction={t} />
        <div className="tx-main">
          <div className="tx-name">Edit {name}</div>
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
        </div>
      </li>
    )

  const details: ReactNode[] = [
    showDate && formatDate(t.date),
    t.displayName && t.description,
    t.counterparty,
    // A linked Transfer says so in its link ("Transfer with Savings") below.
    t.type === 'transfer'
      ? !t.link && 'Transfer'
      : (t.category?.path ?? <span className="tx-uncat">Uncategorised</span>),
    `${t.accountName ?? HIDDEN_ACCOUNT} · ${t.owner.name}`,
    t.link && <LinkLabel link={t.link} />,
    t.note,
    t.sourceFile && (
      <a href={sourceFileContentUrl(t.sourceFile.id)} title="Download the Source file">
        {t.sourceFile.fileName}
      </a>
    ),
  ].filter(Boolean)

  return (
    <li className={`tx ${kindClass(t)}${selected ? ' selected' : ''}`}>
      <input
        type="checkbox"
        className="tx-select"
        checked={selected}
        aria-label={`Select ${name}`}
        onChange={e => onSelect(e.target.checked)}
      />
      <KindIcon transaction={t} />
      <div className="tx-main">
        <div className="tx-name">
          <span className="tx-name-text">{name}</span>
          <TagChips tags={t.tags} />
        </div>
        <div className="tx-sub">
          {details.map((d, i) => (
            <Fragment key={i}>
              {i > 0 && ' · '}
              {d}
            </Fragment>
          ))}
        </div>
      </div>
      <div className="tx-actions">
        <button type="button" className="button-link" onClick={() => setEditing(true)}>
          Edit
        </button>
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
        {canEditFacts && (
          <button type="button" className="button-link tx-delete" onClick={() => void remove()}>
            Delete
          </button>
        )}
      </div>
      <RowAmount transaction={t} />
      {(offerRule || (linking && !t.link) || error) && (
        <div className="tx-extra">
          {offerRule && <RuleOffer transactionId={t.id} onDismiss={() => setOfferRule(false)} />}
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
      )}
    </li>
  )
}

/** The Tags on a Transaction; automatically applied ones (date range, Rule) are drawn outlined. Each opens its Tag report. */
function TagChips({ tags }: { tags: TagRef[] }) {
  if (tags.length === 0) return null
  return (
    <span className="tx-tags">
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
    </span>
  )
}

/** "Transfer with Savings"; the other leg may sit in an Account the viewer can't see. */
function LinkLabel({ link }: { link: TransactionLink }) {
  return (
    <span className="tx-link">
      {link.kind === 'transfer' ? 'Transfer' : 'Contribution'} with {link.accountName ?? HIDDEN_ACCOUNT}
    </span>
  )
}
