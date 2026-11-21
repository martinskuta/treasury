/*
 * Frankie's drawing: the Swiss money-box cow as one SVG string.
 *
 * Ported from the v4 prototype (branch prototype/design-refresh,
 * samples/ui/prototypes/frankie-v4.js), Swiss look only. The drawing is fixed by
 * the design guideline: change shapes or colours only as a design decision.
 *
 * The SVG holds two poses: `.sit` (the normal, front-ish sitting pose with the
 * mood details) and `.stand` (side view, used by the very-happy run). Class
 * names are the hooks for frankieStyles.ts and the run in frankieElement.ts.
 * Colours are Frankie's own and are deliberately not app tokens.
 */

/** viewBox size of the drawing; the element keeps this aspect ratio. */
export const ART_WIDTH = 252
export const ART_HEIGHT = 250

const SLOT_X = 174, SLOT_Y = 115 // money-box slot on top of the back (sitting)
const PLUG_X = 138, PLUG_Y = 205 // bottom plug on the belly underside (sitting)
// standing pose (side view, facing left)
const SB = { cx: 150, cy: 142, rx: 74, ry: 44 }
const SSLOT_X = 168, SSLOT_Y = 101
const SPLUG_X = 146, SPLUG_Y = 185

const P = {
  outline: '#4A3226',
  body: [['0', '#FFFFFF'], ['.55', '#FBF8F1'], ['1', '#E2D8C6']],
  muzzle: [['0', '#FCD3CB'], ['1', '#F0A398']],
  nostril: '#B8665E',
  innerEar: '#F4B3AA',
  horn: [['0', '#FFF9EA'], ['1', '#E3D2AA']],
  tuft: '#714D3A',
  hoof: '#5A3B2C',
  slot: '#2E211B',
  lip: '#FFFDF7',
  plug: '#F4A7A0',
  plugDk: '#D9837A',
  shade: '#C9BBA2',
  shadeOpacity: 0.35,
  far: '#E3D8C5',
  mouthFill: '#7A2F2B',
} as const

const SW = 3 // master stroke weight
const O = P.outline

const THIGH = 'M166 205 C158 180, 170 152, 197 150 C219 149, 230 170, 225 194 C223 202, 219 207, 214 209 L200 212 L166 212 Z'
const GRASS = ['#8CC63F', '#C6E36B', '#8CC63F', '#C6E36B', '#8CC63F', '#C6E36B']

const stops = (arr: readonly (readonly [string, string])[]) =>
  arr.map(([o, c]) => `<stop offset="${o}" stop-color="${c}"/>`).join('')

function coin(id: string) {
  return `
    <circle r="11" fill="url(#coin-${id})" stroke="#946410" stroke-width="2"/>
    <circle r="7.8" fill="none" stroke="#C08D1C" stroke-width="1.2"/>
    <text y="2.9" text-anchor="middle" font-family="Georgia, 'Times New Roman', serif"
          font-weight="700" font-size="8" fill="#7A4E0A">Fr.</text>
    <path d="M-6.5 -4.5 A8 8 0 0 1 1 -8.2" fill="none" stroke="#FFF7D6" stroke-width="1.6"
          stroke-linecap="round" opacity=".9"/>`
}

/* ---------- shared parts (drawn in sitting-head coordinates) ---------- */

