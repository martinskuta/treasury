/*
 * Donut / ring with rounded segment ends and small gaps; optional total in the centre.
 *
 *   <Donut
 *     ariaLabel="Spending by Category"
 *     segments={categories.map((c, i) => ({ label: c.name, value: c.amount, color: seriesColor(i) }))}
 *     centreLabel="CHF"
 *     centreValue="8'912"
 *   />
 *
 * Pair it with <ChartLegend> for the names and amounts; each segment also has a <title>. With nothing to show
 * (no segments or a zero total) it draws an empty --track ring. Values must be positive.
 */
import { formatAmount } from '../../lib/format'

export type DonutSegment = { label: string; value: number; color: string }

type Props = {
  ariaLabel: string
  segments: DonutSegment[]
  /** Outer size in px (default 148). */
  size?: number
  /** Ring thickness in px (default 16). */
  thickness?: number
  /** Small muted text above the centre value, e.g. "CHF". */
  centreLabel?: string
  /** Centre value in ink, already formatted (e.g. formatAmount(total).slice(0, -3) for whole francs). */
  centreValue?: string
  /** Tooltip text per segment; default "CHF 1'234.50". */
  formatValue?: (value: number) => string
}

const GAP = 3

export function Donut({
  ariaLabel,
  segments,
  size = 148,
  thickness = 16,
  centreLabel,
  centreValue,
  formatValue = v => `CHF ${formatAmount(v)}`,
}: Props) {
  const r = (size - thickness) / 2
  const c = size / 2
  const circumference = 2 * Math.PI * r
  const visible = segments.filter(s => s.value > 0)
  const total = visible.reduce((sum, s) => sum + s.value, 0)

  // Start of each segment along the circle: the lengths of the segments before it.
  const lengths = visible.map(s => (s.value / total) * circumference)
  const starts = lengths.map((_, i) => lengths.slice(0, i).reduce((sum, l) => sum + l, 0))
  const arcs = visible.map((s, i) => ({
    ...s,
    // Round caps add thickness/2 at each end, so shorten the dash by the thickness plus the gap.
    dash: visible.length === 1 ? circumference : Math.max(lengths[i] - thickness - GAP, 0.01),
    dashOffset: -(starts[i] + thickness / 2 + GAP / 2),
  }))

  return (
    <div className="chart">
      <svg width={size} height={size} viewBox={`0 0 ${size} ${size}`} role="img" aria-label={ariaLabel}>
        {total === 0 && (
          <circle cx={c} cy={c} r={r} fill="none" style={{ stroke: 'var(--track)' }} strokeWidth={thickness} />
        )}
        {arcs.map((a, i) => (
          <circle
            key={`${a.label}-${i}`}
            cx={c}
            cy={c}
            r={r}
            fill="none"
            style={{ stroke: a.color }}
            strokeWidth={thickness}
            strokeLinecap={visible.length === 1 ? 'butt' : 'round'}
            strokeDasharray={`${a.dash} ${circumference}`}
            strokeDashoffset={visible.length === 1 ? 0 : a.dashOffset}
            transform={`rotate(-90 ${c} ${c})`}
          >
            <title>{`${a.label}: ${formatValue(a.value)}`}</title>
          </circle>
        ))}
        {centreLabel && (
          <text x={c} y={centreValue ? c - 4 : c + 4} textAnchor="middle" className="donut-label">
            {centreLabel}
          </text>
        )}
        {centreValue && (
          <text x={c} y={centreLabel ? c + 16 : c + 6} textAnchor="middle" className="donut-value">
            {centreValue}
          </text>
        )}
      </svg>
    </div>
  )
}
