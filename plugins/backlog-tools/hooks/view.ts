import type { Plan, PlanEntry, Worker } from '../types'
import { kindOf } from './parse'
import type { Kind } from './parse'

// What the pane's matrix and the band draw, worked out apart from drawing them so the tests
// can hold the layout without a surface.

export type Cell = { g: string; color?: string; dim?: boolean }

/** A stage's mark, in the band language delivery-run-view draws a flow in. */
export const CELL: Record<string, Cell> = {
  done: { g: '✓', color: 'green' },
  in_progress: { g: '●', color: 'yellow' },
  blocked: { g: '✗', color: 'red' },
  skipped: { g: '–', dim: true },
  pending: { g: '○', dim: true },
}

export const KIND: Record<Kind, { mark: string; color?: string; label: string }> = {
  done: { mark: '✓', color: 'green', label: 'done' },
  running: { mark: '▶', color: 'cyan', label: 'running' },
  gate: { mark: '◆', color: 'yellow', label: 'at gate' },
  review: { mark: '◇', color: 'magenta', label: 'PR open' },
  blocked: { mark: '✗', color: 'red', label: 'blocked' },
  // One cell wide in every terminal, so the matrix columns stay aligned.
  manual: { mark: '✎', color: 'yellow', label: 'manual' },
  waiting: { mark: '○', label: 'waiting' },
}

const SHORT: Record<string, string> = {
  'update base': 'upd', scope: 'scp', plan: 'pln', implement: 'imp', review: 'rev',
  'build test': 'b&t', verify: 'ver', 'spec check': 'spc', ready: 'rdy',
  'personal validation': 'pv', 'create pull request': 'pr', 'create pr': 'pr', 'report back': 'rb',
  summary: 'sum', drafting: 'dft', 'check review': 'c&r',
}

/**
 * A stage's column heading: the delivery phases by their usual short name, whether start_run
 * named them by title (`Build & Test`) or by id (`phase-build-test`); others cut to 3.
 */
export const abbrev = (stage: string) => {
  const key = stage.trim().toLowerCase().replace(/^phase-/, '').replace(/[-_&]+/g, ' ').replace(/\s+/g, ' ').trim()
  return SHORT[key] ?? key.replace(/ /g, '').slice(0, 3)
}

export const cellsOf = (w: Worker | undefined): Cell[] =>
  w ? w.stages.map((_, i) => CELL[w.stageStatus[i] ?? 'pending'] ?? { g: '○', dim: true }) : []

/** The stage a worker stands at: the one running or blocked, else none. */
export const currentStage = (w: Worker) => {
  const i = w.stageStatus.findIndex(s => s === 'in_progress' || s === 'blocked')
  return i >= 0 ? (w.stages[i] ?? '') : ''
}

export type Row = { id: string; n: number; title: string; type: string; kind: Kind; w?: Worker; e?: PlanEntry }

/** Every entry of the plan in plan order; without a plan read, the workers. */
export const rowsOf = (plan: Plan | null, workers: Worker[]): Row[] => {
  const byEntry = new Map(workers.map(w => [w.entryId, w]))
  return plan && plan.entries.length > 0
    ? plan.entries.map(e => ({ id: e.id, n: e.n, title: e.title, type: e.type, kind: kindOf(e, byEntry.get(e.id)), w: byEntry.get(e.id), e }))
    : workers.map(w => ({ id: w.entryId, n: w.n, title: w.title, type: 'prompt', kind: kindOf(undefined, w), w }))
}

/** The columns of the matrix: the longest stage list any worker declared. */
export const columnsOf = (rows: Row[]) =>
  rows.reduce<string[]>((best, r) => (r.w && r.w.stages.length > best.length ? r.w.stages : best), [])

/** A matrix cell as design C draws it: a block per stage, its mark dark on top. */
export const BLOCK: Record<string, { bg: string; g: string }> = {
  done: { bg: '#5fd38d', g: '' },
  in_progress: { bg: '#56c8e8', g: '▶' },
  blocked: { bg: '#f2767a', g: '✗' },
  skipped: { bg: '#3a3a42', g: '–' },
  pending: { bg: '#26262c', g: '' },
}

/** The same cell in a terminal, where a block cannot leave a gap: one coloured glyph. */
export const GLYPH: Record<string, Cell> = {
  done: { g: '■', color: 'green' },
  in_progress: { g: '▶', color: 'cyan' },
  blocked: { g: '✗', color: 'red' },
  skipped: { g: '–', dim: true },
  pending: { g: '·', dim: true },
}

/**
 * How wide the terminal matrix draws in `cols` cells: a mark and a title column, then one cell
 * per stage — three wide and a gap, under its short name, when that leaves the title 14 cells,
 * else two wide and no headings. The title column stops at 36, so a wide pane does not push
 * the cells away from the names.
 */
export const matrixLayout = (cols: number, stageCount: number) => {
  const wide = cols - 2 - 1 - 14 >= stageCount * 4
  const cell = wide ? 3 : 2
  const gap = wide ? 1 : 0
  const titleWidth = Math.min(36, Math.max(10, cols - 2 - 1 - stageCount * (cell + gap)))
  return { cell, gap, hasHeadings: wide, titleWidth }
}

