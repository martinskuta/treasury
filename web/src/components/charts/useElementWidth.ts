import { useLayoutEffect, useRef, useState } from 'react'

/**
 * Width of an element in whole CSS pixels, kept up to date with a ResizeObserver; 0 until measured.
 * Charts draw at their real width (not a scaled viewBox) so text and strokes stay crisp.
 *
 *   const [ref, width] = useElementWidth<HTMLDivElement>()
 *   return <div ref={ref}>{width > 0 && <svg width={width} … />}</div>
 */
export function useElementWidth<T extends HTMLElement>() {
  const ref = useRef<T>(null)
  const [width, setWidth] = useState(0)

  useLayoutEffect(() => {
    const element = ref.current
    if (!element) return
    const measure = () => setWidth(Math.floor(element.clientWidth))
    measure()
    const observer = new ResizeObserver(measure)
    observer.observe(element)
    return () => observer.disconnect()
  }, [])

  return [ref, width] as const
}
