/*
 * <frankie-cow>: Frankie the money-box cow as a self-contained web component.
 *
 *   <frankie-cow mood="neutral|happy|very-happy|sad" size="160"></frankie-cow>
 *
 *   mood  neutral (default) | happy | very-happy | sad; live, switches smoothly
 *   size  width of the drawn cow in px (default 160); height = size * 250/252
 *
 * Room for very-happy: the cow is drawn `size` px wide, centred in the element's
 * box. During the run it trots out of that box and is clipped at the element's
 * left and right edges (vertical overflow stays visible: the heel kick rises a
 * little above the cow). By default the element shrinks to `size`, so the run
 * would vanish at once. The host gives it room by making the element wider,
 * about 2.2 x size or more, e.g. `width: 100%` in a wide strip, plus a little
 * headroom above. The run distance is measured from the element's width.
 *
 * Under prefers-reduced-motion each mood is a still pose (very-happy stands).
 * The run's timers stop while the element is detached or the page is hidden.
 *
 * Use defineFrankie() to register the element (idempotent).
 */
import { drawFrankie } from './frankieArt'
import { FRANKIE_CSS } from './frankieStyles'

export const FRANKIE_MOODS = ['neutral', 'happy', 'very-happy', 'sad'] as const
export type FrankieMood = (typeof FRANKIE_MOODS)[number]

export const FRANKIE_TAG = 'frankie-cow'
const DEFAULT_SIZE = 160

const MOOD_LABELS: Record<FrankieMood, string> = {
  neutral: 'Frankie the cow, chewing calmly',
  happy: 'Frankie the cow, happy',
  'very-happy': 'Frankie the cow, very happy',
  sad: 'Frankie the cow, sad',
}

const isMood = (v: string | null): v is FrankieMood => FRANKIE_MOODS.includes(v as FrankieMood)

const reducedMotionQuery = () => window.matchMedia('(prefers-reduced-motion: reduce)')

/* ---------- tiny tween engine ----------
   The run writes inline transforms from a clock it owns: requestAnimationFrame,
   plus a timer fallback for when no frames are produced (headless renders with
   virtual time). Frames are partial {x, y, r, sx, sy, o} at an offset 0..1. */

type Ease = (p: number) => number

function bezier(x1: number, y1: number, x2: number, y2: number): Ease {
  const cx = 3 * x1, bx = 3 * (x2 - x1) - cx, ax = 1 - cx - bx
  const cy = 3 * y1, by = 3 * (y2 - y1) - cy, ay = 1 - cy - by
  const fx = (t: number) => ((ax * t + bx) * t + cx) * t
  return (x) => {
    let lo = 0, hi = 1, t = x
    for (let i = 0; i < 24; i++) {
      const v = fx(t)
      if (Math.abs(v - x) < 1e-4) break
      if (v < x) lo = t
      else hi = t
      t = (lo + hi) / 2
    }
    return ((ay * t + by) * t + cy) * t
  }
}

const EASE = {
  linear: (p: number) => p,
  in: bezier(0.42, 0, 1, 1),
  out: bezier(0, 0, 0.58, 1),
  inOut: bezier(0.42, 0, 0.58, 1),
  trotOff: bezier(0.4, 0, 0.8, 0.6),
  trotHome: bezier(0.3, 0.4, 0.4, 1),
} satisfies Record<string, Ease>

type Pose = { x: number, y: number, r: number, sx: number, sy: number, o?: number }
type Frame = Partial<Pose> & { offset?: number }
type KeyFrame = Frame & { offset: number }

const REST: Pose = { x: 0, y: 0, r: 0, sx: 1, sy: 1 }

function sample(frames: KeyFrame[], p: number): Pose {
  let i = 1
  while (i < frames.length - 1 && frames[i].offset < p) i++
  const a = frames[i - 1], b = frames[i]
  const k = b.offset === a.offset ? 1 : Math.min(1, Math.max(0, (p - a.offset) / (b.offset - a.offset)))
  const lerp = (key: 'x' | 'y' | 'r' | 'sx' | 'sy') => {
    const va = a[key] ?? REST[key], vb = b[key] ?? REST[key]
    return va + (vb - va) * k
  }
  const pose: Pose = { x: lerp('x'), y: lerp('y'), r: lerp('r'), sx: lerp('sx'), sy: lerp('sy') }
  if (a.o !== undefined && b.o !== undefined) pose.o = a.o + (b.o - a.o) * k
  return pose
}

function applyPose(el: Element, v: Pose) {
  const style = (el as HTMLElement | SVGElement).style
  style.transform = `translate(${v.x}px, ${v.y}px) rotate(${v.r}deg) scale(${v.sx}, ${v.sy})`
  if (v.o !== undefined) style.opacity = String(v.o)
}

