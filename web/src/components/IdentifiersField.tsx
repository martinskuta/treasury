import { useId } from 'react'

type Props = {
  /** The Identifiers as typed; blank ones are dropped by the API. */
  value: string[]
  errors?: string[]
  onChange: (identifiers: string[]) => void
}

/**
 * The Identifiers of an Account or a Multi-currency account (GLOSSARY: Identifier): one input each, with a way to
 * add and remove them. Imports add the ones a statement shows, so none need to be typed in.
 */
export function IdentifiersField({ value, errors, onChange }: Props) {
  const id = useId()
  const errorId = `${id}-errors`
  const invalid = errors !== undefined && errors.length > 0
  return (
    <fieldset className="field identifiers-field" aria-describedby={invalid ? errorId : undefined}>
      <legend>Identifiers (IBANs or last card digits; imports add those a statement shows)</legend>
      {value.map((identifier, i) => (
        <div className="identifiers-row" key={i}>
          <input
            aria-label={`Identifier ${i + 1}`}
            aria-invalid={invalid}
            value={identifier}
            onChange={e => onChange(value.map((v, j) => (j === i ? e.target.value : v)))}
          />
          <button type="button" className="button-link" onClick={() => onChange(value.filter((_, j) => j !== i))}>
            Remove
          </button>
        </div>
      ))}
      <button type="button" className="button-link identifiers-add" onClick={() => onChange([...value, ''])}>
        Add Identifier
      </button>
      {invalid && (
        <ul id={errorId} className="field-errors">
          {errors.map(e => (
            <li key={e}>{e}</li>
          ))}
        </ul>
      )}
    </fieldset>
  )
}
