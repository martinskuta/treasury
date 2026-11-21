/*
 * Chart primitives: inline SVG / div charts styled from the design tokens, no chart library
 * (docs/design-guidelines.md, "Charts"). Each file starts with a usage example.
 *
 *   import { TrendChart, Donut, ProgressBar, StackedBar, ChartLegend, seriesColor, toneColors } from '../components/charts'
 */
export { ProgressBar, StackedBar, type StackedSegment } from './Bars'
export { ChartLegend, type LegendItem } from './ChartLegend'
export { seriesColor, toneColors, type Tone } from './colors'
export { Donut, type DonutSegment } from './Donut'
export { TrendChart, type TrendSeries } from './TrendChart'
export { useElementWidth } from './useElementWidth'
