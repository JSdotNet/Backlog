/** One entry of the plan, as get_plan_items answered it and transition moved it since. */
export type PlanEntry = {
  id: string
  n: number
  title: string
  type: string
  status: string
}

export type Plan = { tag: string; entries: PlanEntry[] }

export type WorkerState = 'running' | 'gate' | 'pr' | 'blocked' | 'done-before' | 'stopped'

/** One execute-plan entry session and the delivery-flow run its worktree shows. */
export type Worker = {
  /** The session's `local_…` id, as start_session answered it. */
  sessionId: string
  /** The session's worktree, as list_sessions answered it; '' until known. */
  worktree: string
  entryId: string
  n: number
  title: string
  state: WorkerState
  note: string
  flow: string
  /** The id of the run in hand, as list_runs answered it; none until a surface answered. */
  runIds: string[]
  stages: string[]
  stageStatus: string[]
  runStatus: string
}

declare module 'claude-code' {
  interface PluginState {
    'backlog-tools': {
      plan: Plan | null
      workers: Worker[]
    }
  }
}
