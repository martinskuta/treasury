/*
 * Pill bars: a progress bar on a --track pill, and a stacked bar of rounded segments with 4px gaps.
 *
 *   <ProgressBar label="Expense" value={8912.35} max={12480} color={toneColors.expense.fill} />
 *   <StackedBar
 *     ariaLabel="italy-2026 by Category"
 *     segments={parts.map((p, i) => ({ label: p.name, value: p.amount, color: seriesColor(i) }))}
 *   />
 *
 * Both are drawn with divs (no SVG needed). Each has an accessible name with the numbers; StackedBar segments
 * also have a title. Pair StackedBar with <ChartLegend variant="inline">.
 */
import { formatAmount } from '../../lib/format'

type ProgressProps = {
  /** What is measured; used in the accessible name ("Expense: CHF 8'912.35 of CHF 12'480.00"). */
  label: string
  value: number
  max: number
  color: string
  /** Track height in px (default 10; the guideline allows 10–12). */
  height?: number
  formatValue?: (value: number) => string
}

const chf = (v: number) => `CHF ${formatAmount(v)}`

export function ProgressBar({ label, value, max, color, height = 10, formatValue = chf }: ProgressProps) {
  const share = max > 0 ? Math.min(Math.max(value / max, 0), 1) : 0
  return (
    <div
      className="pill-track"
      style={{ height }}
      role="img"
      aria-label={`${label}: ${formatValue(value)} of ${formatValue(max)}`}
    >
      {share > 0 && <span style={{ width: `${share * 100}%`, background: color }} />}
    </div>
  )
}

export type StackedSegment = { label: string; value: number; color: string }

type StackedProps = {
  ariaLabel: string
  segments: StackedSegment[]
  formatValue?: (value: number) => string
}

export function StackedBar({ ariaLabel, segments, formatValue = chf }: StackedProps) {
  const visible = segments.filter(s => s.value > 0)
  const total = visible.reduce((sum, s) => sum + s.value, 0)
  const description = visible.map(s => `${s.label} ${formatValue(s.value)}`).join(', ')
  return (
    <div
      className={total > 0 ? 'stackbar' : 'stackbar empty-bar'}
      role="img"
      aria-label={description ? `${ariaLabel}: ${description}` : ariaLabel}
    >
      {visible.map((s, i) => (
        <span
          key={`${s.label}-${i}`}
          title={`${s.label}: ${formatValue(s.value)}`}
          style={{ flex: `${(s.value / total).toFixed(4)} 1 0`, background: s.color }}
        />
      ))}
    </div>
  )
}
