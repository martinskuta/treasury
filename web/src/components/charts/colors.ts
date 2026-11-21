/*
 * Colours for charts, as CSS variable references (they work in SVG `style` and React `style` props).
 *
 *   toneColors.income.line   // 'var(--lime-line)', the Income trend line
 *   toneColors.expense.fill  // 'var(--coral)', the Expense bar
 *   seriesColor(i)           // i-th Category colour: var(--c1) … var(--c7), then repeats
 */

/** Fixed meanings: Income lime, Expense coral, Transfer teal. `line` is for strokes, `fill` for bars/areas. */
export const toneColors = {
  income: { fill: 'var(--lime)', line: 'var(--lime-line)', soft: 'var(--lime-soft)', ink: 'var(--inc-ink)' },
  expense: { fill: 'var(--coral)', line: 'var(--coral)', soft: 'var(--coral-soft)', ink: 'var(--exp-ink)' },
  transfer: { fill: 'var(--teal)', line: 'var(--teal)', soft: 'var(--teal-soft)', ink: 'var(--tr-ink)' },
} as const

export type Tone = keyof typeof toneColors

const SERIES_COUNT = 7

/** Colour of the i-th Category series (donuts, stacked bars), in the guideline's order; repeats after seven. */
export function seriesColor(index: number): string {
  return `var(--c${(index % SERIES_COUNT) + 1})`
}
