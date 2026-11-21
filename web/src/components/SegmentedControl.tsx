/*
 * Segmented control: a pill track with one selected segment (Owner toggle, chart switches, Frankie's mood…).
 *
 *   <SegmentedControl
 *     label="Owner"
 *     value={owner}
 *     options={[{ value: 'me', label: 'Mine' }, { value: 'family', label: 'Family' }]}
 *     onChange={setOwner}
 *   />
 *
 * Renders .segmented with buttons using aria-pressed (a toggle-button group); keyboard: Tab between segments.
 */
import type { ReactNode } from 'react'

export type SegmentedOption<T extends string> = { value: T; label: ReactNode }

type Props<T extends string> = {
  /** Accessible name of the group. */
  label: string
  value: T
  options: readonly SegmentedOption<T>[]
  onChange: (value: T) => void
  className?: string
}

export function SegmentedControl<T extends string>({ label, value, options, onChange, className }: Props<T>) {
  return (
    <div className={className ? `segmented ${className}` : 'segmented'} role="group" aria-label={label}>
      {options.map(option => (
        <button
          key={option.value}
          type="button"
          aria-pressed={option.value === value}
          onClick={() => onChange(option.value)}
        >
          {option.label}
        </button>
      ))}
    </div>
  )
}