function headSvg(id: string) {
  return `
  <g class="head-pose"><g class="head">
    <g class="ear-l-pose"><g class="ear-l">
      <path d="M50 94 C36 86, 14 92, 6 106 C20 114, 40 112, 50 106 Z" fill="url(#body-${id})" stroke="${O}" stroke-width="${SW}" stroke-linejoin="round"/>
      <path d="M42 98.5 C32 95, 21 98, 15 105 C24 108.5, 35 108, 42 104 Z" fill="${P.innerEar}"/>
    </g></g>
    <g class="ear-r-pose"><g class="ear-r">
      <path d="M114 94 C128 86, 150 92, 158 106 C144 114, 124 112, 114 106 Z" fill="url(#body-${id})" stroke="${O}" stroke-width="${SW}" stroke-linejoin="round"/>
      <path d="M122 98.5 C132 95, 143 98, 149 105 C140 108.5, 129 108, 122 104 Z" fill="${P.innerEar}"/>
    </g></g>
    <g fill="url(#horn-${id})" stroke="${O}" stroke-width="2.6" stroke-linejoin="round">
      <path d="M60 84 C53 75, 51 64, 56 55 C63 61, 69 69, 73 79 Z"/>
      <path d="M104 84 C111 75, 113 64, 108 55 C101 61, 95 69, 91 79 Z"/>
    </g>
    <ellipse cx="82" cy="116" rx="48" ry="42" fill="url(#body-${id})" stroke="${O}" stroke-width="${SW}"/>
    <path clip-path="url(#clip-head-${id})" fill="url(#patch-${id})"
          d="M90 84 C102 74, 124 80, 130 96 C134 110, 122 120, 108 118 C94 116, 84 104, 86 94 Z"/>
    <ellipse cx="82" cy="116" rx="48" ry="42" fill="none" stroke="${O}" stroke-width="${SW}"/>
    <path d="M72 79 C71 68, 80 64, 84 72 C87 64, 97 67, 94 77 C87 74, 78 76, 72 79 Z"
          fill="${P.tuft}" stroke="${O}" stroke-width="2.4" stroke-linejoin="round"/>

    <g class="muzzle">
      <ellipse cx="82" cy="149" rx="37" ry="23" fill="url(#muzzle-${id})" stroke="${O}" stroke-width="${SW}"/>
      <ellipse cx="70" cy="146" rx="4" ry="5.6" transform="rotate(-18 70 146)" fill="${P.nostril}"/>
      <ellipse cx="94" cy="146" rx="4" ry="5.6" transform="rotate(18 94 146)" fill="${P.nostril}"/>
      <g class="mouth-g" fill="none" stroke="${O}" stroke-width="2.6" stroke-linecap="round" stroke-linejoin="round">
        <path class="mouth mouth-happy" d="M71 158.5 Q82 173 93 158.5 Q82 163 71 158.5 Z" fill="${P.mouthFill}"/>
        <g class="mouth mouth-neutral">
          <path d="M89 161.5 C95 157, 100 153.5, 106 151" stroke="#5E8F22" stroke-width="3.6"/>
          <path d="M89 161.5 C95 157, 100 153.5, 106 151" stroke="#8CC63F" stroke-width="1.8"/>
          <path d="M89 162.5 C96 162, 101 164, 105 168" stroke="#5E8F22" stroke-width="3.2"/>
          <path d="M89 162.5 C96 162, 101 164, 105 168" stroke="#C6E36B" stroke-width="1.5"/>
          <path d="M73 161 Q82 166 91 161.5"/>
        </g>
        <path class="mouth mouth-sad" d="M73 166 Q82 158 91 166"/>
      </g>
    </g>

    <g class="cheeks" fill="#F46060">
      <ellipse cx="45" cy="130" rx="7.5" ry="4.5" opacity=".38"/>
      <ellipse cx="119" cy="130" rx="7.5" ry="4.5" opacity=".38"/>
    </g>
    <g class="eyes-open">
      <g class="eye eye-l">
        <ellipse cx="64" cy="110" rx="11.5" ry="13.5" fill="#FFFFFF" stroke="${O}" stroke-width="2.5"/>
        <g class="pupils">
          <circle cx="65.5" cy="112" r="8" fill="#2B201B"/>
          <circle cx="62.5" cy="108" r="3.2" fill="#FFFFFF"/>
          <circle cx="68.6" cy="115.6" r="1.5" fill="#FFFFFF"/>
        </g>
      </g>
      <g class="eye eye-r">
        <ellipse cx="100" cy="110" rx="11.5" ry="13.5" fill="#FFFFFF" stroke="${O}" stroke-width="2.5"/>
        <g class="pupils">
          <circle cx="98.5" cy="112" r="8" fill="#2B201B"/>
          <circle cx="95.5" cy="108" r="3.2" fill="#FFFFFF"/>
          <circle cx="101.6" cy="115.6" r="1.5" fill="#FFFFFF"/>
        </g>
      </g>
    </g>
    <g class="eyes-arc" fill="none" stroke="#2B201B" stroke-width="3.6" stroke-linecap="round">
      <path d="M54 115 Q64 100 74 115"/>
      <path d="M90 115 Q100 100 110 115"/>
    </g>
    <g class="brows" fill="none" stroke="${O}" stroke-width="2.6" stroke-linecap="round">
      <path d="M53 92 Q61 90.5 70 85.5"/>
      <path d="M111 92 Q103 90.5 94 85.5"/>
    </g>
    <ellipse cx="66" cy="140" rx="7" ry="2.6" transform="rotate(-18 66 140)" fill="#FFFFFF" opacity=".55"/>
    <g class="tear-g">
      <path class="tear" d="M55 121 C51.5 126.5, 50.5 129.5, 55 132 C59.5 129.5, 58.5 126.5, 55 121 Z"
            fill="#95C8CE" stroke="#5E9BA2" stroke-width="1.2"/>
    </g>
  </g></g>`
}

