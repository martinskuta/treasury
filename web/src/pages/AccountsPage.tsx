import { useEffect, useState, type FormEvent } from 'react'
import { Balance } from '../components/Balance'
import { FormField } from '../components/FormField'
import { IdentifiersField } from '../components/IdentifiersField'
import { SelectField } from '../components/SelectField'
import {
  ApiError,
  createAccount,
  createMultiCurrencyAccount,
  deleteSourceFile,
  importStatement,
  listAccounts,
  listCurrencies,
  listIntegrations,
  listMultiCurrencyAccounts,
  listSourceFiles,
  sourceFileContentUrl,
  updateAccount,
  updateMultiCurrencyAccount,
  type Account,
  type Currency,
  type ImportResult,
  type ImportTarget,
  type Integration,
  type MultiCurrencyAccount,
  type OwnerRef,
  type SourceFile,
} from '../lib/api'
import { toErrors, type Errors } from '../lib/errors'
import { useMe } from '../lib/auth'
import { formatAmount, formatDate } from '../lib/format'
import '../styles/accounts.css'

/** Rounded square before an Account: brown for a Multi-currency account, teal for the Family's, neutral otherwise. */
function AccountIcon({ label, tone }: { label: string; tone: 'multi' | 'family' | 'member' | 'currency' }) {
  return (
    <span className={`acct-ico acct-ico-${tone}`} aria-hidden="true">
      {label}
    </span>
  )
}

/** First letter of a name for the icon. */
const initial = (name: string) => name.trim().charAt(0).toUpperCase() || '?'

/**
 * The Accounts the Member can see (their own and the Family's) with balances; create and rename. The Accounts of a
 * Multi-currency account are shown nested under it, with its total in the main currency.
 */
export function AccountsPage() {
  const [accounts, setAccounts] = useState<Account[] | null>(null)
  const [multiCurrencyAccounts, setMultiCurrencyAccounts] = useState<MultiCurrencyAccount[]>([])
  const [integrations, setIntegrations] = useState<Integration[]>([])
  const [currencies, setCurrencies] = useState<Currency[]>([])
  const [loadError, setLoadError] = useState(false)
  const [creating, setCreating] = useState(false)

  useEffect(() => {
    const controller = new AbortController()
    Promise.all([
      listAccounts(controller.signal),
      listMultiCurrencyAccounts(controller.signal),
      listIntegrations(controller.signal),
      listCurrencies(controller.signal),
    ])
      .then(([a, m, i, c]) => {
        setAccounts(a)
        setMultiCurrencyAccounts(m)
        setIntegrations(i)
        setCurrencies(c)
      })
      .catch(() => {
        if (!controller.signal.aborted) setLoadError(true)
      })
    return () => controller.abort()
  }, [])

  const reloadAccounts = () =>
    Promise.all([listAccounts(), listMultiCurrencyAccounts()])
      .then(([a, m]) => {
        setAccounts(a)
        setMultiCurrencyAccounts(m)
      })
      .catch(() => setLoadError(true))

  const integrationName = (key: string) => integrations.find(i => i.key === key)?.name ?? key
  const canProvideIdentifier = (key: string) => integrations.find(i => i.key === key)?.canProvideIdentifier ?? false
  const plainAccounts = accounts?.filter(a => a.multiCurrencyAccountId === null) ?? []
  // Accounts and Multi-currency accounts in one list, by name.
  const rows = [
    ...plainAccounts.map(a => ({ name: a.name, account: a, multiCurrencyAccount: null })),
    ...multiCurrencyAccounts.map(m => ({ name: m.name, account: null, multiCurrencyAccount: m })),
  ].sort((x, y) => x.name.localeCompare(y.name))

  return (
    <>
      <div className="page-header">
        <h1 className="page-title">Accounts</h1>
        {!creating && (
          <button type="button" className="button-primary" onClick={() => setCreating(true)}>
            New Account
          </button>
        )}
      </div>
      {creating && (
        <NewAccountForm
          integrations={integrations}
          currencies={currencies}
          onCancel={() => setCreating(false)}
          onCreated={() => {
            setCreating(false)
            void reloadAccounts()
          }}
        />
      )}
      {loadError && <p className="status status-bad">Couldn’t load the Accounts.</p>}
      {accounts === null && !loadError && <p className="page-loading">Loading…</p>}
      {accounts !== null && (
        <section className="ui-component accounts-panel">
          {rows.length === 0 ? (
            <p className="empty">No Accounts yet.</p>
          ) : (
            <ul className="acct-list">
              {rows.map(({ account, multiCurrencyAccount }) =>
                multiCurrencyAccount ? (
                  <MultiCurrencyAccountRow
                    key={multiCurrencyAccount.id}
                    multiCurrencyAccount={multiCurrencyAccount}
                    integrationName={integrationName(multiCurrencyAccount.integrationKey)}
                    canHaveIdentifier={canProvideIdentifier(multiCurrencyAccount.integrationKey)}
                    onChanged={() => void reloadAccounts()}
                  />
                ) : (
                  <AccountRow
                    key={account.id}
                    account={account}
                    integrationName={integrationName(account.integrationKey)}
                    canHaveIdentifiers={canProvideIdentifier(account.integrationKey)}
                    onRenamed={renamed =>
                      setAccounts(list => list?.map(x => (x.id === renamed.id ? renamed : x)) ?? null)
                    }
                    onImported={() => void reloadAccounts()}
                  />
                ),
              )}
            </ul>
          )}
        </section>
      )}
    </>
  )
}

