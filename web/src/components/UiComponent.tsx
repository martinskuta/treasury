/*
 * A UI component: the flat panel pages are made of (never call it a "card").
 *
 *   <UiComponent title="This month">…</UiComponent>
 *   <UiComponent title="Accounts" actions={<Link className="button-link" to="/accounts">All</Link>} className="a-accounts">…</UiComponent>
 *   <UiComponent>…</UiComponent>                     // no title row
 *
 * Renders <section class="ui-component"> with an h2 title row. Plain markup with className="ui-component" is fine
 * too. Put several in a .ui-grid (set grid-template-columns) or .stack to get the standard gap.
 */
import type { HTMLAttributes, ReactNode } from 'react'

type Props = Omit<HTMLAttributes<HTMLElement>, 'title'> & {
  /** Title shown at the top (h2). */
  title?: ReactNode
  /** Shown at the right end of the title row (a link, a segmented control, a muted date range…). */
  actions?: ReactNode
  children?: ReactNode
}

export function UiComponent({ title, actions, className, children, ...rest }: Props) {
  return (
    <section className={className ? `ui-component ${className}` : 'ui-component'} {...rest}>
      {(title !== undefined || actions !== undefined) && (
        <div className="ui-component-title">
          <h2>{title}</h2>
          {actions}
        </div>
      )}
      {children}
    </section>
  )
}