const HEX: Record<string, string> = {
  green: '#5fd38d', cyan: '#56c8e8', yellow: '#e8c25a', magenta: '#d58be8', red: '#f2767a',
}
const xml = (text: string) => text.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
const clip = (text: string, max: number) => (text.length <= max ? text : `${text.slice(0, max - 1)}…`)

/**
 * Design C as one SVG, for a surface that draws one: a dark card with the plan's tag and
 * counts, the stage headings, a row per entry with a rounded block per stage, and the gate's
 * review link and each block's reason under it.
 */
export const matrixSvg = (tag: string, summary: string, rows: Row[], stages: string[]) => {
  const pad = 16
  const labelWidth = 260
  const cell = 30
  const gap = 4
  const rowHeight = 24
  const notes = rows.filter(r => r.w && r.w.note !== '' && r.w.state !== 'running')
  const width = pad * 2 + labelWidth + Math.max(stages.length, 4) * (cell + gap)
  const top = 68
  const notesTop = top + rows.length * rowHeight + 12
  const height = notesTop + notes.length * 20 + (notes.length > 0 ? 8 : 0)
  const out: string[] = [
    `<svg xmlns="http://www.w3.org/2000/svg" width="${width}" height="${height}" viewBox="0 0 ${width} ${height}" font-family="JetBrains Mono, ui-monospace, Menlo, Consolas, monospace" font-size="13">`,
    `<rect width="${width}" height="${height}" rx="8" fill="#121215"/>`,
    `<text x="${pad}" y="28" fill="#e6e6ea" font-weight="700">${xml(tag)}</text>`,
    `<text x="${width - pad}" y="28" fill="#e6e6ea" text-anchor="end">${xml(summary)}</text>`,
  ]
  const x0 = pad + labelWidth
  stages.forEach((s, i) => {
    out.push(`<text x="${x0 + i * (cell + gap) + cell / 2}" y="56" fill="#9a9aa3" font-size="11" text-anchor="middle">${xml(abbrev(s))}</text>`)
  })
  rows.forEach((r, j) => {
    const y = top + j * rowHeight
    const kind = KIND[r.kind]
    out.push(`<text x="${pad}" y="${y + 14}" fill="${HEX[kind.color ?? ''] ?? '#9a9aa3'}">${xml(kind.mark)}</text>`)
    out.push(`<text x="${pad + 20}" y="${y + 14}" fill="#e6e6ea">${xml(clip(`${r.n} ${r.title}`, 32))}</text>`)
    const w = r.w
    if (w && w.stages.length > 0) {
      w.stages.forEach((_, i) => {
        const b = BLOCK[w.stageStatus[i] ?? 'pending'] ?? BLOCK.pending!
        const x = x0 + i * (cell + gap)
        out.push(`<rect x="${x}" y="${y}" width="${cell}" height="18" rx="3" fill="${b.bg}"/>`)
        if (b.g) out.push(`<text x="${x + cell / 2}" y="${y + 13}" fill="#121215" font-size="11" text-anchor="middle">${xml(b.g)}</text>`)
      })
    } else if (r.kind === 'manual' || (w && w.state === 'running')) {
      out.push(`<text x="${x0}" y="${y + 14}" fill="#9a9aa3">${r.kind === 'manual' ? xml(r.type) : 'starting…'}</text>`)
    }
  })
  notes.forEach((r, j) => {
    const w = r.w!
    const kind = KIND[r.kind]
    out.push(`<text x="${pad}" y="${notesTop + j * 20 + 12}" fill="#9a9aa3" font-size="12"><tspan fill="${HEX[kind.color ?? ''] ?? '#9a9aa3'}">${xml(kind.mark)}</tspan> ${r.n} ${xml(currentStage(w) || w.state)}: ${xml(clip(w.note, 90))}</text>`)
  })
  out.push('</svg>')
  return out.join('')
}

/** The band's rows: the entries still in flow — running, at a gate, or blocked. */
export const liveRows = (rows: Row[]) =>
  rows.filter(r => r.w && (r.kind === 'running' || r.kind === 'gate' || r.kind === 'blocked'))

/** The band's "also": the entries off the band, counted by kind, as `✓ 12 · ◇ 2 · ✎ 1`. */
export const alsoLine = (rows: Row[]) => {
  const off = rows.filter(r => liveRows([r]).length === 0)
  return (['done', 'review', 'running', 'manual', 'waiting'] as const)
    .map(k => [k, off.filter(r => r.kind === k).length] as const)
    .filter(([, n]) => n > 0)
    .map(([k, n]) => `${KIND[k].mark} ${n}`)
    .join(' · ')
}

/** The band's live rows within the rows it may take, and how many did not fit. */
export const bandRows = (rows: Row[], maxRows: number) => {
  const live = liveRows(rows)
  // One row for the header, and one for "+n more" when it is needed.
  const room = Math.max(1, maxRows - 1)
  return live.length <= room ? { shown: live, more: 0 } : { shown: live.slice(0, room - 1), more: live.length - (room - 1) }
}
