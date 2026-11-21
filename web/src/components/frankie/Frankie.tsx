import type { CSSProperties, DetailedHTMLProps, HTMLAttributes } from 'react'
import { defineFrankie, type FrankieElement, type FrankieMood } from './frankieElement'

export type { FrankieMood } from './frankieElement'

defineFrankie()

type Props = {
  /** Which mood to show; changing it at runtime switches smoothly. Default `neutral`. */
  mood?: FrankieMood
  /** Width of the drawn cow in px (height follows the drawing). Default 160. */
  size?: number
  /**
   * Size the element itself here. For `very-happy` give it room for the run:
   * about 2.2 x `size` wide (or `width: 100%` in a wide strip) and a little
   * headroom above; the cow stays centred and is clipped at the left and right
   * edges of this box.
   */
  className?: string
  style?: CSSProperties
}

/** Frankie the cow, Treasury's mascot. A thin wrapper around `<frankie-cow>`. */
export function Frankie({ mood = 'neutral', size, className, style }: Props) {
  return <frankie-cow mood={mood} size={size} className={className} style={style} />
}

type FrankieCowProps = DetailedHTMLProps<HTMLAttributes<FrankieElement>, FrankieElement> & {
  mood?: FrankieMood
  size?: number
}

declare module 'react' {
  namespace JSX {
    interface IntrinsicElements {
      'frankie-cow': FrankieCowProps
    }
  }
}
