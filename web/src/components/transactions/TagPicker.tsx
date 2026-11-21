import type { Tag } from '../../lib/api'

/** Tag checkboxes, drawn as toggle pills (a ticked Tag looks like a Tag pill). */
export function TagPicker(props: {
  legend?: string
  tags: Tag[]
  selected: string[]
  errors: string[] | undefined
  hint: string
  onChange: (ids: string[]) => void
}) {
  if (props.tags.length === 0) return null
  return (
    <fieldset className="checkbox-group tag-picker">
      <legend>{props.legend ?? 'Tags'}</legend>
      <span className="tag-picker-hint">{props.hint}</span>
      {props.tags.map(tag => (
        <label key={tag.id} className="tag-picker-option">
          <input
            type="checkbox"
            checked={props.selected.includes(tag.id)}
            onChange={e =>
              props.onChange(e.target.checked ? [...props.selected, tag.id] : props.selected.filter(id => id !== tag.id))
            }
          />{' '}
          {tag.name}
        </label>
      ))}
      {props.errors && <p className="form-error">{props.errors[0]}</p>}
    </fieldset>
  )
}
