/*
 * Smooth trend chart (Last 6 months, Monthly overview, any time series): inline SVG sized to its container.
 * Curves have horizontal tangents at each point (never overshoot), 3px round strokes, a gradient fill under each
 * line, an end marker on the latest point, 3–4 faint gridlines and muted labels with the current one in ink.
 *
 *   <TrendChart
 *     ariaLabel="Income and Expense, last 6 months"
 *     labels={['Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov']}
 *     series={[
 *       { name: 'Income', values: income, tone: 'income' },
 *       { name: 'Expense', values: expense, tone: 'expense' },
 *     ]}
 *   />
 *
 * Gridlines and the y range are worked out from the data unless `yTicks` / `min` / `max` are given.
 * Each point has a <title> ("Income Nov: CHF 12'480.00") as the text fallback; pair it with a legend or the totals.
 * Below 600px wide it gets shorter and thinner (`compactHeight`).
 */
import { useId } from 'react'
import { formatAmount, formatCompactAmount } from '../../lib/format'
import { toneColors, type Tone } from './colors'
import { autoScale, smoothPath } from './geometry'
import { useElementWidth } from './useElementWidth'

export type TrendSeries = {
  name: string
  /** One value per label. */
  values: number[]
  /** Income / Expense / Transfer colours; or give `color` (any CSS colour, e.g. seriesColor(2)). */
  tone?: Tone
  color?: string
  /** Opacity at the top of the gradient fill (default: 0.45 Income, 0.16 Expense, 0.38 otherwise). */
  fillOpacity?: number
}

type Props = {
  ariaLabel: string
  labels: string[]
  series: TrendSeries[]
  /** Height at 600px wide and more (default 280). */
  height?: number
  /** Height below 600px wide (default 210). */
  compactHeight?: number
  /** Gridline values; default 3–4 rounded values from the data. */
  yTicks?: number[]
  min?: number
  max?: number
  /** Label shown in ink (the current period); default the last one. Pass -1 for none. */
  currentIndex?: number
  /** Value text in tooltips; default "CHF 1'234.50". */
  formatValue?: (value: number) => string
  /** Gridline labels; default "12k". */
  formatTick?: (value: number) => string
}

const PAD_LEFT = 30
const PAD_TOP = 14
const PAD_BOTTOM = 28

export function TrendChart({
  ariaLabel,
  labels,
  series,
  height = 280,
  compactHeight = 210,
  yTicks,
  min,
  max,
  currentIndex = labels.length - 1,
  formatValue = v => `CHF ${formatAmount(v)}`,
  formatTick = formatCompactAmount,
}: Props) {
  const [ref, width] = useElementWidth<HTMLDivElement>()
  const gradientId = `trend${useId().replace(/[^a-zA-Z0-9]/g, '')}`
  const compact = width < 600
  const h = compact ? compactHeight : height

  const scale = autoScale(series.flatMap(s => s.values))
  const ticks = yTicks ?? scale.ticks
  const lo = min ?? (yTicks ? Math.min(...yTicks) : scale.min)
  const hi = max ?? (yTicks ? Math.max(...yTicks) : scale.max)

  const padX = compact ? 10 : 28
  const stroke = compact ? 2.5 : 3
  const x0 = PAD_LEFT + padX
  const x1 = width - padX
  const x = (i: number) => (labels.length < 2 ? (x0 + x1) / 2 : x0 + ((x1 - x0) * i) / (labels.length - 1))
  const y = (v: number) => PAD_TOP + (h - PAD_TOP - PAD_BOTTOM) * (1 - (v - lo) / (hi - lo || 1))
  const bottom = h - PAD_BOTTOM

  const drawn = series.map(s => {
    const color = s.color ?? toneColors[s.tone ?? 'income'].line
    const fill = s.color ?? toneColors[s.tone ?? 'income'].fill
    const opacity = s.fillOpacity ?? (s.tone === 'income' ? 0.45 : s.tone === 'expense' ? 0.16 : 0.38)
    const points = s.values.map((v, i) => [x(i), y(v)] as [number, number])
    return { ...s, color, fill, opacity, points, line: smoothPath(points) }
  })

  return (
    <div ref={ref} className="chart" style={{ minHeight: h }}>
      {width > 0 && (
        <svg width={width} height={h} viewBox={`0 0 ${width} ${h}`} role="img" aria-label={ariaLabel}>
          <defs>
            {drawn.map((s, j) => (
              <linearGradient key={s.name} id={`${gradientId}-${j}`} x1="0" y1="0" x2="0" y2="1">
                <stop offset="0" style={{ stopColor: s.fill, stopOpacity: s.opacity }} />
                <stop offset="0.9" style={{ stopColor: s.fill, stopOpacity: 0 }} />
              </linearGradient>
            ))}
          </defs>
          {ticks.map(v => (
            <g key={v}>
              <line x1={PAD_LEFT} x2={width} y1={y(v)} y2={y(v)} style={{ stroke: 'var(--grid)' }} strokeWidth={1} />
              <text x={0} y={y(v) + 4}>
                {formatTick(v)}
              </text>
            </g>
          ))}
          {drawn.map((s, j) =>
            s.points.length === 0 ? null : (
              <g key={s.name}>
                <path
                  d={`${s.line} L${s.points[s.points.length - 1][0]},${bottom} L${s.points[0][0]},${bottom} Z`}
                  fill={`url(#${gradientId}-${j})`}
                />
                <path d={s.line} fill="none" style={{ stroke: s.color }} strokeWidth={stroke} strokeLinecap="round" />
              </g>
            ),
          )}
          {/* Invisible hover targets with the values as text fallback. */}
          {drawn.map(s =>
            s.values.map((v, i) => (
              <circle key={`${s.name}-${i}`} cx={x(i)} cy={y(v)} r={10} fill="transparent">
                <title>{`${s.name} ${labels[i]}: ${formatValue(v)}`}</title>
              </circle>
            )),
          )}
          {drawn.map(s => {
            const i = s.values.length - 1
            if (i < 0) return null
            return (
              <circle
                key={`${s.name}-end`}
                cx={x(i)}
                cy={y(s.values[i])}
                r={stroke + 3}
                style={{ fill: 'var(--panel)', stroke: s.color }}
                strokeWidth={stroke}
              >
                <title>{`${s.name} ${labels[i]}: ${formatValue(s.values[i])}`}</title>
              </circle>
            )
          })}
          {labels.map((label, i) => (
            <text key={i} x={x(i)} y={h - 6} textAnchor="middle" className={i === currentIndex ? 'current' : undefined}>
              {label}
            </text>
          ))}
        </svg>
      )}
    </div>
  )
}
