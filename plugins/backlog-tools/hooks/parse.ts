import type { Plan, PlanEntry, Worker, WorkerState } from '../types'

/** get_plan_items on any server whose tool name ends in it. */
export const isPlanItems = (tool: string) => /^mcp__.*__get_plan_items$/.test(tool)
export const isTransition = (tool: string) => /^mcp__.*backlog.*__transition$/.test(tool)
/** The host's session tools, on any server: start one, list or read them, read a session's events. */
export const isStartSession = (tool: string) => /^mcp__.*__start_session$/.test(tool)
export const isSessionRows = (tool: string) => /^mcp__.*__(list_sessions|get_session)$/.test(tool)
export const isEvents = (tool: string) => /^mcp__.*__list_events$/.test(tool)
/** A message to a session: the host's SendMessage (`to`) or a session server's send_message (`session_id`). */
export const messageTarget = (tool: string, args: Record<string, unknown>): string =>
  tool === 'SendMessage' ? String(args.to ?? '') : /^mcp__.*__send_message$/.test(tool) ? String(args.session_id ?? '') : ''
/** list_runs/get_run on any delivery surface, bound or bare. */
export const isRuns = (tool: string) => /^mcp__.*__(list_runs|get_run)$/.test(tool)

/** `Draft`, `in-progress`, `InProgress` → `draft`, `inprogress`. */
export const normStatus = (s: unknown) => String(s ?? '').toLowerCase().replace(/[^a-z]/g, '')

const parseJson = (text: string | undefined): unknown => {
  if (!text) return null
  try {
    return JSON.parse(text)
  } catch {
    const at = [text.indexOf('{'), text.indexOf('[')].filter(i => i >= 0).sort((a, b) => a - b)[0]
    if (at === undefined) return null
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

/**
 * The PlanItemsPayload: { planId, count, entries: EntryPayload[] }, wire tokens lowercase.
 * An entry with no step number in its title — a follow-up create_item filed under the plan tag —
 * is numbered after the highest step, so it never shows a number an imported step holds.
 */
export const parsePlan = (tag: string, text: string | undefined): Plan | null => {
  const body = parseJson(text) as { planId?: string; entries?: Record<string, unknown>[] } | null
  if (!body || !Array.isArray(body.entries)) return null
  const split = body.entries
    .filter(raw => String(raw.type ?? '').toLowerCase() !== 'plan')
    .map(raw => ({ raw, ...splitTitle(String(raw.title ?? ''), NaN) }))
  let next = Math.max(0, ...split.map(s => s.n).filter(n => !Number.isNaN(n)))
  const entries: PlanEntry[] = split.map(({ raw, n, title }) => ({
    id: String(raw.id ?? ''),
    n: Number.isNaN(n) ? ++next : n,
    title,
    type: String(raw.type ?? '').toLowerCase(),
    status: normStatus(raw.status),
  }))
  return { tag: body.planId ?? tag, entries }
}

const stageName = (v: unknown): string => {
  const raw = typeof v === 'string' ? v : v && typeof v === 'object' ? ((v as { name?: unknown; title?: unknown }).name ?? (v as { title?: unknown }).title) : undefined
  return typeof raw === 'string' ? raw.trim() : ''
}

/**
 * The stages a run lists, however they are spelled: an array of names or of
 * `{ name }` / `{ title }` objects, that array JSON-encoded as a string, or a comma-separated
 * string. Anything else declares none.
 */
export const parseStages = (v: unknown): string[] => {
  if (typeof v === 'string') {
    const text = v.trim()
    if (text.startsWith('[')) {
      try {
        return parseStages(JSON.parse(text))
      } catch {
        return []
      }
    }
    if (text.startsWith('{')) return []
    return text.split(',').map(s => s.trim()).filter(s => s !== '')
  }
  if (!Array.isArray(v)) return []
  return v.map(stageName).filter(s => s !== '')
}

/** The brief's opening line: You run one entry of the Backlog plan `<tag>`: `<n> - <Title>` (id `<id>`). */
export const parseBrief = (prompt: string) => {
  const m = /entry of the Backlog plan `([^`]+)`: `(\d+)\s*-\s*([^`]+)` \(id `([^`]+)`\)/.exec(prompt)
  if (!m) return null
  const { title } = splitTitle(m[3] ?? '', 0)
  return { tag: m[1] ?? '', n: Number(m[2]), title, entryId: m[4] ?? '' }
}

/** An entry session's one-line return: done-before, gate, pr <url>, blocked <stage>: <reason>. */
export const parseReturn = (answer: string): { state: WorkerState; note: string } => {
  const lines = answer.trim().split(/\r?\n/)
  for (const line of [lines[0] ?? '', ...lines.slice(1).reverse()]) {
    const l = line.replace(/^[`*\s]+/, '')
    const m = /^(done-before|gate|pr|blocked)\b[:\s]*(.*)$/i.exec(l)
    if (m) return { state: (m[1] ?? '').toLowerCase() as WorkerState, note: (m[2] ?? '').trim() }
  }
  return { state: 'stopped', note: (lines[0] ?? '').slice(0, 80) }
}

