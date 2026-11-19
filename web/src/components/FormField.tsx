import { useId, type InputHTMLAttributes } from 'react'

type Props = InputHTMLAttributes<HTMLInputElement> & {
  label: string
  /** Messages for this field, e.g. ApiError.fieldErrors[name]. */
  errors?: string[]
}

/** Labelled input with its validation messages underneath. */
export function FormField({ label, errors, ...input }: Props) {
  const id = useId()
  const errorId = `${id}-errors`
  const invalid = errors !== undefined && errors.length > 0
  return (
    <div className="field">
      <label htmlFor={id}>{label}</label>
      <input id={id} aria-invalid={invalid} aria-describedby={invalid ? errorId : undefined} {...input} />
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
