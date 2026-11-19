import { useEffect, useState, type FormEvent } from 'react'
import { FormField } from '../components/FormField'
import { SelectField } from '../components/SelectField'
import {
  ApiError,
  createAccount,
  currencies,
  listAccounts,
  listIntegrations,
  renameAccount,
  type Account,
  type Currency,
  type Integration,
} from '../lib/api'
import { useMe } from '../lib/auth'
import { formatAmount, formatDate } from '../lib/format'

type Errors = Record<string, string[]>

const toErrors = (err: unknown): Errors =>
  err instanceof ApiError && err.status === 400 ? err.fieldErrors : { form: ['Something went wrong, try again.'] }

/** The Accounts the Member can see (their own and the Family's) with balances; create and rename. */
export function AccountsPage() {
  const [accounts, setAccounts] = useState<Account[] | null>(null)
  const [integrations, setIntegrations] = useState<Integration[]>([])
  const [loadError, setLoadError] = useState(false)
  const [creating, setCreating] = useState(false)

  useEffect(() => {
    const controller = new AbortController()
    Promise.all([listAccounts(controller.signal), listIntegrations(controller.signal)])
      .then(([a, i]) => {
        setAccounts(a)
        setIntegrations(i)
      })
      .catch(() => {
        if (!controller.signal.aborted) setLoadError(true)
      })
    return () => controller.abort()
  }, [])

  const reloadAccounts = () =>
    listAccounts()
      .then(setAccounts)
      .catch(() => setLoadError(true))

  const integrationName = (key: string) => integrations.find(i => i.key === key)?.name ?? key

  return (
    <>
      <h1>Accounts</h1>
      {loadError && <p className="status status-bad">Couldn’t load the Accounts.</p>}
      {accounts === null && !loadError && <p>Loading…</p>}
      {accounts !== null && (
        <section className="card">
          {accounts.length === 0 ? (
            <p>No Accounts yet.</p>
          ) : (
            <ul className="account-list">
              {accounts.map(a => (
                <AccountRow
                  key={a.id}
                  account={a}
                  integrationName={integrationName(a.integrationKey)}
                  onRenamed={renamed => setAccounts(list => list?.map(x => (x.id === renamed.id ? renamed : x)) ?? null)}
                />
              ))}
            </ul>
          )}
        </section>
      )}
      {creating ? (
        <NewAccountForm
          integrations={integrations}
          onCancel={() => setCreating(false)}
          onCreated={() => {
            setCreating(false)
            void reloadAccounts()
          }}
        />
      ) : (
        <button type="button" className="button-primary" onClick={() => setCreating(true)}>
          New Account
        </button>
      )}
    </>
  )
}

function AccountRow({
  account,
  integrationName,
  onRenamed,
}: {
  account: Account
  integrationName: string
  onRenamed: (a: Account) => void
}) {
  const [editing, setEditing] = useState(false)
  const [name, setName] = useState(account.name)
  const [errors, setErrors] = useState<Errors>({})
  const [busy, setBusy] = useState(false)

  async function save(e: FormEvent) {
    e.preventDefault()
    setBusy(true)
    setErrors({})
    try {
      onRenamed(await renameAccount(account.id, name))
      setEditing(false)
    } catch (err) {
      setErrors(toErrors(err))
    } finally {
      setBusy(false)
    }
  }

  return (
    <li className="account-row">
      <div className="account-main">
        {editing ? (
          <form className="account-rename" onSubmit={save}>
            <FormField
              label="Name"
              name="name"
              required
              autoFocus
              value={name}
              errors={errors.name ?? errors.form}
              onChange={e => setName(e.target.value)}
            />
            <button type="submit" className="button-primary" disabled={busy}>
              Save
            </button>
            <button
              type="button"
              className="button-secondary"
              onClick={() => {
                setEditing(false)
                setName(account.name)
                setErrors({})
              }}
            >
              Cancel
            </button>
          </form>
        ) : (
          <>
            <strong>{account.name}</strong>{' '}
            <button type="button" className="button-link" onClick={() => setEditing(true)}>
              Rename
            </button>
          </>
        )}
        <div className="account-meta">
          {account.owner.name} · {integrationName}
          {account.identifier && <> · {account.identifier}</>} · Ingest folder ingest/{account.slug}/
        </div>
      </div>
      <div className="amount account-balance">
        {formatAmount(account.balance)} {account.currency}
        <div className="account-meta">opening {formatDate(account.openingDate)}</div>
      </div>
    </li>
  )
}

