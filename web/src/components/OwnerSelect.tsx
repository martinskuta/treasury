import type { ReactNode } from 'react'
import type { Owner } from '../lib/api'
import { ownerValue } from '../lib/owners'
import { SelectField } from './SelectField'

/** The offered Owners as options; `current` is added when it isn't offered any more (e.g. a deactivated Member). */
export function OwnerOptions({ owners, current }: { owners: Owner[]; current?: Owner | null }) {
  const offered = current && !owners.some(o => ownerValue(o) === ownerValue(current)) ? [...owners, current] : owners
  return (
    <>
      {offered.map(o => (
        <option key={ownerValue(o)} value={ownerValue(o)}>
          {o.name}
        </option>
      ))}
    </>
  )
}

/**
 * Owner picker. Values are {@link ownerValue}s ("family" or a Member id); `before` / `after` add options ahead of /
 * after the Owners, e.g. `<option value="">Keep as it is</option>`.
 */
export function OwnerSelect({
  label = 'Owner',
  name = 'owner',
  owners,
  current,
  value,
  errors,
  before,
  after,
  onChange,
}: {
  label?: string
  name?: string
  owners: Owner[]
  current?: Owner | null
  value: string
  errors?: string[]
  before?: ReactNode
  after?: ReactNode
  onChange: (value: string) => void
}) {
  return (
    <SelectField label={label} name={name} value={value} errors={errors} onChange={e => onChange(e.target.value)}>
      {before}
      <OwnerOptions owners={owners} current={current} />
      {after}
    </SelectField>
  )
}