function collarSvg() {
  return `
  <path d="M58 160 Q82 196 106 160" fill="none" stroke="${O}" stroke-width="10.5" stroke-linecap="round"/>
  <path d="M58 160 Q82 196 106 160" fill="none" stroke="#F46060" stroke-width="6.5" stroke-linecap="round"/>
  <path d="M64 167 Q82 188 100 167" fill="none" stroke="#FFFFFF" stroke-width="1.4" stroke-dasharray="2 4" stroke-linecap="round" opacity=".7"/>`
}

function bellSvg(id: string) {
  return `
  <g class="bell-pose">
    <g class="jingle" fill="none" stroke="#C99212" stroke-width="2.2" stroke-linecap="round">
      <path d="M67 193 L62.5 190.5 M66.5 199 L61.5 199.5"/>
      <path d="M97 193 L101.5 190.5 M97.5 199 L102.5 199.5"/>
    </g>
    <g class="bell">
      <circle cx="82" cy="181" r="3.4" fill="none" stroke="${O}" stroke-width="2.2"/>
      <path d="M72 199.5 C72 189, 76 184.5, 82 184.5 C88 184.5, 92 189, 92 199.5 L95.5 204 L68.5 204 Z"
            fill="url(#bell-${id})" stroke="${O}" stroke-width="2.6" stroke-linejoin="round"/>
      <path d="M71.6 198.5 H92.4" stroke="#A97412" stroke-width="1.6"/>
      <circle cx="82" cy="206" r="3" fill="#A97412" stroke="${O}" stroke-width="1.8"/>
      <path d="M76 196 C76 191, 77.5 188.5, 80.5 187.5" fill="none" stroke="#FFFBE6" stroke-width="2" stroke-linecap="round" opacity=".9"/>
    </g>
  </g>`
}

/** One standing leg: rounded column with hoof; its top is hidden behind the body. */
function legSvg(cls: string, x: number, w: number, fill: string) {
  const r = 9, top = 154, bot = 220, mid = x + w / 2
  return `
    <g class="leg ${cls}">
      <g stroke="${O}" stroke-width="3" stroke-linejoin="round">
        <rect x="${x}" y="${top}" width="${w}" height="${bot - top}" rx="${r}" fill="${fill}"/>
        <path d="M${x} 209 H${x + w} V211 A${r} ${r} 0 0 1 ${x + w - r} ${bot} H${x + r} A${r} ${r} 0 0 1 ${x} 211 Z" fill="${P.hoof}"/>
      </g>
      <path d="M${mid} 213 V219.5" stroke="${O}" stroke-width="1.6" stroke-linecap="round"/>
    </g>`
}