function NewAccountForm({
  integrations,
  onCancel,
  onCreated,
}: {
  integrations: Integration[]
  onCancel: () => void
  onCreated: () => void
}) {
  const me = useMe()
  const [name, setName] = useState('')
  const [owner, setOwner] = useState<'me' | 'family'>('me')
  const [integrationKey, setIntegrationKey] = useState(integrations[0]?.key ?? '')
  const [currency, setCurrency] = useState<Currency>('CHF')
  const [openingBalance, setOpeningBalance] = useState('0.00')
  const [openingDate, setOpeningDate] = useState('')
  const [identifier, setIdentifier] = useState('')
  const [errors, setErrors] = useState<Errors>({})
  const [busy, setBusy] = useState(false)

  const integration = integrations.find(i => i.key === integrationKey)
  const offerIdentifier = integration?.canProvideIdentifier ?? false

  async function submit(e: FormEvent) {
    e.preventDefault()
    const balance = openingBalance.trim() === '' ? null : Number(openingBalance.replace(/'/g, ''))
    if (balance !== null && Number.isNaN(balance)) {
      setErrors({ openingBalance: ['Enter a number, e.g. 1234.50'] })
      return
    }
    setBusy(true)
    setErrors({})
    try {
      await createAccount({
        name,
        owner: owner === 'family' ? { kind: 'family' } : { kind: 'member', memberId: me.id },
        integrationKey,
        currency,
        openingBalance: balance,
        openingDate: openingDate || null,
        identifier: offerIdentifier && identifier.trim() ? identifier.trim() : null,
      })
      onCreated()
    } catch (err) {
      setErrors(toErrors(err))
    } finally {
      setBusy(false)
    }
  }

  return (
    <form className="card form" onSubmit={submit}>
      <h2>New Account</h2>
      {integrations.length === 0 && (
        <p className="status status-bad">No Integrations are installed yet, so no Account can be created.</p>
      )}
      <FormField label="Name" name="name" required value={name} errors={errors.name} onChange={e => setName(e.target.value)} />
      <SelectField
        label="Owner"
        name="owner"
        value={owner}
        errors={errors.owner}
        onChange={e => setOwner(e.target.value as 'me' | 'family')}
      >
        <option value="me">Me ({me.userName})</option>
        <option value="family">Family</option>
      </SelectField>
      <SelectField
        label="Integration"
        name="integrationKey"
        required
        value={integrationKey}
        errors={errors.integrationKey}
        onChange={e => setIntegrationKey(e.target.value)}
      >
        {integrations.map(i => (
          <option key={i.key} value={i.key}>
            {i.name}
          </option>
        ))}
      </SelectField>
      <SelectField
        label="Currency"
        name="currency"
        value={currency}
        errors={errors.currency}
        onChange={e => setCurrency(e.target.value as Currency)}
      >
        {currencies.map(c => (
          <option key={c} value={c}>
            {c}
          </option>
        ))}
      </SelectField>
      <FormField
        label="Opening balance"
        name="openingBalance"
        inputMode="decimal"
        required
        value={openingBalance}
        errors={errors.openingBalance}
        onChange={e => setOpeningBalance(e.target.value)}
      />
      <FormField
        label="Opening balance date"
        name="openingDate"
        type="date"
        required
        value={openingDate}
        errors={errors.openingDate}
        onChange={e => setOpeningDate(e.target.value)}
      />
      {offerIdentifier && (
        <FormField
          label="Identifier (optional: IBAN or last card digits)"
          name="identifier"
          value={identifier}
          errors={errors.identifier}
          onChange={e => setIdentifier(e.target.value)}
        />
      )}
      {errors.form && (
        <p className="form-error" role="alert">
          {errors.form[0]}
        </p>
      )}
      <div className="form-actions">
        <button type="submit" className="button-primary" disabled={busy || integrations.length === 0}>
          Create Account
        </button>
        <button type="button" className="button-secondary" onClick={onCancel}>
          Cancel
        </button>
      </div>
    </form>
  )
}