/**
 * Inline form editing a name and, with `hasIdentifiersField`, the Identifiers; calls onSave with the new name and
 * the Identifiers as typed, and closes when it resolves.
 */
function NameAndIdentifiersForm({
  initialName,
  hasIdentifiersField = false,
  initialIdentifiers = [],
  onSave,
  onClose,
}: {
  initialName: string
  hasIdentifiersField?: boolean
  initialIdentifiers?: string[]
  onSave: (name: string, identifiers: string[]) => Promise<void>
  onClose: () => void
}) {
  const [name, setName] = useState(initialName)
  const [identifiers, setIdentifiers] = useState(initialIdentifiers)
  const [errors, setErrors] = useState<Errors>({})
  const [busy, setBusy] = useState(false)

  async function save(e: FormEvent) {
    e.preventDefault()
    setBusy(true)
    setErrors({})
    try {
      await onSave(name, identifiers)
      onClose()
    } catch (err) {
      setErrors(toErrors(err))
    } finally {
      setBusy(false)
    }
  }

  return (
    <form className="acct-rename" onSubmit={save}>
      <FormField
        label="Name"
        name="name"
        required
        autoFocus
        value={name}
        errors={errors.name ?? errors.form}
        onChange={e => setName(e.target.value)}
      />
      {hasIdentifiersField && (
        <IdentifiersField value={identifiers} errors={errors.identifiers} onChange={setIdentifiers} />
      )}
      <div className="form-actions">
        <button type="submit" className="button-primary button-small" disabled={busy}>
          Save
        </button>
        <button type="button" className="button-secondary button-small" onClick={onClose}>
          Cancel
        </button>
      </div>
    </form>
  )
}

/**
 * A Multi-currency account: its Ingest folder, Identifiers and total, with its Accounts nested (renamable, never
 * moved). Its Identifiers can be edited when its Integration can provide one.
 */
