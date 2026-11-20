import type { Category } from '../lib/api'

/** <option>s for a Category picker, in tree order, labelled with the full path ("Food › Eating out"). */
export function CategoryOptions({ categories, exclude }: { categories: Category[]; exclude?: (c: Category) => boolean }) {
  return (
    <>
      {categories
        .filter(c => !exclude?.(c))
        .map(c => (
          <option key={c.id} value={c.id}>
            {c.path}
          </option>
        ))}
    </>
  )
}
