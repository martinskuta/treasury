import { useEffect, useState } from 'react'
import { useNavigate, useParams, useSearchParams } from 'react-router'
import { FormField } from '../components/FormField'
import { ReportBreakdown, ReportOwnerToggle, ReportSideToggle, ReportTotals } from '../components/ReportViews'
import { SelectField } from '../components/SelectField'
import {
  ApiError,
  getTagReport,
  listTags,
  tagReportPath,
  type Tag,
  type TagReport,
  type TagReportPeriod,
} from '../lib/api'
import { formatDate } from '../lib/format'
import { reportOwnerFromParams, reportSideFromParams } from '../lib/reportParams'

const toPeriod = (allTime: boolean, from: string, to: string): TagReportPeriod =>
  allTime ? 'all' : from || to ? { from, to } : 'default'

function describePeriod(from: string | null, to: string | null): string {
  if (from && to) return `${formatDate(from)} – ${formatDate(to)}`
  if (from) return `from ${formatDate(from)}`
  if (to) return `until ${formatDate(to)}`
  return 'All time'
}

/**
 * Tag report: a Tag's Income and Expense by Category in CHF ("what did we spend on food on the Italy holiday?").
 * Tag in the path, Owner/side/period in the URL (?owner=family&side=income&from=2026-07-01&to=2026-07-14, or
 * ?period=all). Without a period the Tag's date range is used, or all time when it has none.
 */
export function TagReportPage() {
  const { tagId } = useParams()
  const navigate = useNavigate()
  const [params, setParams] = useSearchParams()
  const owner = reportOwnerFromParams(params)
  const side = reportSideFromParams(params)
  const from = params.get('from') ?? ''
  const to = params.get('to') ?? ''
  const allTime = params.get('period') === 'all'
  const period = toPeriod(allTime, from, to)
  const key = `${tagId}|${owner}|${allTime}|${from}..${to}`

  const [tags, setTags] = useState<Tag[] | null>(null)
  const [loaded, setLoaded] = useState<{ key: string; report: TagReport } | null>(null)
  // The request whose load failed (so changing the Tag, Owner or period clears the error).
  const [failed, setFailed] = useState<{ key: string; notFound: boolean } | null>(null)
  const [drill, setDrill] = useState<string | null>(null)

  useEffect(() => {
    const controller = new AbortController()
    listTags(controller.signal)
      .then(setTags)
      .catch(() => {
        if (!controller.signal.aborted) setTags([])
      })
    return () => controller.abort()
  }, [])

  useEffect(() => {
    if (!tagId) return
    const controller = new AbortController()
    const requestKey = `${tagId}|${owner}|${allTime}|${from}..${to}`
    getTagReport(tagId, owner, toPeriod(allTime, from, to), controller.signal)
      .then(report => setLoaded({ key: requestKey, report }))
      .catch((err: unknown) => {
        if (!controller.signal.aborted)
          setFailed({ key: requestKey, notFound: err instanceof ApiError && err.status === 404 })
      })
    return () => controller.abort()
  }, [tagId, owner, allTime, from, to])

  const shown = loaded?.key === key ? loaded.report : null
  const error = failed?.key === key ? failed : null

  const update = (changes: Record<string, string | null>) => {
    const next = new URLSearchParams(params)
    for (const [name, value] of Object.entries(changes)) {
      if (value === null || value === '') next.delete(name)
      else next.set(name, value)
    }
    setParams(next)
    setDrill(null)
  }

  const setBound = (name: 'from' | 'to', value: string) => {
    // Editing one bound of the default (or all-time) period starts from what is shown now.
    const shownFrom = shown?.from ?? ''
    const shownTo = shown?.to ?? ''
    update({
      period: null,
      from: name === 'from' ? value : from || (period === 'default' ? shownFrom : ''),
      to: name === 'to' ? value : to || (period === 'default' ? shownTo : ''),
    })
  }

  const tag = shown?.tag ?? tags?.find(t => t.id === tagId)
  const hasRange = Boolean(tag?.dateFrom && tag?.dateTo)

  return (
    <>
      <h1>Tag report</h1>
      <div className="report-controls">
        <SelectField
          label="Tag"
          value={tagId ?? ''}
          onChange={e =>
            navigate({ pathname: tagReportPath(e.target.value), search: owner === 'family' ? '?owner=family' : '' })
          }
        >
          {!tagId && <option value="">Choose a Tag…</option>}
          {tags?.map(t => (
            <option key={t.id} value={t.id}>
              {t.name}
            </option>
          ))}
        </SelectField>
        <ReportOwnerToggle owner={owner} onChange={o => update({ owner: o })} />
      </div>

      {!tagId && tags !== null && (
        <p>{tags.length === 0 ? 'There are no Tags yet. Create one under Settings › Tags.' : 'Choose a Tag to see its totals.'}</p>
      )}

      {tagId && (
        <div className="report-controls tag-report-period">
          <FormField
            label="From"
            type="date"
            value={from || (period === 'default' ? (shown?.from ?? '') : '')}
            onChange={e => setBound('from', e.target.value)}
          />
          <FormField
            label="To"
            type="date"
            value={to || (period === 'default' ? (shown?.to ?? '') : '')}
            onChange={e => setBound('to', e.target.value)}
          />
          <div className="segmented" role="group" aria-label="Period">
            {hasRange && (
              <button type="button" aria-pressed={period === 'default'} onClick={() => update({ period: null, from: null, to: null })}>
                Tag’s dates
              </button>
            )}
            <button
              type="button"
              aria-pressed={period === 'all' || (period === 'default' && !hasRange)}
              onClick={() => update({ period: hasRange ? 'all' : null, from: null, to: null })}
            >
              All time
            </button>
          </div>
        </div>
      )}

      {error && (
        <p className="status status-bad">{error.notFound ? 'This Tag doesn’t exist any more.' : 'Couldn’t load the report.'}</p>
      )}
      {tagId && shown === null && !error && <p>Loading…</p>}
      {shown !== null && (
        <>
          <p className="report-note">
            <span className="tag-chip">{shown.tag.name}</span> · {describePeriod(shown.from, shown.to)}
          </p>
          <ReportTotals report={shown} />
          <section className="card">
            <ReportSideToggle side={side} onChange={s => update({ side: s })} />
            <ReportBreakdown
              section={shown[side]}
              emptyText={side === 'expense' ? 'No Expenses with this Tag.' : 'No Income with this Tag.'}
              drill={drill}
              onDrill={setDrill}
            />
          </section>
        </>
      )}
    </>
  )
}