function standingSvg(id: string) {
  const body = `<ellipse cx="${SB.cx}" cy="${SB.cy}" rx="${SB.rx}" ry="${SB.ry}"`
  return `
  <g class="stand"><g class="stand-hop"><g class="stand-flip">
    <g class="puffs" fill="#F3ECDC" stroke="${O}" stroke-width="1.6">
      <circle class="puff pf1" cx="214" cy="212" r="8"/>
      <circle class="puff pf2" cx="204" cy="215" r="7"/>
      <circle class="puff pf3" cx="222" cy="208" r="6"/>
    </g>
    <g class="stand-body">
      <g class="s-tail">
        <path d="M216 128 C238 116, 250 138, 241 158" fill="none" stroke="${O}" stroke-width="4.5" stroke-linecap="round"/>
        <path d="M241 154 C250 158, 250 171, 241 176 C234 172, 232 161, 241 154 Z"
              fill="${P.tuft}" stroke="${O}" stroke-width="2.5" stroke-linejoin="round"/>
      </g>
      ${legSvg('lb2', 194, 19, P.far)}
      ${legSvg('lf2', 114, 19, P.far)}
      ${legSvg('lb', 174, 22, `url(#body-${id})`)}
      ${legSvg('lf', 92, 22, `url(#body-${id})`)}

      ${body} fill="url(#body-${id})" stroke="${O}" stroke-width="${SW}"/>
      <g clip-path="url(#clip-sbody-${id})" fill="url(#patch-${id})">
        <path d="M146 104 C164 94, 194 102, 196 120 C198 136, 174 140, 160 134 C146 128, 134 112, 146 104 Z"/>
        <path d="M108 142 C120 132, 142 136, 143 152 C144 168, 126 176, 113 170 C100 164, 98 150, 108 142 Z"/>
        <path d="M200 146 C212 140, 228 148, 228 162 C228 176, 212 182, 203 176 C193 170, 190 154, 200 146 Z"/>
      </g>
      <g clip-path="url(#clip-sbody-${id})">
        <ellipse cx="156" cy="190" rx="92" ry="22" fill="${P.shade}" opacity="${P.shadeOpacity}"/>
      </g>
      ${body} fill="none" stroke="${O}" stroke-width="${SW}"/>
      <path d="M184 134 C204 138, 212 158, 203 178" fill="none" stroke="${O}" stroke-width="2.4" stroke-linecap="round" opacity=".75"/>
      <path d="M188 142 C198 146, 203 154, 204 160" fill="none" stroke="#FFFFFF" stroke-width="2" stroke-linecap="round" opacity=".5"/>

      <ellipse cx="${SSLOT_X}" cy="${SSLOT_Y + 3}" rx="21" ry="7.5" fill="#000" opacity=".12"/>
      <ellipse cx="${SSLOT_X}" cy="${SSLOT_Y}" rx="20" ry="7.2" fill="${P.lip}" stroke="${O}" stroke-width="2.6"/>
      <rect x="${SSLOT_X - 14}" y="${SSLOT_Y - 3.4}" width="28" height="7" rx="3.5" fill="${P.slot}"/>

      <ellipse cx="${SPLUG_X}" cy="${SPLUG_Y}" rx="13" ry="6.2" fill="${P.lip}" stroke="${O}" stroke-width="2.4"/>
      <ellipse cx="${SPLUG_X}" cy="${SPLUG_Y + 1.2}" rx="10" ry="5" fill="${P.plugDk}" stroke="${O}" stroke-width="2.2"/>
      <ellipse cx="${SPLUG_X}" cy="${SPLUG_Y - .2}" rx="10" ry="4.6" fill="${P.plug}" stroke="${O}" stroke-width="2.2"/>
      <ellipse cx="${SPLUG_X}" cy="${SPLUG_Y - .3}" rx="3.6" ry="1.8" fill="${P.plugDk}" stroke="${O}" stroke-width="1.2"/>

      <!-- head, collar and bell: the sitting head at 80% -->
      <g class="s-head-place">
        ${collarSvg()}
        ${headSvg(id)}
        ${bellSvg(id)}
      </g>
    </g>
  </g></g></g>`
}

/**
 * The whole drawing. `id` makes gradient and clip-path ids unique per element,
 * so several Frankies (or a re-render) never reference each other's defs.
 */
export function drawFrankie(id: string): string {
  return `
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ${ART_WIDTH} ${ART_HEIGHT}" role="img" aria-label="Frankie the cow">
  <defs>
    <radialGradient id="body-${id}" cx=".36" cy=".3" r=".8">${stops(P.body)}</radialGradient>
    <radialGradient id="muzzle-${id}" cx=".4" cy=".3" r=".8">${stops(P.muzzle)}</radialGradient>
    <linearGradient id="horn-${id}" x1="0" y1="0" x2="1" y2="1">${stops(P.horn)}</linearGradient>
    <radialGradient id="patch-${id}" cx=".35" cy=".3" r=".8">
      <stop offset="0" stop-color="#87624C"/><stop offset=".6" stop-color="#714D3A"/><stop offset="1" stop-color="#5C3D2D"/>
    </radialGradient>
    <linearGradient id="coin-${id}" x1="0" y1="0" x2="1" y2="1">
      <stop offset="0" stop-color="#FFEBA6"/><stop offset=".5" stop-color="#E0B23A"/><stop offset="1" stop-color="#B07E16"/>
    </linearGradient>
    <radialGradient id="bell-${id}" cx=".35" cy=".3" r=".85">
      <stop offset="0" stop-color="#FFF0B5"/><stop offset=".45" stop-color="#E0B23A"/><stop offset="1" stop-color="#A97412"/>
    </radialGradient>
    <clipPath id="clip-body-${id}"><ellipse cx="138" cy="162" rx="80" ry="56"/></clipPath>
    <clipPath id="clip-sbody-${id}"><ellipse cx="${SB.cx}" cy="${SB.cy}" rx="${SB.rx}" ry="${SB.ry}"/></clipPath>
    <clipPath id="clip-head-${id}"><ellipse cx="82" cy="116" rx="48" ry="42"/></clipPath>
    <clipPath id="clip-slot-${id}"><rect x="100" y="0" width="150" height="${SLOT_Y}"/></clipPath>
    <clipPath id="clip-plug-${id}"><rect x="40" y="${PLUG_Y}" width="200" height="200"/></clipPath>
    <clipPath id="clip-thigh-${id}"><path d="${THIGH}"/></clipPath>
  </defs>

  <ellipse class="ground" cx="136" cy="223" rx="92" ry="7" fill="#000" opacity=".2"/>

  <g class="stage">
   <g class="sit">
    <g class="torso-pose"><g class="torso">
      <g class="tail-pose"><g class="tail">
        <path d="M210 148 C230 138, 240 160, 230 181" fill="none" stroke="${O}" stroke-width="4.5" stroke-linecap="round"/>
        <path d="M230 177 C239 181, 239 194, 230 199 C223 195, 221 184, 230 177 Z"
              fill="${P.tuft}" stroke="${O}" stroke-width="2.5" stroke-linejoin="round"/>
      </g></g>
      <ellipse cx="138" cy="162" rx="80" ry="56" fill="url(#body-${id})" stroke="${O}" stroke-width="${SW}"/>
      <g clip-path="url(#clip-body-${id})" fill="url(#patch-${id})">
        <path d="M134 130 C148 120, 174 122, 180 136 C186 150, 170 160, 154 156 C138 152, 124 142, 134 130 Z"/>
        <path d="M184 160 C198 154, 214 162, 216 176 C216 188, 202 194, 192 188 C182 182, 176 166, 184 160 Z"/>
        <path d="M100 186 C110 176, 132 178, 136 194 C140 208, 124 222, 108 218 C96 214, 92 196, 100 186 Z"/>
      </g>
      <g clip-path="url(#clip-body-${id})">
        <ellipse cx="150" cy="208" rx="92" ry="26" fill="${P.shade}" opacity="${P.shadeOpacity}"/>
        <ellipse cx="84" cy="170" rx="42" ry="12" fill="#000" opacity=".07"/>
      </g>
      <ellipse cx="138" cy="162" rx="80" ry="56" fill="none" stroke="${O}" stroke-width="${SW}"/>

      <!-- back leg: chubby sitting haunch + forward-pointing foot -->
      <path d="${THIGH}" fill="url(#body-${id})"/>
      <g clip-path="url(#clip-thigh-${id})">
        <path fill="url(#patch-${id})" d="M190 150 C206 144, 224 156, 224 172 C224 186, 208 190, 198 184 C186 178, 180 158, 190 150 Z"/>
        <ellipse cx="200" cy="214" rx="34" ry="14" fill="${P.shade}" opacity="${P.shadeOpacity}"/>
      </g>
      <path d="M166 205 C158 180, 170 152, 197 150 C219 149, 230 170, 225 194 C223 202, 219 207, 214 209"
            fill="none" stroke="${O}" stroke-width="${SW}" stroke-linecap="round" stroke-linejoin="round"/>
      <path d="M190 164 C200 160, 210 163, 214 170" fill="none" stroke="#FFFFFF" stroke-width="2"
            stroke-linecap="round" opacity=".5"/>
      <g stroke="${O}" stroke-width="${SW}" stroke-linejoin="round">
        <rect x="158" y="199" width="58" height="21" rx="10.5" fill="url(#body-${id})"/>
        <path d="M171 199 V220 H168.5 A10.5 10.5 0 0 1 158 209.5 A10.5 10.5 0 0 1 168.5 199 Z" fill="${P.hoof}"/>
      </g>
      <path d="M160 209.5 H167" stroke="${O}" stroke-width="1.6" stroke-linecap="round"/>

      <!-- the money-box slot: raised light lip, dark opening -->
      <ellipse cx="${SLOT_X}" cy="${SLOT_Y + 3}" rx="25" ry="8.5" fill="#000" opacity=".12"/>
      <ellipse cx="${SLOT_X}" cy="${SLOT_Y}" rx="24" ry="8.5" fill="${P.lip}" stroke="${O}" stroke-width="2.6"/>
      <path d="M${SLOT_X - 20} ${SLOT_Y + 2.5} Q${SLOT_X} ${SLOT_Y + 9} ${SLOT_X + 20} ${SLOT_Y + 2.5}" fill="none"
            stroke="${P.shade}" stroke-width="2" stroke-linecap="round" opacity=".8"/>
      <rect x="${SLOT_X - 16.5}" y="${SLOT_Y - 4}" width="33" height="8" rx="4" fill="${P.slot}"/>
      <path d="M${SLOT_X - 13} ${SLOT_Y - 2.2} H${SLOT_X + 13}" stroke="#000" stroke-width="1.6" stroke-linecap="round" opacity=".35"/>

      <!-- happy: coins slide into the slot (clipped at the slot line) -->
      <g class="happy-coins" clip-path="url(#clip-slot-${id})">
        <g transform="translate(${SLOT_X} ${SLOT_Y - 11})"><g class="coin hc hc1">${coin(id)}</g></g>
        <g transform="translate(${SLOT_X} ${SLOT_Y - 11})"><g class="coin hc hc2">${coin(id)}</g></g>
        <g transform="translate(${SLOT_X} ${SLOT_Y - 11})"><g class="coin hc hc3">${coin(id)}</g></g>
      </g>
      <!-- front lip edge redrawn over the coins so they go in -->
      <path d="M${SLOT_X - 16.5} ${SLOT_Y + 1.5} Q${SLOT_X} ${SLOT_Y + 5.5} ${SLOT_X + 16.5} ${SLOT_Y + 1.5}" fill="none"
            stroke="${P.lip}" stroke-width="2.2" stroke-linecap="round"/>

      <!-- the bottom plug: rim, hole, escaping coins, stopper -->
      <ellipse cx="${PLUG_X}" cy="${PLUG_Y}" rx="15" ry="7.5" fill="${P.lip}" stroke="${O}" stroke-width="2.4"/>
      <ellipse cx="${PLUG_X}" cy="${PLUG_Y}" rx="10" ry="4.6" fill="${P.slot}"/>
      <g class="sad-coins" clip-path="url(#clip-plug-${id})">
        <g transform="translate(${PLUG_X} ${PLUG_Y - 9})"><g class="coin sc sc1"><g transform="scale(.72)">${coin(id)}</g></g></g>
        <g transform="translate(${PLUG_X} ${PLUG_Y - 9})"><g class="coin sc sc2"><g transform="scale(.72)">${coin(id)}</g></g></g>
        <g transform="translate(${PLUG_X} ${PLUG_Y - 9})"><g class="coin sc sc3"><g transform="scale(.72)">${coin(id)}</g></g></g>
      </g>
      <g class="cap-pose"><g class="cap">
        <ellipse cx="${PLUG_X}" cy="${PLUG_Y + 1.5}" rx="12" ry="6" fill="${P.plugDk}" stroke="${O}" stroke-width="2.4"/>
        <ellipse cx="${PLUG_X}" cy="${PLUG_Y - .2}" rx="12" ry="5.6" fill="${P.plug}" stroke="${O}" stroke-width="2.4"/>
        <ellipse cx="${PLUG_X}" cy="${PLUG_Y - .4}" rx="4.2" ry="2.1" fill="${P.plugDk}" stroke="${O}" stroke-width="1.4"/>
        <path d="M${PLUG_X - 8} ${PLUG_Y - 2} Q${PLUG_X - 4} ${PLUG_Y - 4.6} ${PLUG_X + 1} ${PLUG_Y - 4.4}" fill="none"
              stroke="#FFFFFF" stroke-width="1.4" stroke-linecap="round" opacity=".8"/>
      </g></g>
    </g></g>

    <!-- front legs -->
    <g stroke="${O}" stroke-width="${SW}" stroke-linejoin="round">
      <rect x="54" y="184" width="22" height="36" rx="9" fill="url(#body-${id})"/>
      <path d="M54 209 H76 V211 A9 9 0 0 1 67 220 H63 A9 9 0 0 1 54 211 Z" fill="${P.hoof}"/>
      <rect x="88" y="184" width="22" height="36" rx="9" fill="url(#body-${id})"/>
      <path d="M88 209 H110 V211 A9 9 0 0 1 101 220 H97 A9 9 0 0 1 88 211 Z" fill="${P.hoof}"/>
    </g>
    <path d="M65 213 V219.5 M99 213 V219.5" stroke="${O}" stroke-width="1.6" stroke-linecap="round"/>

    ${collarSvg()}
    ${headSvg(id)}
    ${bellSvg(id)}

    <!-- neutral: pixel grass bits drop from the mouth corner and fade -->
    <g class="grass-bits">
      ${GRASS.map((c, i) => `<rect class="gb gb${i + 1}" x="${92 + (i % 3) * 2}" y="${162 + (i % 2)}" width="${i % 2 ? 4 : 5}" height="${i % 2 ? 4 : 5}"
            fill="${c}" stroke="#4F7A1C" stroke-width=".9"/>`).join('')}
    </g>
   </g>

   ${standingSvg(id)}
  </g>
</svg>`
}
