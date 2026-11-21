/*
 * Chart legend: 8px dots, muted labels, values in tabular figures.
 *
 *   <ChartLegend items={[{ label: 'Housing', value: "3'150", color: seriesColor(0) }, …]} />      // list, values right
 *   <ChartLegend variant="inline" items={[{ label: 'Income', color: toneColors.income.fill }]} /> // one wrapping row
 *
 * Values are pre-formatted (string or element) so the caller decides on cents.
 */
import type { ReactNode } from 'react'

export type LegendItem = { label: ReactNode; color: string; value?: ReactNode }

type Props = {
  items: LegendItem[]
  /** 'list' (default): one per line, value right-aligned. 'inline': a wrapping row. */
  variant?: 'list' | 'inline'
  className?: string
}

export function ChartLegend({ items, variant = 'list', className }: Props) {
  const base = variant === 'inline' ? 'legend-inline' : 'legend'
  return (
    <ul className={className ? `${base} ${className}` : base}>
      {items.map((item, i) => (
        <li key={i}>
          <span className="dot" style={{ background: item.color }} />
          {item.label}
          {item.value !== undefined && <span className="legend-value">{item.value}</span>}
        </li>
      ))}
    </ul>
  )
}