function MultiCurrencyAccountRow({
  multiCurrencyAccount,
  integrationName,
  canHaveIdentifier,
  onChanged,
}: {
  multiCurrencyAccount: MultiCurrencyAccount
  integrationName: string
  canHaveIdentifier: boolean
  onChanged: () => void
}) {
  const [editing, setEditing] = useState(false)
  const [importing, setImporting] = useState(false)

  return (
    <li className="acct-item acct-multi">
      <div className="acct-line">
        <AccountIcon label={initial(multiCurrencyAccount.name)} tone="multi" />
        <div className="acct-name">
          {editing ? (
            <NameAndIdentifiersForm
              initialName={multiCurrencyAccount.name}
              hasIdentifiersField={canHaveIdentifier}
              initialIdentifiers={multiCurrencyAccount.identifiers}
              onSave={async (name, identifiers) => {
                await updateMultiCurrencyAccount(
                  multiCurrencyAccount.id,
                  canHaveIdentifier ? { name, identifiers } : { name },
                )
                onChanged()
              }}
              onClose={() => setEditing(false)}
            />
          ) : (
            <strong>{multiCurrencyAccount.name}</strong>
          )}
          <small>
            {multiCurrencyAccount.owner.name} · {integrationName} · Multi-currency account
            {multiCurrencyAccount.identifiers.length > 0 && <> · {multiCurrencyAccount.identifiers.join(', ')}</>}
          </small>
        </div>
        <div className="acct-bal">
          <Balance
            value={multiCurrencyAccount.totalBalance}
            currency={multiCurrencyAccount.mainCurrency}
            className="acct-amount"
          />
          <small>
            {multiCurrencyAccount.totalBalance === null ? 'total: exchange rate missing' : 'total at today’s rates'}
          </small>
        </div>
      </div>
      <div className="acct-actions">
        {!editing && (
          <>
            <button type="button" className="button-link" onClick={() => setEditing(true)}>
              {canHaveIdentifier ? 'Edit' : 'Rename'}
            </button>
            <button type="button" className="button-link" aria-expanded={importing} onClick={() => setImporting(v => !v)}>
              {importing ? 'Close imports' : 'Imports'}
            </button>
          </>
        )}
        <span className="acct-folder">Ingest folder ingest/{multiCurrencyAccount.slug}/</span>
      </div>
      {importing && (
        <ImportPanel
          target={{ kind: 'multiCurrencyAccount', id: multiCurrencyAccount.id }}
          slug={multiCurrencyAccount.slug}
          integrationName={integrationName}
          onImported={onChanged}
        />
      )}
      <ul className="acct-sub">
        {multiCurrencyAccount.accounts.map(a => (
          <AccountRow
            key={a.id}
            account={a}
            integrationName={integrationName}
            onRenamed={() => onChanged()}
            onImported={onChanged}
          />
        ))}
      </ul>
    </li>
  )
}

/** An Account; a plain one's Identifiers can be edited when its Integration can provide one. */
function AccountRow({
  account,
  integrationName,
  canHaveIdentifiers = false,
  onRenamed,
  onImported,
}: {
  account: Account
  integrationName: string
  canHaveIdentifiers?: boolean
  onRenamed: (a: Account) => void
  onImported: () => void
}) {
  const [editing, setEditing] = useState(false)
  const [importing, setImporting] = useState(false)
  // An Account in a Multi-currency account has no Ingest folder of its own; files go into that of its
  // Multi-currency account.
  const inMultiCurrencyAccount = account.multiCurrencyAccountId !== null

  return (
    <li className={`acct-item${inMultiCurrencyAccount ? ' acct-nested' : ''}`}>
      <div className="acct-line">
        {inMultiCurrencyAccount ? (
          <AccountIcon label={account.currency} tone="currency" />
        ) : (
          <AccountIcon label={initial(account.name)} tone={account.owner.kind === 'family' ? 'family' : 'member'} />
        )}
        <div className="acct-name">
          {editing ? (
            <NameAndIdentifiersForm
              initialName={account.name}
              hasIdentifiersField={canHaveIdentifiers}
              initialIdentifiers={account.identifiers}
              onSave={async (name, identifiers) =>
                onRenamed(await updateAccount(account.id, canHaveIdentifiers ? { name, identifiers } : { name }))
              }
              onClose={() => setEditing(false)}
            />
          ) : (
            <>
              <strong>{account.name}</strong>
              {inMultiCurrencyAccount && (
                <button type="button" className="button-link acct-inline-action" onClick={() => setEditing(true)}>
                  Rename
                </button>
              )}
            </>
          )}
          <small>
            {inMultiCurrencyAccount ? (
              account.currency
            ) : (
              <>
                {account.owner.name} · {integrationName}
                {account.identifiers.length > 0 && <> · {account.identifiers.join(', ')}</>}
              </>
            )}
          </small>
        </div>
        <div className="acct-bal">
          <Balance value={account.balance} currency={account.currency} className="acct-amount" />
          <small>opening {formatDate(account.openingDate)}</small>
        </div>
      </div>
      {!inMultiCurrencyAccount && (
        <div className="acct-actions">
          {!editing && (
            <>
              <button type="button" className="button-link" onClick={() => setEditing(true)}>
                {canHaveIdentifiers ? 'Edit' : 'Rename'}
              </button>
              <button type="button" className="button-link" aria-expanded={importing} onClick={() => setImporting(v => !v)}>
                {importing ? 'Close imports' : 'Imports'}
              </button>
            </>
          )}
          <span className="acct-folder">Ingest folder ingest/{account.slug}/</span>
        </div>
      )}
      {account.balanceWarning && (
        <p className="acct-warning" role="alert">
          <strong>Balance mismatch:</strong> {account.balanceWarning.sourceFile.fileName} says{' '}
          <span className="num">
            {formatAmount(account.balanceWarning.statementBalance)} {account.currency}
          </span>{' '}
          on {formatDate(account.balanceWarning.date)}, but the calculated balance on that date is{' '}
          <span className="num">
            {formatAmount(account.balanceWarning.calculatedBalance)} {account.currency}
          </span>
          . An import may be missing or doubled.
        </p>
      )}
      {importing && (
        <ImportPanel
          target={{ kind: 'account', id: account.id }}
          slug={account.slug}
          integrationName={integrationName}
          onImported={onImported}
        />
      )}
    </li>
  )
}

