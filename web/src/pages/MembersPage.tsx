import { useEffect, useState, type FormEvent } from 'react'
import { FormField } from '../components/FormField'
import { OwnerSelect } from '../components/OwnerSelect'
import {
  addMember,
  deactivateMember,
  isAdmin,
  listMemberAccountsForHandover,
  listMembers,
  resetMemberPassword,
  setMemberAdmin,
  setMemberName,
  type Handover,
  type HandoverAccount,
  type MemberSummary,
  type Owner,
} from '../lib/api'
import { toErrors, type Errors } from '../lib/errors'
import { useMe } from '../lib/auth'
import { fullName, initials, memberLabel } from '../lib/members'
import { ownerRefFromValue } from '../lib/owners'
import '../styles/settings.css'

/** Settings › Members (Admin only): add Members, change names, reset passwords, give/remove the Admin role, deactivate. */
export function MembersPage() {
  const me = useMe()
  const [members, setMembers] = useState<MemberSummary[] | null>(null)
  const [loadError, setLoadError] = useState(false)
  const [adding, setAdding] = useState(false)
  const admin = isAdmin(me)

  useEffect(() => {
    if (!admin) return
    const controller = new AbortController()
    listMembers(controller.signal)
      .then(setMembers)
      .catch(() => {
        if (!controller.signal.aborted) setLoadError(true)
      })
    return () => controller.abort()
  }, [admin])

  const reload = () =>
    listMembers()
      .then(setMembers)
      .catch(() => setLoadError(true))

  if (!admin) {
    return (
      <div className="settings-page">
        <h1 className="page-title">Members</h1>
        <p className="status status-bad">Only Admins can manage Members.</p>
      </div>
    )
  }

  return (
    <div className="settings-page">
      <div className="page-header">
        <h1 className="page-title">Members</h1>
        {!adding && (
          <button type="button" className="button-primary" onClick={() => setAdding(true)}>
            Add Member
          </button>
        )}
      </div>
      {adding && (
        <AddMemberForm
          onCancel={() => setAdding(false)}
          onAdded={() => {
            setAdding(false)
            void reload()
          }}
        />
      )}
      {loadError && <p className="status status-bad">Couldn’t load the Members.</p>}
      {members === null && !loadError && <p className="page-loading">Loading…</p>}
      {members !== null && (
        <section className="ui-component" aria-label="Members">
          <ul className="settings-list">
            {members.map(m => (
              <MemberRow key={m.id} member={m} members={members} isMe={m.id === me.id} onChanged={() => void reload()} />
            ))}
          </ul>
        </section>
      )}
    </div>
  )
}

type Panel = 'none' | 'name' | 'password' | 'deactivate'

function MemberRow({
  member,
  members,
  isMe,
  onChanged,
}: {
  member: MemberSummary
  members: MemberSummary[]
  isMe: boolean
  onChanged: () => void
}) {
  const [panel, setPanel] = useState<Panel>('none')
  const [errors, setErrors] = useState<Errors>({})
  const [busy, setBusy] = useState(false)

  async function toggleAdmin() {
    setBusy(true)
    setErrors({})
    try {
      await setMemberAdmin(member.id, !member.isAdmin)
      onChanged()
    } catch (err) {
      setErrors(toErrors(err))
    } finally {
      setBusy(false)
    }
  }

  const close = () => setPanel('none')

  return (
    <li className={`settings-row member-row${member.isActive ? '' : ' member-inactive'}`}>
      <div className="settings-row-main">
        <span className="member-avatar" aria-hidden="true">
          {initials(member)}
        </span>
        <span className="settings-row-name">
          {fullName(member)} <span className="muted">({member.userName})</span>
        </span>
        <span className="member-chips">
          {member.isAdmin && <span className="chip">Admin</span>}
          {!member.isActive && <span className="chip">Deactivated</span>}
          {isMe && <span className="chip">you</span>}
        </span>
        <div className="settings-row-actions">
          <button type="button" className="button-link" onClick={() => setPanel('name')}>
            Edit name
          </button>
          {member.isActive && (
            <>
              <button type="button" className="button-link" onClick={() => setPanel('password')}>
                Reset password
              </button>
              <button type="button" className="button-link" disabled={busy} onClick={() => void toggleAdmin()}>
                {member.isAdmin ? 'Remove Admin role' : 'Make Admin'}
              </button>
              {!isMe && (
                <button type="button" className="button-link" onClick={() => setPanel('deactivate')}>
                  Deactivate
                </button>
              )}
            </>
          )}
        </div>
      </div>
      {(errors.isAdmin ?? errors.form) && (
        <p className="form-error" role="alert">
          {(errors.isAdmin ?? errors.form)[0]}
        </p>
      )}
      {panel === 'name' && (
        <EditNameForm
          member={member}
          onCancel={close}
          onDone={() => {
            close()
            onChanged()
          }}
        />
      )}
      {panel === 'password' && <ResetPasswordForm member={member} onDone={close} />}
      {panel === 'deactivate' && (
        <DeactivateForm
          member={member}
          members={members}
          onCancel={close}
          onDone={() => {
            close()
            onChanged()
          }}
        />
      )}
    </li>
  )
}

