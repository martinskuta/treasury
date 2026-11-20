import { useEffect, useState, type FormEvent } from 'react'
import { FormField } from '../components/FormField'
import { SelectField } from '../components/SelectField'
import {
  ApiError,
  addMember,
  deactivateMember,
  isAdmin,
  listMemberAccountsForHandover,
  listMembers,
  resetMemberPassword,
  setMemberAdmin,
  type HandoverAccount,
  type MemberSummary,
  type OwnerRef,
} from '../lib/api'
import { useMe } from '../lib/auth'

type Errors = Record<string, string[]>

const toErrors = (err: unknown): Errors =>
  err instanceof ApiError && err.status === 400 ? err.fieldErrors : { form: ['Something went wrong, try again.'] }

/** Settings › Members (Admin only): add Members, reset passwords, give/remove the Admin role, deactivate. */
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
      <>
        <h1>Members</h1>
        <p className="status status-bad">Only Admins can manage Members.</p>
      </>
    )
  }

  return (
    <>
      <h1>Members</h1>
      {loadError && <p className="status status-bad">Couldn’t load the Members.</p>}
      {members === null && !loadError && <p>Loading…</p>}
      {members !== null && (
        <section className="card">
          <ul className="member-list">
            {members.map(m => (
              <MemberRow key={m.id} member={m} members={members} isMe={m.id === me.id} onChanged={() => void reload()} />
            ))}
          </ul>
        </section>
      )}
      {adding ? (
        <AddMemberForm
          onCancel={() => setAdding(false)}
          onAdded={() => {
            setAdding(false)
            void reload()
          }}
        />
      ) : (
        <button type="button" className="button-primary" onClick={() => setAdding(true)}>
          Add Member
        </button>
      )}
    </>
  )
}

type Panel = 'none' | 'password' | 'deactivate'

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
    <li className="member-row">
      <div className="member-head">
        <strong>{member.userName}</strong>
        <span className="member-meta">
          {[member.isAdmin && 'Admin', !member.isActive && 'Deactivated', isMe && 'you'].filter(Boolean).join(' · ')}
        </span>
      </div>
      {member.isActive && (
        <div className="member-actions">
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
        </div>
      )}
      {(errors.isAdmin ?? errors.form) && (
        <p className="form-error" role="alert">
          {(errors.isAdmin ?? errors.form)[0]}
        </p>
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
  const [password, setPassword] = useState('')
  const [errors, setErrors] = useState<Errors>({})
  const [busy, setBusy] = useState(false)

  async function submit(e: FormEvent) {
    e.preventDefault()
    setBusy(true)
    setErrors({})
    try {
      await addMember(userName, password)
      onAdded()
    } catch (err) {
      setErrors(toErrors(err))
    } finally {
      setBusy(false)
    }
  }

  return (
    <form className="card form" onSubmit={submit}>
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
    <form className="member-panel" onSubmit={submit}>
      <FormField
        label={`New password for ${member.userName}`}
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
          Password reset. {member.userName}’s open sessions have ended.
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
  const others = members.filter(m => m.isActive && m.id !== member.id)

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
    const handover = Object.entries(targets)
      .filter(([, target]) => target !== KEEP)
      .map(([accountId, target]) => ({
        accountId,
        owner: (target === FAMILY ? { kind: 'family' } : { kind: 'member', memberId: target }) satisfies OwnerRef,
      }))
    try {
      await deactivateMember(member.id, handover)
      onDone()
    } catch (err) {
      setErrors(toErrors(err))
      setBusy(false)
    }
  }

  return (
    <form className="member-panel" onSubmit={submit}>
      <p>
        Deactivate <strong>{member.userName}</strong>? They won’t be able to log in any more; their data is kept.
      </p>
      {accounts === null && !errors.form && <p>Loading Accounts…</p>}
      {accounts !== null && accounts.length === 0 && <p>{member.userName} owns no Accounts.</p>}
      {accounts?.map(a => (
        <SelectField
          key={a.id}
          label={`Hand “${a.name}” (${a.currency}) to`}
          name={`handover-${a.id}`}
          value={targets[a.id] ?? FAMILY}
          onChange={e => setTargets(t => ({ ...t, [a.id]: e.target.value }))}
        >
          <option value={FAMILY}>Family</option>
          {others.map(m => (
            <option key={m.id} value={m.id}>
              {m.userName}
            </option>
          ))}
          <option value={KEEP}>Nobody (keep with {member.userName})</option>
        </SelectField>
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
