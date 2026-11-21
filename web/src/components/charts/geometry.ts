// Geometry helpers for the chart primitives (pure functions, no React).

/** Path through the points with horizontal tangents: control points at the horizontal midpoint. */
export function smoothPath(points: [number, number][]): string {
  if (points.length === 0) return ''
  let d = `M${points[0][0]},${points[0][1]}`
  for (let i = 1; i < points.length; i++) {
    const [x0, y0] = points[i - 1]
    const [x1, y1] = points[i]
    const m = (x1 - x0) / 2
    d += ` C${x0 + m},${y0} ${x1 - m},${y1} ${x1},${y1}`
  }
  return d
}

function niceStep(rough: number): number {
  const power = 10 ** Math.floor(Math.log10(rough))
  const fraction = rough / power
  return (fraction <= 1 ? 1 : fraction <= 2 ? 2 : fraction <= 2.5 ? 2.5 : fraction <= 5 ? 5 : 10) * power
}

/** Gridlines and y range from the data: about three steps, with room below the lowest gridline for the fill. */
export function autoScale(values: number[]): { ticks: number[]; min: number; max: number } {
  const lo = values.length ? Math.min(...values) : 0
  const hi = values.length ? Math.max(...values) : 0
  const span = hi - lo || Math.max(Math.abs(hi), 1000)
  const step = niceStep(span / 3)
  const first = Math.floor(lo / step) * step
  const last = Math.max(Math.ceil(hi / step) * step, first + step)
  const ticks: number[] = []
  for (let v = first; v <= last + step / 2; v += step) ticks.push(v)
  const min = lo >= 0 ? Math.max(0, first - step) : first - step
  return { ticks: min === first ? ticks.slice(1) : ticks, min, max: last + step / 4 }
}
