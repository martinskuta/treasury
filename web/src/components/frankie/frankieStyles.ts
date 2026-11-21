/*
 * Frankie's shadow-DOM stylesheet: pivots, mood poses and the CSS loops.
 * The very-happy run itself is driven from frankieElement.ts; this file only
 * holds the gait cycles it switches on. Ported from the v4 prototype.
 *
 * Pivot notes: groups whose pivot is a known point use transform-box: view-box
 * with absolute user-space origins (the head copy inside the standing pose sits
 * in a transformed group, and still pivots correctly because coordinates are
 * local); coins, eyes and the tear use transform-box: fill-box.
 */
export const FRANKIE_CSS = `
:host { display: inline-block; line-height: 0; vertical-align: middle; }
:host([hidden]) { display: none; }
.lane { width: 100%; overflow-x: clip; overflow-y: visible; }
.mover { margin: 0 auto; transform-origin: 50% 100%; }
svg { display: block; width: 100%; height: auto; overflow: visible; }

/* ---------- pivots ---------- */
.stage, .torso-pose, .torso, .tail-pose, .tail, .head-pose, .head, .sit, .stand, .stand-hop, .stand-flip,
.stand-body, .leg, .s-tail, .s-head-place,
.ear-l-pose, .ear-l, .ear-r-pose, .ear-r, .muzzle, .bell-pose, .bell, .cap-pose, .cap, .mouth-g { transform-box: view-box; }
.stage { transform-origin: 120px 224px; }
.sit { transform-origin: 124px 222px; }
.stand, .stand-hop, .stand-flip { transform-origin: 140px 222px; }
.stand-body { transform-origin: 150px 200px; }
.lf { transform-origin: 103px 166px; }
.lf2 { transform-origin: 123.5px 164px; }
.lb { transform-origin: 185px 166px; }
.lb2 { transform-origin: 203.5px 164px; }
.s-tail { transform-origin: 216px 128px; }
.s-head-place { transform-origin: 0 0; transform: translate(64px, 98px) scale(.8) translate(-82px, -116px); }
.ground { transform-box: fill-box; transform-origin: 50% 50%; }
.torso-pose, .torso { transform-origin: 174px 218px; }   /* pivot under the slot: slot stays put */
.cap-pose, .cap { transform-origin: 126px 205px; }       /* plug hinge: left edge of the rim */
.mouth-g { transform-origin: 82px 162px; }
.gb { transform-box: fill-box; transform-origin: 50% 50%; }
.tail-pose, .tail { transform-origin: 210px 148px; }
.head-pose, .head { transform-origin: 82px 170px; }
.ear-l-pose, .ear-l { transform-origin: 48px 100px; }
.ear-r-pose, .ear-r { transform-origin: 116px 100px; }
.muzzle { transform-origin: 82px 127px; }
.bell-pose, .bell { transform-origin: 82px 181px; }
.coin, .eye, .puff { transform-box: fill-box; transform-origin: 50% 50%; }
.tear { transform-box: fill-box; transform-origin: 50% 0%; }

/* ---------- sitting vs standing ---------- */
.stand { opacity: 0; }
.standing .stand { opacity: 1; }
.standing .sit { opacity: 0; }
.puff, .eyes-arc, .jingle { opacity: 0; }

/* ---------- mood poses (transition = smooth switch) ---------- */
.torso-pose, .head-pose, .ear-l-pose, .ear-r-pose, .tail-pose, .bell-pose, .pupils, .cap-pose {
  transition: transform .6s cubic-bezier(.34, 1.5, .64, 1);
}
.mouth, .brows, .happy-coins, .sad-coins, .tear-g, .cheeks, .grass-bits { transition: opacity .4s ease; }
.mouth, .brows, .happy-coins, .sad-coins, .tear-g, .grass-bits { opacity: 0; }
.hc, .sc, .tear, .gb { opacity: 0; }
.cheeks { opacity: .7; }

.m-happy .mouth-happy, .m-neutral .mouth-neutral, .m-sad .mouth-sad { opacity: 1; }
.m-happy .happy-coins, .m-sad .sad-coins, .m-neutral .grass-bits, .m-sad .brows, .m-sad .tear-g { opacity: 1; }
.m-happy .cheeks { opacity: 1; }
.m-sad .cheeks { opacity: .35; }

.m-happy .ear-l-pose { transform: rotate(10deg); }
.m-happy .ear-r-pose { transform: rotate(-10deg); }
.m-sad .ear-l-pose { transform: rotate(-38deg); }
.m-sad .ear-r-pose { transform: rotate(38deg); }
.m-sad .torso-pose { transform: scaleX(.96); }
.m-sad .head-pose { transform: translateY(3px) rotate(-4deg); }
.m-sad .pupils { transform: translateY(2.5px); }
.m-sad .tail-pose { transform: rotate(24deg); }
.m-sad .bell-pose { transform: translateY(1.5px); }
.m-sad .cap-pose { transform: translate(-4px, 2px) rotate(96deg); }   /* plug popped open, dangling */

/* ---------- always: blink ---------- */
.eye { animation: blink 4.2s infinite; }
.eye-r { animation-delay: .02s; }
@keyframes blink {
  0%, 91%, 97%, 100% { transform: scaleY(1); }
  94% { transform: scaleY(.08); }
}

/* ---------- happy (and very-happy while sitting): "so happy to see you", like a dog ---------- */
.m-happy .stage { animation: happy-dance 2.8s ease-in-out infinite; }
.m-happy .torso { animation: fatten 4s infinite; }
.m-happy .hc { animation: coin-drop 4s infinite; }
.m-happy .hc2 { animation-delay: 1s; }
.m-happy .hc3 { animation-delay: 2s; }
.m-happy .tail, .m-happy .s-tail { animation: wag .44s ease-in-out infinite; }
.m-happy .bell { animation: jingle-swing .7s ease-in-out infinite; }
.m-happy .jingle { animation: jingle-marks .7s steps(1, end) infinite; }
.m-happy .head { animation: bob 1.4s ease-in-out infinite; }
.m-happy .ear-l { animation: perk-l .7s ease-in-out infinite; }
.m-happy .ear-r { animation: perk-r .7s ease-in-out infinite .05s; }
.m-happy .eyes-open { animation: squint-open 3.6s infinite; }
.m-happy .eyes-arc { animation: squint-arc 3.6s infinite; }

/* hip sway, then two little excited bounces (squash on landing) */
@keyframes happy-dance {
  0%   { transform: translateY(0) rotate(0) scale(1, 1); }
  9%   { transform: translateY(0) rotate(-2.6deg) scale(1, 1); }
  18%  { transform: translateY(0) rotate(2.6deg) scale(1, 1); }
  27%  { transform: translateY(0) rotate(-2.6deg) scale(1, 1); }
  36%  { transform: translateY(0) rotate(2deg) scale(1, 1); }
  45%  { transform: translateY(0) rotate(0) scale(1.04, .95); }
  53%  { transform: translateY(-9px) rotate(0) scale(.98, 1.03); }
  60%  { transform: translateY(0) rotate(0) scale(1.045, .955); }
  67%  { transform: translateY(-6px) rotate(0) scale(.99, 1.02); }
  74%  { transform: translateY(0) rotate(0) scale(1.03, .97); }
  81%, 100% { transform: translateY(0) rotate(0) scale(1, 1); }
}
/* fast dog wag: overshoots each swing a little and springs back */
@keyframes wag {
  0%   { transform: rotate(-30deg); }
  36%  { transform: rotate(22deg); }
  48%  { transform: rotate(14deg); }
  86%  { transform: rotate(-36deg); }
  100% { transform: rotate(-30deg); }
}
@keyframes perk-l {
  0%, 100% { transform: rotate(0); }
  35% { transform: rotate(13deg); }
  55% { transform: rotate(6deg); }
  75% { transform: rotate(10deg); }
}
@keyframes perk-r {
  0%, 100% { transform: rotate(0); }
  35% { transform: rotate(-13deg); }
  55% { transform: rotate(-6deg); }
  75% { transform: rotate(-10deg); }
}
@keyframes squint-open { 0%, 30%, 47%, 100% { opacity: 1; } 31%, 46% { opacity: 0; } }
@keyframes squint-arc  { 0%, 30%, 47%, 100% { opacity: 0; } 31%, 46% { opacity: 1; } }
@keyframes jingle-swing {
  0%, 100% { transform: rotate(0); }
  22% { transform: rotate(18deg); }
  40% { transform: rotate(-6deg); }
  62% { transform: rotate(-18deg); }
  82% { transform: rotate(6deg); }
}
@keyframes jingle-marks { 0%, 45%, 100% { opacity: 0; } 15%, 65% { opacity: .85; } }

/* each coin: fall (ease-in = gravity), cross the slot line (clipped), gone */
@keyframes coin-drop {
  0% { transform: translateY(-150px); opacity: 0; animation-timing-function: cubic-bezier(.55, 0, 1, .6); }
  6% { opacity: 1; }
  24% { transform: translateY(0); animation-timing-function: cubic-bezier(.3, 0, .7, 1); }
  27% { transform: translateY(2px); animation-timing-function: ease-in; }   /* rests on the lip a beat */
  36% { transform: translateY(24px); opacity: 1; }
  37%, 100% { transform: translateY(24px); opacity: 0; }
}
/* belly gulps a little rounder as each coin lands (~32%, 57%, 82%) */
@keyframes fatten {
  0%, 30% { transform: scale(1, 1); }
  34% { transform: scale(1.045, .985); }
  40%, 55% { transform: scale(1.03, .99); }
  59% { transform: scale(1.065, .975); }
  65%, 80% { transform: scale(1.055, .98); }
  84% { transform: scale(1.09, .968); }
  90% { transform: scale(1.08, .972); }
  100% { transform: scale(1, 1); }
}
@keyframes bob {
  0%, 100% { transform: rotate(0); }
  25% { transform: rotate(3.5deg); }
  75% { transform: rotate(-3.5deg); }
}

/* ---------- very-happy: standing / trotting (the run is scripted in JS) ---------- */
.standing .stage { animation: none; }
.gait-trot .leg { animation: trot .48s ease-in-out infinite; }
.gait-trot .lf2, .gait-trot .lb { animation-delay: -.24s; }
.gait-trot .stand-body { animation: trot-bob .24s ease-in-out infinite; }
@keyframes trot { 0%, 100% { transform: rotate(20deg); } 50% { transform: rotate(-20deg); } }
@keyframes trot-bob { 0%, 100% { transform: translateY(0); } 50% { transform: translateY(-4px); } }
.gait-trot .puff { animation: puff .6s ease-out infinite; }
.gait-trot .pf2 { animation-delay: .15s; }
.gait-trot .pf3 { animation-delay: .3s; }
@keyframes puff {
  0% { transform: translate(0, 0) scale(.6); opacity: .9; }
  100% { transform: translate(30px, -8px) scale(1.5); opacity: 0; }
}

/* ---------- mood switch "pop" (after happy so it wins for its half second) ---------- */
.pop .stage { animation: pop .5s cubic-bezier(.3, 1.4, .5, 1); }
@keyframes pop {
  0% { transform: scale(1, 1); }
  35% { transform: scale(1.05, .94); }
  70% { transform: scale(.98, 1.02); }
  100% { transform: scale(1, 1); }
}

/* ---------- neutral: sits, blinks, chews cud ---------- */
.m-neutral .eye { animation: blink2 5s infinite; }
.m-neutral .muzzle { animation: chew .8s ease-in-out infinite; }
.m-neutral .mouth-g { animation: chew-x .8s ease-in-out infinite; }
.m-neutral .ear-l { animation: flick-l 5.5s infinite; }
.m-neutral .ear-r { animation: flick-r 5.5s infinite; }
.m-neutral .tail { animation: sway 4s ease-in-out infinite; }
.m-neutral .torso { animation: breathe 4s ease-in-out infinite; }
.m-neutral .bell { animation: bell-sway 4s ease-in-out infinite; }

@keyframes blink2 {
  0%, 40%, 44%, 84%, 88%, 92%, 100% { transform: scaleY(1); }
  42%, 86%, 90% { transform: scaleY(.08); }
}
/* steady cud chewing: muzzle squashes while the jaw grinds in a small loop */
@keyframes chew {
  0%, 50%, 100% { transform: rotate(0) scaleY(1); }
  25% { transform: rotate(-2.5deg) scaleY(.93); }
  75% { transform: rotate(2.5deg) scaleY(.93); }
}
@keyframes chew-x {
  0%, 100% { transform: translate(0, 0); }
  25% { transform: translate(-3.2px, 2px); }
  50% { transform: translate(0, 3.4px); }
  75% { transform: translate(3.2px, 2px); }
}
/* grass bits: pixel-y stepped fall from the mouth corner, fading out */
.m-neutral .gb { animation: grass 4.8s steps(9, end) infinite; }
.m-neutral .gb1 { --dx: 6px; animation-delay: .3s; }
.m-neutral .gb2 { --dx: 11px; animation-delay: .7s; }
.m-neutral .gb3 { --dx: 3px; animation-delay: 2.1s; }
.m-neutral .gb4 { --dx: 9px; animation-delay: 2.5s; }
.m-neutral .gb5 { --dx: 14px; animation-delay: 3.6s; }
.m-neutral .gb6 { --dx: 5px; animation-delay: 4.1s; }
@keyframes grass {
  0% { transform: translate(0, 0); opacity: 0; }
  2% { transform: translate(0, 0); opacity: 1; }
  22% { transform: translate(calc(var(--dx) * .8), 24px); opacity: 1; }
  32% { transform: translate(var(--dx), 32px); opacity: 0; }
  100% { transform: translate(var(--dx), 34px); opacity: 0; }
}
@keyframes flick-l {
  0%, 60%, 70%, 100% { transform: rotate(0); }
  63% { transform: rotate(16deg); }
  66% { transform: rotate(-5deg); }
}
@keyframes flick-r {
  0%, 82%, 92%, 100% { transform: rotate(0); }
  85% { transform: rotate(-16deg); }
  88% { transform: rotate(5deg); }
}
@keyframes sway { 0%, 100% { transform: rotate(-4deg); } 50% { transform: rotate(5deg); } }
@keyframes breathe { 0%, 100% { transform: scale(1, 1); } 50% { transform: scale(1.012, 1.018); } }
@keyframes bell-sway { 0%, 100% { transform: rotate(-3deg); } 50% { transform: rotate(3deg); } }

/* ---------- sad: plug pops, coins fall out, thinner and droopy ---------- */
.m-sad .eye { animation: blink 5.5s infinite; }
.m-sad .torso { animation: shrink 4.5s infinite; }
.m-sad .sc1 { animation: fall-1 4.5s infinite; }
.m-sad .sc2 { animation: fall-2 4.5s infinite 1.2s; }
.m-sad .sc3 { animation: fall-3 4.5s infinite 2.4s; }
.m-sad .tear { animation: tear 4.5s infinite .6s; }
.m-sad .cap { animation: dangle 1.6s ease-in-out infinite alternate; }
.m-sad .head { animation: sigh 4.5s ease-in-out infinite; }

/* skinnier each time a coin escapes (~22%, 49%, 75%); combined with the pose .96 -> ~.92 */
@keyframes shrink {
  0%, 22% { transform: scaleX(1); }
  27%, 49% { transform: scaleX(.985); }
  54%, 75% { transform: scaleX(.97); }
  80%, 92% { transform: scaleX(.955); }
  100% { transform: scaleX(1); }
}
@keyframes fall-1 {
  0% { transform: translate(0, 0) rotate(0); opacity: 1; animation-timing-function: ease-in; }
  16% { transform: translate(0, 13px) rotate(0); animation-timing-function: cubic-bezier(.5, 0, 1, 1); }
  30% { transform: translate(-9.1px, 34px) rotate(-60deg); animation-timing-function: ease-out; }
  37% { transform: translate(-15.6px, 27px) rotate(-110deg); animation-timing-function: ease-in; }
  44% { transform: translate(-20.8px, 34px) rotate(-150deg); opacity: 1; animation-timing-function: ease-out; }
  58% { transform: translate(-26.0px, 34px) rotate(-200deg); opacity: 0; }
  100% { transform: translate(-26.0px, 34px) rotate(-200deg); opacity: 0; }
}
@keyframes fall-2 {
  0% { transform: translate(0, 0) rotate(0); opacity: 1; animation-timing-function: ease-in; }
  16% { transform: translate(0, 13px) rotate(0); animation-timing-function: cubic-bezier(.5, 0, 1, 1); }
  30% { transform: translate(2.1px, 34px) rotate(27deg); animation-timing-function: ease-out; }
  37% { transform: translate(3.6px, 27px) rotate(50deg); animation-timing-function: ease-in; }
  44% { transform: translate(4.8px, 34px) rotate(68deg); opacity: 1; animation-timing-function: ease-out; }
  58% { transform: translate(6.0px, 34px) rotate(90deg); opacity: 0; }
  100% { transform: translate(6.0px, 34px) rotate(90deg); opacity: 0; }
}
@keyframes fall-3 {
  0% { transform: translate(0, 0) rotate(0); opacity: 1; animation-timing-function: ease-in; }
  16% { transform: translate(0, 13px) rotate(0); animation-timing-function: cubic-bezier(.5, 0, 1, 1); }
  30% { transform: translate(9.8px, 34px) rotate(66deg); animation-timing-function: ease-out; }
  37% { transform: translate(16.8px, 27px) rotate(121deg); animation-timing-function: ease-in; }
  44% { transform: translate(22.4px, 34px) rotate(165deg); opacity: 1; animation-timing-function: ease-out; }
  58% { transform: translate(28.0px, 34px) rotate(220deg); opacity: 0; }
  100% { transform: translate(28.0px, 34px) rotate(220deg); opacity: 0; }
}
@keyframes tear {
  0% { transform: translateY(0) scale(.3); opacity: 0; }
  12% { transform: translateY(0) scale(1); opacity: 1; }
  55% { transform: translateY(16px) scale(1); opacity: 1; }
  70%, 100% { transform: translateY(22px) scale(.9); opacity: 0; }
}
@keyframes dangle { from { transform: rotate(-7deg); } to { transform: rotate(6deg); } }
@keyframes sigh { 0%, 100% { transform: translateY(0); } 50% { transform: translateY(1.5px); } }

/* ---------- reduced motion: one still pose per mood (very-happy = standing) ---------- */
@media (prefers-reduced-motion: reduce) {
  *, *::before, *::after { animation: none !important; transition: none !important; }
  .m-happy .torso { transform: scale(1.06, .98); }
  .m-happy .hc1 { opacity: 1; transform: translateY(7px); }
  .m-happy .hc2 { opacity: 1; transform: translateY(-42px); }
  .m-happy .tail { transform: rotate(14deg); }
  .m-happy .s-tail { transform: rotate(-20deg); }
  .m-sad .torso { transform: scaleX(.96); }
  .m-sad .sc1 { opacity: 1; transform: translate(-6px, 28px) rotate(-40deg); }
  .m-sad .sc2 { opacity: 1; transform: translate(0, 9px); }
  .m-neutral .gb1 { opacity: 1; transform: translate(4px, 9px); }
  .m-neutral .gb2 { opacity: .7; transform: translate(8px, 19px); }
  .m-sad .tear { opacity: 1; transform: translateY(6px); }
}
`
