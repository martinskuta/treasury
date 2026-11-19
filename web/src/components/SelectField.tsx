import { useId, type ReactNode, type SelectHTMLAttributes } from 'react'

type Props = SelectHTMLAttributes<HTMLSelectElement> & {
  label: string
  /** Messages for this field, e.g. ApiError.fieldErrors[name]. */
  errors?: string[]
  children: ReactNode
}

/** Labelled select with its validation messages underneath (the select counterpart of FormField). */
export function SelectField({ label, errors, children, ...select }: Props) {
  const id = useId()
  const errorId = `${id}-errors`
  const invalid = errors !== undefined && errors.length > 0
  return (
    <div className="field">
      <label htmlFor={id}>{label}</label>
      <select id={id} aria-invalid={invalid} aria-describedby={invalid ? errorId : undefined} {...select}>
        {children}
      </select>
      {invalid && (
        <ul id={errorId} className="field-errors">
          {errors.map(e => (
            <li key={e}>{e}</li>
          ))}
        </ul>
      )}
    </div>
  )
}
