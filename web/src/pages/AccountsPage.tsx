import { useEffect, useState, type FormEvent } from 'react'
import { FormField } from '../components/FormField'
import { SelectField } from '../components/SelectField'
import {
  ApiError,
  createAccount,
  currencies,
  importStatement,
  listAccounts,
  listIntegrations,
  listSourceFiles,
  renameAccount,
  sourceFileContentUrl,
  type Account,
  type Currency,
  type ImportResult,
  type Integration,
  type SourceFile,
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
                  onImported={() => void reloadAccounts()}
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
  onImported,
}: {
  account: Account
  integrationName: string
  onRenamed: (a: Account) => void
  onImported: () => void
}) {
  const [editing, setEditing] = useState(false)
  const [importing, setImporting] = useState(false)
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
            </button>{' '}
            <button type="button" className="button-link" onClick={() => setImporting(v => !v)}>
              {importing ? 'Close import' : 'Import statement'}
            </button>
          </>
        )}
        <div className="account-meta">
          {account.owner.name} · {integrationName}
          {account.identifier && <> · {account.identifier}</>} · Ingest folder ingest/{account.slug}/
        </div>
        {importing && <ImportPanel account={account} integrationName={integrationName} onImported={onImported} />}
      </div>
      <div className="amount account-balance">
        {formatAmount(account.balance)} {account.currency}
        <div className="account-meta">opening {formatDate(account.openingDate)}</div>
      </div>
    </li>
  )
}

/** Upload a statement into the Account (through its Integration) and list the files imported so far. */
function ImportPanel({
  account,
  integrationName,
  onImported,
}: {
  account: Account
  integrationName: string
  onImported: () => void
}) {
  const [file, setFile] = useState<File | null>(null)
  const [inputKey, setInputKey] = useState(0)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [result, setResult] = useState<ImportResult | null>(null)
  const [sourceFiles, setSourceFiles] = useState<SourceFile[] | null>(null)

  const [reloads, setReloads] = useState(0)

  useEffect(() => {
    const controller = new AbortController()
    listSourceFiles(account.id, controller.signal)
      .then(setSourceFiles)
      .catch(() => {
        if (!controller.signal.aborted) setSourceFiles([])
      })
    return () => controller.abort()
  }, [account.id, reloads])

  async function upload(e: FormEvent) {
    e.preventDefault()
    if (!file) {
      setError('Choose a statement file.')
      return
    }
    setBusy(true)
    setError(null)
    setResult(null)
    try {
      setResult(await importStatement(account.id, file))
      setFile(null)
      setInputKey(k => k + 1)
      onImported()
      setReloads(n => n + 1)
    } catch (err) {
      setError(
        err instanceof ApiError && err.status === 400
          ? (err.fieldErrors.file?.[0] ?? 'The file couldn’t be imported.')
          : 'Something went wrong, try again.',
      )
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="import-panel">
      <form className="import-form" onSubmit={upload}>
        <label className="field">
          <span>Statement file ({integrationName})</span>
          <input key={inputKey} type="file" name="file" onChange={e => setFile(e.target.files?.[0] ?? null)} />
        </label>
        <button type="submit" className="button-primary" disabled={busy || !file}>
          {busy ? 'Importing…' : 'Import'}
        </button>
      </form>
      {error && (
        <p className="form-error" role="alert">
          Not imported: {error} The file was moved to ingest/{account.slug}/failed/.
        </p>
      )}
      {result && (
        <p className="status status-good" role="status">
          Imported {result.importedCount} Transaction{result.importedCount === 1 ? '' : 's'} from{' '}
          {result.sourceFile.fileName}
          {result.skippedCount > 0 && <>, skipped {result.skippedCount} already imported</>}.
        </p>
      )}
      {result?.warnings.map(w => (
        <p key={w} className="status status-bad">
          {w}
        </p>
      ))}
      {sourceFiles && sourceFiles.length > 0 && (
        <ul className="source-file-list">
          {sourceFiles.map(f => (
            <li key={f.id}>
              <a href={sourceFileContentUrl(f.id)}>{f.fileName}</a>{' '}
              <span className="account-meta">
                {formatDate(f.importedAt.slice(0, 10))} · {f.transactionCount} Transaction
                {f.transactionCount === 1 ? '' : 's'}
              </span>
            </li>
          ))}
        </ul>
      )}
    </div>
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
