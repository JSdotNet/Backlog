import type { Plan, PlanEntry, Worker, WorkerState } from '../types'

/** get_plan_items on any server whose tool name ends in it. */
export const isPlanItems = (tool: string) => /^mcp__.*__get_plan_items$/.test(tool)
export const isTransition = (tool: string) => /^mcp__.*backlog.*__transition$/.test(tool)
/** start_run/update_stage/finish_run on any delivery surface, bound or bare. */
export const surfaceOp = (tool: string): 'start_run' | 'update_stage' | 'finish_run' | null => {
  const m = /^mcp__.*__(start_run|update_stage|finish_run)$/.exec(tool)
  return m ? (m[1] as 'start_run' | 'update_stage' | 'finish_run') : null
}

/** `Draft`, `in-progress`, `InProgress` → `draft`, `inprogress`. */
export const normStatus = (s: unknown) => String(s ?? '').toLowerCase().replace(/[^a-z]/g, '')

const parseJson = (text: string | undefined): unknown => {
  if (!text) return null
  try {
    return JSON.parse(text)
  } catch {
    const at = text.indexOf('{')
    if (at < 0) return null
    try {
      return JSON.parse(text.slice(at))
    } catch {
      return null
    }
  }
}

/** A stored title carries its step number (`3 - Sort by effort`): the number and the rest. */
export const splitTitle = (title: string, fallback: number) => {
  const m = /^(\d+)\s*-\s*(.*)$/.exec(title.trim())
  return m ? { n: Number(m[1]), title: (m[2] ?? '').trim() } : { n: fallback, title: title.trim() }
}

/** The PlanItemsPayload: { planId, count, entries: EntryPayload[] }, wire tokens lowercase. */
export const parsePlan = (tag: string, text: string | undefined): Plan | null => {
  const body = parseJson(text) as { planId?: string; entries?: Record<string, unknown>[] } | null
  if (!body || !Array.isArray(body.entries)) return null
  const entries: PlanEntry[] = []
  for (const raw of body.entries) {
    const type = String(raw.type ?? '').toLowerCase()
    if (type === 'plan') continue
    const { n, title } = splitTitle(String(raw.title ?? ''), entries.length + 1)
    entries.push({
      id: String(raw.id ?? ''),
      n,
      title,
      type,
      status: normStatus(raw.status),
    })
  }
  return { tag: body.planId ?? tag, entries }
}

/** The run id start_run answered with, under any of the spellings a surface uses. */
export const parseRunId = (text: string | undefined): string => {
  const body = parseJson(text) as Record<string, unknown> | null
  const id = body?.runId ?? body?.id ?? (body?.run as Record<string, unknown> | undefined)?.id
  if (id) return String(id)
  const m = /run[_ ]?id["':\s]+([\w-]+)/i.exec(text ?? '')
  return m?.[1] ?? ''
}

/** The brief's opening line: You run one entry of the Backlog plan `<tag>`: `<n> - <Title>` (id `<id>`). */
export const parseBrief = (prompt: string) => {
  const m = /entry of the Backlog plan `([^`]+)`: `(\d+)\s*-\s*([^`]+)` \(id `([^`]+)`\)/.exec(prompt)
  if (!m) return null
  const { title } = splitTitle(m[3] ?? '', 0)
  return { tag: m[1] ?? '', n: Number(m[2]), title, entryId: m[4] ?? '' }
}

/** A sub-agent's one-line return: done-before, gate, pr <url>, blocked <stage>: <reason>. */
export const parseReturn = (answer: string): { state: WorkerState; note: string } => {
  const lines = answer.trim().split(/\r?\n/)
  for (const line of [lines[0] ?? '', ...lines.slice(1).reverse()]) {
    const l = line.replace(/^[`*\s]+/, '')
    const m = /^(done-before|gate|pr|blocked)\b[:\s]*(.*)$/i.exec(l)
    if (m) return { state: (m[1] ?? '').toLowerCase() as WorkerState, note: (m[2] ?? '').trim() }
  }
  return { state: 'stopped', note: (lines[0] ?? '').slice(0, 80) }
}

export const newWorker = (agentId: string, b: NonNullable<ReturnType<typeof parseBrief>>): Worker => ({
  agentId,
  entryId: b.entryId,
  n: b.n,
  title: b.title,
  state: 'running',
  note: '',
  flow: '',
  runId: '',
  stages: [],
  stageStatus: [],
  runStatus: '',
})

/** Progress counted per entry, a worker's state overriding the stored status. */
export const tally = (plan: Plan | null, workers: Worker[]) => {
  const byEntry = new Map(workers.map(w => [w.entryId, w]))
  // A plan read from a file never passes get_plan_items: its workers are all there is.
  const ids = plan && plan.entries.length > 0 ? plan.entries.map(e => e.id) : workers.map(w => w.entryId)
  const c = { total: ids.length, done: 0, running: 0, gate: 0, review: 0, blocked: 0, manual: 0, waiting: 0 }
  for (const id of ids) {
    const e = plan?.entries.find(x => x.id === id)
    const w = byEntry.get(id)
    const k = kindOf(e, w)
    c[k] += 1
  }
  return c
}

export type Kind = 'done' | 'running' | 'gate' | 'review' | 'blocked' | 'manual' | 'waiting'

export const kindOf = (e: PlanEntry | undefined, w: Worker | undefined): Kind => {
  if (e && (e.status === 'done' || e.status === 'archived')) return 'done'
  if (w) {
    if (w.state === 'running') return 'running'
    if (w.state === 'gate') return 'gate'
    if (w.state === 'pr') return 'review'
    if (w.state === 'done-before') return 'done'
    // Aborted, or a return that is none of the four: it needs the person as much as a block.
    if (w.state === 'blocked' || w.state === 'stopped') return 'blocked'
  }
  if (e && (e.type === 'task' || e.type === 'test')) return 'manual'
  if (e?.status === 'inprogress') return 'running'
  return 'waiting'
}