type Tween = {
  el: Element
  dur: number
  frames: KeyFrame[]
  ease: Ease
  iterations: number
  start: number
  resolve: (finished: boolean) => void
}

class Tweener {
  #tweens: Tween[] = []
  #waits = new Map<number, (finished: boolean) => void>()
  #raf = 0
  #interval = 0
  #lastTick = 0

  start() {
    if (this.#interval) return
    const loop = () => {
      this.#tick()
      this.#raf = requestAnimationFrame(loop)
    }
    this.#raf = requestAnimationFrame(loop)
    this.#interval = window.setInterval(() => {
      if (performance.now() - this.#lastTick > 40) this.#tick()
    }, 16)
  }

  /** Stops the clock and settles every pending tween and wait with `false`. */
  stop() {
    cancelAnimationFrame(this.#raf)
    clearInterval(this.#interval)
    this.#raf = 0
    this.#interval = 0
    for (const [id, resolve] of this.#waits) {
      clearTimeout(id)
      resolve(false)
    }
    this.#waits.clear()
    const tweens = this.#tweens
    this.#tweens = []
    tweens.forEach((t) => t.resolve(false))
  }

  add(el: Element, ms: number, frames: Frame[], ease: Ease = EASE.linear, iterations = 1): Promise<boolean> {
    const n = frames.length - 1
    const keyed = frames.map((f, i) => ({ ...f, offset: f.offset ?? (n ? i / n : 1) }))
    return new Promise((resolve) => {
      this.#tweens.push({ el, dur: Math.max(1, ms), frames: keyed, ease, iterations, start: performance.now(), resolve })
      this.#tick()
    })
  }

  wait(ms: number): Promise<boolean> {
    return new Promise((resolve) => {
      const id = window.setTimeout(() => {
        this.#waits.delete(id)
        resolve(true)
      }, ms)
      this.#waits.set(id, resolve)
    })
  }

  #tick() {
    const now = performance.now()
    this.#lastTick = now
    this.#tweens = this.#tweens.filter((t) => {
      const progress = Math.max(0, (now - t.start) / t.dur)
      const done = progress >= t.iterations
      applyPose(t.el, sample(t.frames, t.ease(done ? 1 : progress % 1)))
      if (done) t.resolve(true)
      return !done
    })
  }
}

/* ---------- very-happy run: "heel kick" ----------
   Stand up, two spring-pasture bucks, turn and trot off right, peek back in from
   the edge, then trot back and sit. One Run per pass; a cancelled pass throws
   CANCELLED out of whatever step it is in. */

const CANCELLED = Symbol('cancelled')

type Stage = {
  tw: Tweener
  svg: SVGSVGElement
  mover: HTMLElement
  laneWidth: number
  size: number
  isCurrent: () => boolean
}

class Run {
  readonly #s: Stage
  readonly #sit: Element
  readonly #hop: Element
  readonly #flip: Element
  readonly #ground: Element
  readonly #frontLegs: Element[]
  readonly #backLegs: Element[]
  /** Distance from centre at which the cow is fully outside the element. */
  readonly #away: number
  #face = 1 // 1 = facing left (as drawn), -1 = right
  #x = 0

  constructor(stage: Stage) {
    this.#s = stage
    const q = (sel: string) => {
      const el = stage.svg.querySelector(sel)
      if (!el) throw new Error(`Frankie drawing is missing ${sel}`)
      return el
    }
    this.#sit = q('.sit')
    this.#hop = q('.stand-hop')
    this.#flip = q('.stand-flip')
    this.#ground = q('.ground')
    this.#frontLegs = [...stage.svg.querySelectorAll('.stand .lf, .stand .lf2')]
    this.#backLegs = [...stage.svg.querySelectorAll('.stand .lb, .stand .lb2')]
    this.#away = stage.laneWidth / 2 + stage.size / 2 + 12
  }

  async perform() {
    await this.#standUp()
    await this.#wait(150)
    await this.#kick()
    await this.#kick()
    await this.#turn()
    this.#trot(true)
    await this.#move(this.#away, 1300, EASE.trotOff)
    this.#trot(false)
    await this.#wait(600)
    this.#setFace(1)
    this.#place(this.#away)
    this.#trot(true)
    await this.#move(this.#s.laneWidth / 2 + this.#s.size * 0.06, 600, EASE.out) // only head and front legs show
    this.#trot(false)
    await this.#bounce()
    await this.#wait(700)
    this.#trot(true)
    await this.#move(0, 1300, EASE.trotHome)
    this.#trot(false)
    await this.#plop()
  }

