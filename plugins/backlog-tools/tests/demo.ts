// A scripted execute-plan run, one tick per stage, for the tests: three entries in parallel,
// one reaching the gate, one landing a pull request that merges, one blocked, a manual step,
// and an entry that waits on the merged one.
import type { Plan, Worker, WorkerState } from '../types'

/** flow-code's stages, as start_run declares them. */
const STAGES = [
  'Update Base', 'Scope', 'Implement', 'Review', 'Build & Test', 'Verify',
  'Spec Check', 'Ready', 'Personal Validation', 'Create Pull Request', 'Report Back', 'Summary',
]

type Script = {
  n: number
  start: number | ((tick: number) => number | null)
  /** The stage it stops at, and what the sub-agent returns there. */
  stop: number
  outcome: Exclude<WorkerState, 'running'> | 'running'
  note: string
}

const MERGE_DELAY = 2
const SCRIPTS: Script[] = [
  { n: 1, start: 0, stop: 8, outcome: 'gate', note: 'review at http://localhost:5173 — check the new filter chip' },
  { n: 2, start: 0, stop: STAGES.length, outcome: 'pr', note: 'https://github.com/JSdotNet/Backlog/pull/9001 (draft)' },
  { n: 3, start: 1, stop: 4, outcome: 'blocked', note: 'build-test: 2 failing tests in Backlog.Domain.Tests' },
  // Entry 5 waits on entry 2 (after: 2): it starts once 2 has merged.
  { n: 5, start: t => (t >= mergeTick() + 1 ? mergeTick() + 1 : null), stop: 6, outcome: 'running', note: '' },
]

const TITLES = ['Add the filter chip', 'Persist the filter per pane', 'Sort by effort', 'Check the chip by hand', 'Show the filter in the roadmap']
const TYPES = ['prompt', 'prompt', 'prompt', 'test', 'prompt']

/** The tick entry 2's pull request merges: its run done, then MERGE_DELAY ticks. */
const mergeTick = () => 1 + STAGES.length + MERGE_DELAY

const startOf = (s: Script, tick: number) => (typeof s.start === 'number' ? s.start : s.start(tick))

/** Where the scripted run stands at `tick` (one tick ≈ one stage). */
export const demoAt = (tick: number): { plan: Plan; workers: Worker[]; isOver: boolean } => {
  const workers: Worker[] = []
  for (const s of SCRIPTS) {
    const start = startOf(s, tick)
    if (start === null || tick < start) continue
    const reached = Math.min(tick - start - 1, s.stop)
    const finished = s.outcome !== 'running' && tick - start - 1 > s.stop
    const stageStatus = STAGES.map((_, i) => {
      if (reached < 0) return 'pending'
      if (i < reached) return 'done'
      if (i === reached && i < STAGES.length) return finished && s.outcome === 'blocked' ? 'blocked' : 'in_progress'
      return 'pending'
    })
    workers.push({
      agentId: `demo-${s.n}`,
      entryId: `e${s.n}`,
      n: s.n,
      title: TITLES[s.n - 1] ?? '',
      state: finished ? s.outcome : 'running',
      note: finished ? s.note : '',
      flow: reached >= 0 ? 'flow-code' : '',
      runId: reached >= 0 ? `run-${s.n}` : '',
      stages: reached >= 0 ? STAGES : [],
      stageStatus: reached >= 0 ? stageStatus : [],
      runStatus: finished ? (s.outcome === 'pr' ? 'done' : s.outcome === 'blocked' ? 'blocked' : 'in_progress') : 'in_progress',
    })
  }
  const entries = TITLES.map((title, i) => ({
    id: `e${i + 1}`,
    n: i + 1,
    title,
    type: TYPES[i] ?? 'prompt',
    status: i + 1 === 2 && tick >= mergeTick() ? 'done' : workers.some(w => w.n === i + 1) ? 'inprogress' : 'ready',
  }))
  return { plan: { tag: '+demo-filter-chips', entries }, workers, isOver: tick >= mergeTick() + 1 + 7 }
}