function AddMemberForm({ onCancel, onAdded }: { onCancel: () => void; onAdded: () => void }) {
  const [userName, setUserName] = useState('')
  const [firstName, setFirstName] = useState('')
  const [lastName, setLastName] = useState('')
  const [password, setPassword] = useState('')
  const [errors, setErrors] = useState<Errors>({})
  const [busy, setBusy] = useState(false)

  async function submit(e: FormEvent) {
    e.preventDefault()
    setBusy(true)
    setErrors({})
    try {
      await addMember({ userName, firstName, lastName, password })
      onAdded()
    } catch (err) {
      setErrors(toErrors(err))
    } finally {
      setBusy(false)
    }
  }

  return (
    <form className="ui-component form settings-narrow" onSubmit={submit}>
      <h2>Add Member</h2>
      <FormField
        label="User name"
        name="userName"
        autoComplete="off"
        required
        value={userName}
        errors={errors.userName}
        onChange={e => setUserName(e.target.value)}
      />
      <FormField
        label="First name"
        name="firstName"
        autoComplete="off"
        required
        value={firstName}
        errors={errors.firstName}
        onChange={e => setFirstName(e.target.value)}
      />
      <FormField
        label="Last name (optional)"
        name="lastName"
        autoComplete="off"
        value={lastName}
        errors={errors.lastName}
        onChange={e => setLastName(e.target.value)}
      />
      <FormField
        label="Initial password"
        name="password"
        type="password"
        autoComplete="new-password"
        required
        value={password}
        errors={errors.password}
        onChange={e => setPassword(e.target.value)}
      />
      {errors.form && (
        <p className="form-error" role="alert">
          {errors.form[0]}
        </p>
      )}
      <div className="form-actions">
        <button type="submit" className="button-primary" disabled={busy}>
          Add Member
        </button>
        <button type="button" className="button-secondary" onClick={onCancel}>
          Cancel
        </button>
      </div>
    </form>
  )
}

function EditNameForm({ member, onCancel, onDone }: { member: MemberSummary; onCancel: () => void; onDone: () => void }) {
  const [firstName, setFirstName] = useState(member.firstName)
  const [lastName, setLastName] = useState(member.lastName ?? '')
  const [errors, setErrors] = useState<Errors>({})
  const [busy, setBusy] = useState(false)

  async function submit(e: FormEvent) {
    e.preventDefault()
    setBusy(true)
    setErrors({})
    try {
      await setMemberName(member.id, firstName, lastName)
      onDone()
    } catch (err) {
      setErrors(toErrors(err))
      setBusy(false)
    }
  }

  return (
    <form className="settings-edit" onSubmit={submit}>
      <FormField
        label={`First name of ${member.userName}`}
        name="firstName"
        autoComplete="off"
        required
        value={firstName}
        errors={errors.firstName ?? errors.form}
        onChange={e => setFirstName(e.target.value)}
      />
      <FormField
        label="Last name (optional)"
        name="lastName"
        autoComplete="off"
        value={lastName}
        errors={errors.lastName}
        onChange={e => setLastName(e.target.value)}
      />
      <div className="form-actions">
        <button type="submit" className="button-primary" disabled={busy}>
          Save name
        </button>
        <button type="button" className="button-secondary" onClick={onCancel}>
          Cancel
        </button>
      </div>
    </form>
  )
}