  #check() {
    if (!this.#s.isCurrent()) throw CANCELLED
  }

  #fire(el: Element, ms: number, frames: Frame[], ease?: Ease) {
    return this.#s.tw.add(el, ms, frames, ease)
  }

  async #play(el: Element, ms: number, frames: Frame[], ease?: Ease, iterations?: number) {
    await this.#s.tw.add(el, ms, frames, ease, iterations)
    this.#check()
  }

  async #wait(ms: number) {
    await this.#s.tw.wait(ms)
    this.#check()
  }

  #set(el: Element, frame: Frame) {
    applyPose(el, { ...REST, ...frame })
  }

  #move(x: number, ms: number, ease: Ease) {
    const from = this.#x
    this.#x = x
    return this.#play(this.#s.mover, ms, [{ x: from }, { x }], ease)
  }

  #place(x: number) {
    this.#x = x
    this.#set(this.#s.mover, { x })
  }

  #setFace(f: number) {
    this.#face = f
    this.#set(this.#flip, { sx: f })
  }

  #trot(on: boolean) {
    this.#s.svg.classList.toggle('gait-trot', on)
  }

  async #standUp() {
    await this.#play(this.#sit, 200, [{}, { sx: 1.07, sy: 0.88 }], EASE.in)
    this.#s.svg.classList.add('standing')
    this.#set(this.#sit, {})
    await this.#play(this.#hop, 480, [
      { sx: 1.08, sy: 0.84 },
      { y: -16, sx: 0.96, sy: 1.06, offset: 0.45 },
      { sx: 1.03, sy: 0.97, offset: 0.8 },
      {},
    ], EASE.out)
  }

  #bounce() {
    return this.#play(this.#hop, 300, [
      {}, { y: -9, sx: 0.98, sy: 1.03, offset: 0.4 }, { sx: 1.04, sy: 0.95, offset: 0.8 }, {},
    ], EASE.inOut)
  }

  async #turn(ms = 280) {
    const f = this.#face
    this.#face = -f
    void this.#fire(this.#hop, ms, [{}, { y: -14, sy: 1.04 }, {}], EASE.inOut)
    // squash to half width, swap facing at the top of the hop, expand: reads as a
    // quick spin and never shows the paper-thin sliver of a plain scaleX flip
    await this.#play(this.#flip, ms, [
      { sx: f }, { sx: f * 0.5, sy: 1.04, offset: 0.5 }, { sx: -f * 0.5, sy: 1.04, offset: 0.5 }, { sx: -f },
    ])
  }

  async #kick() {
    // spring-pasture buck: rear end up, back legs kick out behind, front legs tuck
    const r = -14 * this.#face
    const ms = 760, ease = EASE.inOut
    this.#backLegs.forEach((l) => void this.#fire(l, ms, [
      {}, { r: 8, offset: 0.22 }, { r: -62, offset: 0.55 }, { offset: 0.85 }, {},
    ], ease))
    this.#frontLegs.forEach((l) => void this.#fire(l, ms, [
      {}, { offset: 0.22 }, { r: 22, offset: 0.5 }, { offset: 0.85 }, {},
    ], ease))
    void this.#fire(this.#ground, ms, [{ o: 0.2 }, { sx: 0.7, sy: 0.7, o: 0.1, offset: 0.55 }, { o: 0.2 }], ease)
    await this.#play(this.#hop, ms, [
      {},
      { sx: 1.05, sy: 0.92, offset: 0.22 },
      { y: -38, r, offset: 0.55 },
      { sx: 1.06, sy: 0.93, offset: 0.85 },
      {},
    ], ease)
  }

  async #plop() {
    await this.#play(this.#hop, 140, [{}, { sx: 1.05, sy: 0.9 }], EASE.in)
    this.#s.svg.classList.remove('standing')
    this.#set(this.#hop, {})
    this.#setFace(1)
    await this.#play(this.#sit, 520, [
      { y: -10, sx: 0.95, sy: 1.07 },
      { sx: 1.1, sy: 0.86, offset: 0.35 },
      { sx: 0.98, sy: 1.03, offset: 0.7 },
      {},
    ], EASE.out)
  }
}

/* ---------- the element ---------- */

let instances = 0

export class FrankieElement extends HTMLElement {
  static observedAttributes = ['mood', 'size']

  readonly #lane: HTMLElement
  readonly #mover: HTMLElement
  readonly #svg: SVGSVGElement
  readonly #tw = new Tweener()
  #appliedMood: FrankieMood | null = null
  #runToken = 0
  #popTimer = 0
  #motionQuery: MediaQueryList | null = null

