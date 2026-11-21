import type { Category } from '../../lib/api'
import type { Errors } from '../../lib/errors'
import { CategoryOptions } from '../CategoryOptions'
import { FormField } from '../FormField'
import { SelectField } from '../SelectField'

/** Date, amount and description: the facts of a manual Transaction. */
export function FactFields(props: {
  date: string
  amount: string
  description: string
  currency: string | undefined
  errors: Errors
  onDate: (v: string) => void
  onAmount: (v: string) => void
  onDescription: (v: string) => void
}) {
  return (
    <>
      <FormField
        label="Date"
        name="date"
        type="date"
        required
        value={props.date}
        errors={props.errors.date}
        onChange={e => props.onDate(e.target.value)}
      />
      <FormField
        label={`Amount${props.currency ? ` (${props.currency})` : ''}: negative for an Expense`}
        name="amount"
        required
        value={props.amount}
        errors={props.errors.amount}
        onChange={e => props.onAmount(e.target.value)}
      />
      <FormField
        label="Description"
        name="description"
        required
        value={props.description}
        errors={props.errors.description}
        onChange={e => props.onDescription(e.target.value)}
      />
    </>
  )
}

/** Category picker; "" = Uncategorised. */
export function CategoryField(props: {
  categories: Category[]
  value: string
  errors: string[] | undefined
  onChange: (v: string) => void
}) {
  return (
    <SelectField
      label="Category"
      name="categoryId"
      value={props.value}
      errors={props.errors}
      onChange={e => props.onChange(e.target.value)}
    >
      <option value="">Uncategorised</option>
      <CategoryOptions categories={props.categories} />
    </SelectField>
  )
}