/**
 * Upload a statement into an Account or a Multi-currency account (through its Integration) and list the files
 * imported so far, with undo. Into a Multi-currency account, rows go to the Account of their currency and the result
 * names the Accounts the import created.
 */
function ImportPanel({
  target,
  slug,
  integrationName,
  onImported,
}: {
  target: ImportTarget
  /** Of the Ingest folder failed files are moved to. */
  slug: string
  integrationName: string
  onImported: () => void
}) {
  const [file, setFile] = useState<File | null>(null)
  const [inputKey, setInputKey] = useState(0)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [result, setResult] = useState<ImportResult | null>(null)
  const [sourceFiles, setSourceFiles] = useState<SourceFile[] | null>(null)

  const [undone, setUndone] = useState<string | null>(null)
  const [undoError, setUndoError] = useState<string | null>(null)
  const [reloads, setReloads] = useState(0)

  useEffect(() => {
    const controller = new AbortController()
    listSourceFiles({ kind: target.kind, id: target.id }, controller.signal)
      .then(setSourceFiles)
      .catch(() => {
        if (!controller.signal.aborted) setSourceFiles([])
      })
    return () => controller.abort()
  }, [target.kind, target.id, reloads])

  async function upload(e: FormEvent) {
    e.preventDefault()
    if (!file) {
      setError('Choose a statement file.')
      return
    }
    setBusy(true)
    setError(null)
    setResult(null)
    setUndone(null)
    setUndoError(null)
    try {
      setResult(await importStatement(target, file))
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

  async function undo(f: SourceFile) {
    const count = `${f.transactionCount} Transaction${f.transactionCount === 1 ? '' : 's'}`
    const where = target.kind === 'multiCurrencyAccount' ? ' in every currency (Accounts it created stay)' : ''
    if (!window.confirm(`Undo the import of ${f.fileName}? This deletes the file from Treasury and its ${count}${where}.`))
      return
    setBusy(true)
    setUndoError(null)
    setUndone(null)
    setResult(null)
    setError(null)
    try {
      await deleteSourceFile(f.id)
      setUndone(f.fileName)
    } catch (err) {
      setUndoError(
        err instanceof ApiError && (err.status === 403 || err.status === 404)
          ? 'You can’t undo this import.'
          : 'Something went wrong, try again.',
      )
    } finally {
      setBusy(false)
      onImported()
      setReloads(n => n + 1)
    }
  }

  return (
    <div className="import-panel">
      <form className="import-form" onSubmit={upload}>
        <label className="import-file">
          <span className="label">Statement file ({integrationName})</span>
          <input key={inputKey} type="file" name="file" onChange={e => setFile(e.target.files?.[0] ?? null)} />
        </label>
        <button type="submit" className="button-primary" disabled={busy || !file}>
          {busy ? 'Importing…' : 'Import'}
        </button>
      </form>
      {error && (
        <p className="import-message import-bad" role="alert">
          Not imported: {error} The file was moved to ingest/{slug}/failed/.
        </p>
      )}
      {result && (
        <p className="import-message import-good" role="status">
          Imported {result.importedCount} Transaction{result.importedCount === 1 ? '' : 's'} from{' '}
          {result.sourceFile.fileName}
          {result.skippedCount > 0 && <>, skipped {result.skippedCount} already imported</>}.
        </p>
      )}
      {result?.createdAccounts.map(a => (
        <p key={a.id} className="import-message import-good">
          Created Account {a.name}.
        </p>
      ))}
      {result?.warnings.map(w => (
        <p key={w} className="import-message import-bad">
          {w}
        </p>
      ))}
      {undone && (
        <p className="import-message import-good" role="status">
          Undid the import of {undone}. You can import the file again.
        </p>
      )}
      {undoError && (
        <p className="import-message import-bad" role="alert">
          {undoError}
        </p>
      )}
      {sourceFiles && sourceFiles.length > 0 && (
        <>
          <h3 className="source-files-title">Imported files</h3>
          <ul className="source-files">
            {sourceFiles.map(f => (
              <li key={f.id}>
                <div className="source-file-name">
                  <a href={sourceFileContentUrl(f.id)}>{f.fileName}</a>
                  <small>
                    {formatDate(f.importedAt.slice(0, 10))} · {f.transactionCount} Transaction
                    {f.transactionCount === 1 ? '' : 's'}
                  </small>
                </div>
                <button type="button" className="button-link" disabled={busy} onClick={() => undo(f)}>
                  Undo import
                </button>
              </li>
            ))}
          </ul>
        </>
      )}
    </div>
  )
}

function NewAccountForm({
  integrations,
  currencies,
  onCancel,
  onCreated,
}: {
  integrations: Integration[]
  currencies: Currency[]
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
  const [identifiers, setIdentifiers] = useState<string[]>([])
  const [errors, setErrors] = useState<Errors>({})
  const [busy, setBusy] = useState(false)

  const integration = integrations.find(i => i.key === integrationKey)
  // An Integration holding several currencies is used only through a Multi-currency account (ADR-0009).
  const multiCurrency = integration?.holdsSeveralCurrencies ?? false
  const offerIdentifier = integration?.canProvideIdentifier ?? false
  const enteredIdentifiers = offerIdentifier ? identifiers : []

  async function submit(e: FormEvent) {
    e.preventDefault()
    const balance = openingBalance.trim() === '' ? null : Number(openingBalance.replace(/'/g, ''))
    if (balance !== null && Number.isNaN(balance)) {
      setErrors({ openingBalance: ['Enter a number, e.g. 1234.50'] })
      return
    }
    setBusy(true)
    setErrors({})
    const ownerRef: OwnerRef = owner === 'family' ? { kind: 'family' } : { kind: 'member', memberId: me.id }
    try {
      if (multiCurrency) {
        await createMultiCurrencyAccount({
          name,
          owner: ownerRef,
          integrationKey,
          mainCurrency: currency,
          openingBalance: balance,
          openingDate: openingDate || null,
          identifiers: enteredIdentifiers,
        })
      } else {
        await createAccount({
          name,
          owner: ownerRef,
          integrationKey,
          currency,
          openingBalance: balance,
          openingDate: openingDate || null,
          identifiers: enteredIdentifiers,
        })
      }
      onCreated()
    } catch (err) {
      setErrors(toErrors(err))
    } finally {
      setBusy(false)
    }
  }

  return (
    <form className="ui-component acct-new" onSubmit={submit}>
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
      {multiCurrency && (
        <p className="acct-new-note">
          {integration?.name} holds several currencies, so this creates a Multi-currency account: one Ingest folder,
          an Account for the main currency now, and one for each further currency when a statement first contains
          it. Its total balance is shown in the main currency.
        </p>
      )}
      <SelectField
        label={multiCurrency ? 'Main currency' : 'Currency'}
        name="currency"
        value={currency}
        errors={multiCurrency ? errors.mainCurrency : errors.currency}
        onChange={e => setCurrency(e.target.value)}
      >
        {currencies.map(c => (
          <option key={c} value={c}>
            {c}
          </option>
        ))}
      </SelectField>
      <FormField
        label={multiCurrency ? 'Opening balance (main currency)' : 'Opening balance'}
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
        <IdentifiersField value={identifiers} errors={errors.identifiers} onChange={setIdentifiers} />
      )}
      {errors.form && (
        <p className="form-error" role="alert">
          {errors.form[0]}
        </p>
      )}
      <div className="form-actions">
        <button type="submit" className="button-primary" disabled={busy || integrations.length === 0}>
          {multiCurrency ? 'Create Multi-currency account' : 'Create Account'}
        </button>
        <button type="button" className="button-secondary" onClick={onCancel}>
          Cancel
        </button>
      </div>
    </form>
  )
}