  constructor() {
    super()
    const root = this.attachShadow({ mode: 'open' })
    root.innerHTML = `<style>${FRANKIE_CSS}</style><div class="lane"><div class="mover" part="frankie">${drawFrankie(`frankie${++instances}`)}</div></div>`
    this.#lane = root.querySelector('.lane')!
    this.#mover = root.querySelector('.mover')!
    this.#svg = root.querySelector('svg')!
  }

  get mood(): FrankieMood {
    const m = this.getAttribute('mood')
    return isMood(m) ? m : 'neutral'
  }
  set mood(value: FrankieMood) {
    this.setAttribute('mood', value)
  }

  get size(): number {
    const s = parseFloat(this.getAttribute('size') ?? '')
    return s > 0 ? s : DEFAULT_SIZE
  }
  set size(value: number) {
    this.setAttribute('size', String(value))
  }

  connectedCallback() {
    document.addEventListener('visibilitychange', this.#onVisibility)
    this.#motionQuery = reducedMotionQuery()
    this.#motionQuery.addEventListener('change', this.#onMotionPreference)
    this.#sync()
  }

  disconnectedCallback() {
    document.removeEventListener('visibilitychange', this.#onVisibility)
    this.#motionQuery?.removeEventListener('change', this.#onMotionPreference)
    this.#motionQuery = null
    this.#stopRun()
    clearTimeout(this.#popTimer)
    this.#svg.classList.remove('pop')
    this.#appliedMood = null // re-applied without a pop when reattached
  }

  attributeChangedCallback() {
    if (this.isConnected) this.#sync()
  }

  #sync() {
    this.#mover.style.width = `${this.size}px`
    const mood = this.mood
    if (mood === this.#appliedMood) return
    const animate = this.#appliedMood !== null
    this.#appliedMood = mood
    this.#applyMood(mood, animate)
    this.#restartRun()
  }

  #applyMood(mood: FrankieMood, pop: boolean) {
    const svg = this.#svg
    svg.classList.toggle('m-happy', mood === 'happy' || mood === 'very-happy') // very-happy sits happily between runs
    svg.classList.toggle('m-very-happy', mood === 'very-happy')
    svg.classList.toggle('m-neutral', mood === 'neutral')
    svg.classList.toggle('m-sad', mood === 'sad')
    svg.setAttribute('aria-label', MOOD_LABELS[mood])
    if (!pop) return
    svg.classList.remove('pop')
    void svg.getBoundingClientRect() // restart the pop animation
    svg.classList.add('pop')
    clearTimeout(this.#popTimer)
    this.#popTimer = window.setTimeout(() => svg.classList.remove('pop'), 550)
  }

  #onVisibility = () => {
    if (document.hidden) this.#stopRun()
    else this.#restartRun()
  }

  #onMotionPreference = () => this.#restartRun()

  #restartRun() {
    this.#stopRun()
    if (this.#appliedMood !== 'very-happy' || document.hidden) return
    if (reducedMotionQuery().matches) {
      this.#svg.classList.add('standing')
      return
    }
    this.#tw.start()
    void this.#loop(this.#runToken)
  }

  #stopRun() {
    this.#runToken++
    this.#tw.stop()
    this.#clearInline()
    this.#svg.classList.remove('standing', 'gait-trot')
  }

  #clearInline() {
    this.#mover.removeAttribute('style')
    this.#mover.style.width = `${this.size}px`
    this.#svg.querySelectorAll('.sit, .stand-hop, .stand-flip, .ground, .stand .leg')
      .forEach((el) => el.removeAttribute('style'))
  }

  async #loop(token: number) {
    const isCurrent = () => token === this.#runToken
    let first = true
    try {
      while (isCurrent()) {
        // sitting first: the happy wiggle and bounces play from CSS
        await this.#tw.wait(first ? 1800 : 2600)
        if (!isCurrent()) return
        first = false
        const stage: Stage = {
          tw: this.#tw,
          svg: this.#svg,
          mover: this.#mover,
          laneWidth: Math.max(this.#lane.getBoundingClientRect().width, this.size),
          size: this.size,
          isCurrent,
        }
        await new Run(stage).perform()
        this.#clearInline() // everything ends at rest
      }
    } catch (e) {
      if (e !== CANCELLED) throw e
    }
  }
}

/** Registers <frankie-cow> once; safe to call any number of times. */
export function defineFrankie() {
  if (!customElements.get(FRANKIE_TAG)) customElements.define(FRANKIE_TAG, FrankieElement)
}

declare global {
  interface HTMLElementTagNameMap {
    'frankie-cow': FrankieElement
  }
}