/**
 * The session id start_session answered: its JSON `sessionId` / `session_id` when that is a
 * `local_…` id — the answer may name a parent session first — else, when it has no such field,
 * the first `local_…` id in it.
 */
export const parseSessionId = (text: string | undefined): string => {
  const body = parseJson(text) as Record<string, unknown> | null
  const own = body && typeof body === 'object' && !Array.isArray(body) ? body.sessionId ?? body.session_id : undefined
  // An id field of its own that is no local id answers no session: any local id beside it is another's.
  if (own !== undefined && own !== null) return typeof own === 'string' ? /^local_[0-9A-Za-z-]+$/.exec(own.trim())?.[0] ?? '' : ''
  return /local_[0-9A-Za-z-]+/.exec(text ?? '')?.[0] ?? ''
}

const asText = (v: unknown) => (typeof v === 'string' ? v : '')

/**
 * The sessions a list_sessions or get_session answered — a JSON array of rows or one row,
 * bare or under `sessions` / `session` — each with its worktree: `worktreePath`, else `cwd`.
 */
export const parseSessionRows = (text: string | undefined): { sessionId: string; worktree: string }[] => {
  const body = parseJson(text) as unknown
  const holder = body && typeof body === 'object' && !Array.isArray(body) ? (body as Record<string, unknown>) : null
  const raw = Array.isArray(body) ? body : Array.isArray(holder?.sessions) ? holder.sessions : holder?.session ?? holder
  const rows = (Array.isArray(raw) ? raw : raw ? [raw] : []) as unknown[]
  return rows
    .filter((r): r is Record<string, unknown> => !!r && typeof r === 'object')
    .map(r => ({ sessionId: asText(r.sessionId), worktree: asText(r.worktreePath) || asText(r.cwd) }))
    .filter(r => r.sessionId !== '')
}

/** One path, however spelled: case, `/` against `\` and a trailing slash do not count. Empty never matches. */
export const sameWorktree = (a: string | undefined, b: string | undefined) => {
  const norm = (p: string | undefined) => (p ?? '').trim().replace(/\\/g, '/').replace(/\/+$/, '').toLowerCase()
  return norm(a) !== '' && norm(a) === norm(b)
}

export type RunView = { id: string; flow: string; status: string; stages: string[]; stageStatus: string[] }

/**
 * The run a list_runs or get_run answered for a worktree: list_runs answers `{ runs }` newest
 * first, get_run one run, bare or under `run`. The run in progress wins, else the newest.
 */
export const parseRuns = (text: string | undefined): RunView | null => {
  const body = parseJson(text) as Record<string, unknown> | unknown[] | null
  if (!body || typeof body !== 'object') return null
  const holder = body as Record<string, unknown>
  const raw = Array.isArray(body) ? body : Array.isArray(holder.runs) ? holder.runs : [holder.run ?? holder]
  const runs = (raw as unknown[]).filter(
    (r): r is Record<string, unknown> => !!r && typeof r === 'object' && Array.isArray((r as { stages?: unknown }).stages),
  )
  const run = runs.find(r => normStatus(r.status) === 'inprogress') ?? runs[0]
  if (!run) return null
  const list = (run.stages as unknown[]).filter(s => stageName(s) !== '')
  return {
    id: String(run.id ?? run.runId ?? ''),
    flow: asText(run.skillId),
    status: asText(run.status),
    stages: parseStages(list),
    stageStatus: list.map(s => (s && typeof s === 'object' ? asText((s as { status?: unknown }).status) : '') || 'pending'),
  }
}

/**
 * The session's latest word from a list_events answer: a header, then messages each opening
 * with `[role] `, maybe over several lines. The last assistant message with text beside its
 * `(called …)` lines is the return; no such message, no return.
 */
export const parseEventsReturn = (text: string | undefined): { state: WorkerState; note: string } | null => {
  const messages: { role: string; lines: string[] }[] = []
  const header: string[] = []
  for (const line of (text ?? '').split(/\r?\n/)) {
    // Three letters at least, so a markdown checkbox `[x]` inside a message stays its text.
    const m = /^\[([a-z_]{3,})\](?: (.*))?$/.exec(line)
    if (m) messages.push({ role: m[1] ?? '', lines: [m[2] ?? ''] })
    else if (messages.length === 0) header.push(line)
    else messages.at(-1)?.lines.push(line)
  }
  // A session still in its turn has not returned yet: its latest text is no return line.
  if (header.some(l => /\(running\)/i.test(l))) return null
  for (const msg of messages.reverse()) {
    if (msg.role !== 'assistant') continue
    const said = msg.lines.filter(l => !/^\s*\(called [^)]*\)\s*$/.test(l)).join('\n').trim()
    if (said !== '') return parseReturn(said)
  }
  return null
}

export const newWorker = (sessionId: string, b: NonNullable<ReturnType<typeof parseBrief>>): Worker => ({
  sessionId,
  worktree: '',
  entryId: b.entryId,
  n: b.n,
  title: b.title,
  state: 'running',
  note: '',
  flow: '',
  runIds: [],
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