function ResetPasswordForm({ member, onDone }: { member: MemberSummary; onDone: () => void }) {
  const [newPassword, setNewPassword] = useState('')
  const [errors, setErrors] = useState<Errors>({})
  const [done, setDone] = useState(false)
  const [busy, setBusy] = useState(false)

  async function submit(e: FormEvent) {
    e.preventDefault()
    setBusy(true)
    setErrors({})
    try {
      await resetMemberPassword(member.id, newPassword)
      setDone(true)
      setNewPassword('')
    } catch (err) {
      setErrors(toErrors(err))
    } finally {
      setBusy(false)
    }
  }

  return (
    <form className="settings-edit" onSubmit={submit}>
      <FormField
        label={`New password for ${memberLabel(member)}`}
        name="newPassword"
        type="password"
        autoComplete="new-password"
        required
        value={newPassword}
        errors={errors.newPassword ?? errors.form}
        onChange={e => setNewPassword(e.target.value)}
      />
      {done && (
        <p className="status status-good" role="status">
          Password reset. {member.firstName}’s open sessions have ended.
        </p>
      )}
      <div className="form-actions">
        <button type="submit" className="button-primary" disabled={busy}>
          Reset password
        </button>
        <button type="button" className="button-secondary" onClick={onDone}>
          {done ? 'Close' : 'Cancel'}
        </button>
      </div>
    </form>
  )
}

const FAMILY = 'family'
const KEEP = 'keep'

/** Choose who gets each of the Member's Accounts, then deactivate. */
function DeactivateForm({
  member,
  members,
  onCancel,
  onDone,
}: {
  member: MemberSummary
  members: MemberSummary[]
  onCancel: () => void
  onDone: () => void
}) {
  const [accounts, setAccounts] = useState<HandoverAccount[] | null>(null)
  const [targets, setTargets] = useState<Record<string, string>>({})
  const [errors, setErrors] = useState<Errors>({})
  const [busy, setBusy] = useState(false)
  // The Family or another active Member.
  const targetOwners: Owner[] = [
    { kind: 'family', memberId: null, name: 'Family' },
    ...members
      .filter(m => m.isActive && m.id !== member.id)
      .map(m => ({ kind: 'member' as const, memberId: m.id, name: memberLabel(m) })),
  ]

  useEffect(() => {
    const controller = new AbortController()
    listMemberAccountsForHandover(member.id, controller.signal)
      .then(list => {
        setAccounts(list)
        setTargets(Object.fromEntries(list.map(a => [a.id, FAMILY])))
      })
      .catch(() => {
        if (!controller.signal.aborted) setErrors({ form: ['Couldn’t load the Member’s Accounts.'] })
      })
    return () => controller.abort()
  }, [member.id])

  async function submit(e: FormEvent) {
    e.preventDefault()
    setBusy(true)
    setErrors({})
    const handover: Handover[] = (accounts ?? [])
      .filter(a => (targets[a.id] ?? FAMILY) !== KEEP)
      .map(a => {
        const owner = ownerRefFromValue(targets[a.id] ?? FAMILY)
        return a.isMultiCurrencyAccount ? { multiCurrencyAccountId: a.id, owner } : { accountId: a.id, owner }
      })
    try {
      await deactivateMember(member.id, handover)
      onDone()
    } catch (err) {
      setErrors(toErrors(err))
      setBusy(false)
    }
  }

  return (
    <form className="settings-edit" onSubmit={submit}>
      <p>
        Deactivate <strong>{memberLabel(member)}</strong>? They won’t be able to log in any more; their data is kept.
      </p>
      {accounts === null && !errors.form && <p>Loading Accounts…</p>}
      {accounts !== null && accounts.length === 0 && <p>{member.firstName} owns no Accounts.</p>}
      {accounts?.map(a => (
        <OwnerSelect
          key={a.id}
          label={
            a.isMultiCurrencyAccount
              ? `Hand “${a.name}” (Multi-currency account, all its Accounts) to`
              : `Hand “${a.name}” (${a.currency}) to`
          }
          name={`handover-${a.id}`}
          owners={targetOwners}
          value={targets[a.id] ?? FAMILY}
          after={<option value={KEEP}>Nobody (keep with {memberLabel(member)})</option>}
          onChange={value => setTargets(t => ({ ...t, [a.id]: value }))}
        />
      ))}
      {(errors.handover ?? errors.member ?? errors.form)?.map(message => (
        <p key={message} className="form-error" role="alert">
          {message}
        </p>
      ))}
      <div className="form-actions">
        <button type="submit" className="button-primary" disabled={busy || accounts === null}>
          Deactivate
        </button>
        <button type="button" className="button-secondary" onClick={onCancel}>
          Cancel
        </button>
      </div>
    </form>
  )
}
