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

/** One execute-plan sub-agent and the delivery-flow run it reported. */
export type Worker = {
  agentId: string
  entryId: string
  n: number
  title: string
  state: WorkerState
  note: string
  flow: string
  runId: string
  stages: string[]
  stageStatus: string[]
  runStatus: string
}

declare module 'claude-code' {
  interface PluginState {
    'backlog-tools': {
      plan: Plan | null
      workers: Worker[]
      /** A nested agent (a flow-runner, a phase runner) → the agent that spawned it. */
      parents: Record<string, string>
    }
  }
}
