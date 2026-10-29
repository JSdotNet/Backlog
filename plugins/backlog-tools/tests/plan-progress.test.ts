import { expect, test } from 'claude-code/testing'
import type { On } from 'claude-code'

import type { Worker } from '../types'

import { asId, asIndex, kindOf, parseBrief, parsePlan, parseReturn, parseRunId, parseStages, surfaceOp, tally } from '../hooks/parse'
import { BLOCK, CELL, abbrev, alsoLine, bandRows, cellsOf, columnsOf, currentStage, liveRows, matrixLayout, matrixSvg, rowsOf, stageStatesOf } from '../hooks/view'
import { demoAt } from './demo'

const PLAN = JSON.stringify({
  planId: '+demo',
  count: 3,
  // The wire tokens EnumMap.ToWire writes, and titles that carry their step number.
  entries: [
    { id: 'a1', title: '1 - First prompt', type: 'prompt', status: 'done' },
    { id: 'b2', title: '2 - Second prompt', type: 'prompt', status: 'in_progress' },
    { id: 'c3', title: '4 - Check by hand', type: 'test', status: 'ready' },
  ],
})

const BRIEF =
  'You run one entry of the Backlog plan `+demo`: `2 - Second prompt` (id `b2`) in `JSdotNet/Backlog`,\n**attended**.'

test('reads the plan, numbering each entry by its title', () => {
  const plan = parsePlan('+demo', PLAN)
  expect(plan?.entries.map(e => [e.n, e.title, e.status])).toEqual([
    [1, 'First prompt', 'done'],
    [2, 'Second prompt', 'inprogress'],
    [4, 'Check by hand', 'ready'],
  ])
})

test('numbers a follow-up filed under the plan tag after the highest step, never reusing one', () => {
  const mixed = JSON.stringify({
    planId: '+demo',
    entries: [
      { id: 'a1', title: '1 - First prompt', type: 'prompt', status: 'done' },
      { id: 'f1', title: 'Follow-up from review', type: 'prompt', status: 'ready' },
      { id: 'b2', title: '2 - Second prompt', type: 'prompt', status: 'ready' },
      { id: 'f2', title: 'Another follow-up', type: 'prompt', status: 'ready' },
      { id: 'c3', title: '3 - Check by hand', type: 'test', status: 'ready' },
    ],
  })
  const plan = parsePlan('+demo', mixed)
  expect(plan?.entries.map(e => [e.id, e.n, e.title])).toEqual([
    ['a1', 1, 'First prompt'],
    ['f1', 4, 'Follow-up from review'],
    ['b2', 2, 'Second prompt'],
    ['f2', 5, 'Another follow-up'],
    ['c3', 3, 'Check by hand'],
  ])
})

test('recognises the brief, the surface tools and the returns', () => {
  expect(parseBrief(BRIEF)).toEqual({ tag: '+demo', n: 2, title: 'Second prompt', entryId: 'b2' })
  expect(surfaceOp('mcp__plugin_delivery-surface-backlog_delivery-surface-backlog__update_stage')).toBe('update_stage')
  expect(surfaceOp('mcp__backlog__start_run')).toBe('start_run')
  expect(surfaceOp('mcp__backlog__transition')).toBe(null)
  expect(parseRunId('{"runId":"r-9","resumed":false}')).toBe('r-9')
  expect(parseReturn('pr https://github.com/o/r/pull/7')).toEqual({ state: 'pr', note: 'https://github.com/o/r/pull/7' })
  expect(parseReturn('Some notes.\n\nblocked implement: empty diff').state).toBe('blocked')
  expect(parseReturn('gate\nReview at http://localhost:5000').state).toBe('gate')
  expect(parseReturn('gate run-7 D:/dm/b2\nReview at http://localhost:5000')).toEqual({ state: 'gate', note: 'run-7 D:/dm/b2' })
  expect(parseBrief(BRIEF.replace('`2 - Second', '`2 - 2 - Second'))?.title).toBe('Second prompt')
})

test('reads a declared stage list in any shape a worker sends it', () => {
  const stages = ['scope', 'implement', 'build-test']
  expect(parseStages(stages)).toEqual(stages)
  expect(parseStages('["scope","implement","build-test"]')).toEqual(stages)
  expect(parseStages(' scope, implement ,,build-test ')).toEqual(stages)
  expect(parseStages([{ name: 'scope' }, { title: 'implement' }, { name: 'build-test' }])).toEqual(stages)
  for (const other of [undefined, null, 3, {}, '', '{"stages":1}', [1, 2]]) expect(parseStages(other)).toEqual([])
})

test('compares a stage index and a run id after coercion', () => {
  expect([asIndex(2), asIndex('0'), asIndex(' 3 ')]).toEqual([2, 0, 3])
  for (const other of [undefined, null, '', 'two', '1.5', 1.5, {}]) expect(asIndex(other)).toBeNaN()
  expect([asId('r1'), asId(' r1 '), asId(42)]).toEqual(['r1', 'r1', '42'])
  for (const other of [undefined, null, {}, [], true]) expect(asId(other)).toBe('')
})

test('tallies entries with workers overriding stored status', () => {
  const plan = parsePlan('+demo', PLAN)
  const w = { agentId: 'ag', entryId: 'b2', n: 2, title: 'Second prompt', state: 'gate' as const, note: '', flow: '', runIds: [], stages: [], stageStatus: [], runStatus: '' }
  expect(kindOf(plan?.entries[2], undefined)).toBe('manual')
  const c = tally(plan, [w])
  expect([c.total, c.done, c.gate, c.manual]).toEqual([3, 1, 1, 1])
  // A plan read from a file never passed get_plan_items: the workers are the count.
  const fromFile = tally({ tag: '+demo', entries: [] }, [w, { ...w, entryId: 'x', state: 'stopped' }])
  expect([fromFile.total, fromFile.gate, fromFile.blocked]).toEqual([2, 1, 1])
})

test('the pane follows a worker through its flow stages', async ($, on) => {
  on('tool.call', ($, e) =>
    e.tool === 'mcp__backlog__get_plan_items'
      ? { result: PLAN, text: PLAN }
      : { result: '{"runId":"r1"}', text: '{"runId":"r1"}' },
  )
  let spawned = 0
  on('agent.spawn', () => ({ model: 'x', agentId: ++spawned === 1 ? 'worker-1' : 'runner-1' }))
  // What the plugins beneath the band draw: nothing here.
  on('ui.render', () => ({ type: 'Box' }) as never)

  await $.tool.call({ tool: 'mcp__backlog__get_plan_items', planId: '+demo' } as never)
  await $.agent.spawn({ prompt: BRIEF, description: 'entry 2' } as never)
  await $.tool.call({
    tool: 'mcp__backlog__start_run',
    agentId: 'worker-1',
    worktree: 'D:/w',
    skillId: 'flow-code',
    title: 'Second prompt',
    stages: ['scope', 'implement', 'build-test', 'personal-validation'],
  } as never)
  await $.tool.call({ tool: 'mcp__backlog__update_stage', agentId: 'worker-1', worktree: 'D:/w', runId: 'r1', stageIndex: 0, status: 'done' } as never)
  // A phase the worker's flow delegates reports from a nested agent's loop.
  await $.agent.spawn({ prompt: 'Implement slice 1.', description: 'implement', parentAgentId: 'worker-1' } as never)
  await $.tool.call({ tool: 'mcp__backlog__update_stage', agentId: 'runner-1', worktree: 'D:/w', runId: 'r1', stageIndex: 1, status: 'in_progress' } as never)

  // The details on a terminal: the whole plan, a column per stage under its short name.
  const pane = await $.ui.mount({ plugin: 'backlog-tools', surface: 'terminal', component: 'Pane', requestId: 'plan-progress', props: { bodyColumns: 60 } } as never)
  expect(await pane.find({ type: 'Text', text: /1\/3 done/ })).toBeDefined()
  expect(await pane.find({ type: 'Text', text: /^scp$/ })).toBeDefined()
  expect(await pane.find({ type: 'Text', text: /^pv$/ })).toBeDefined()
  expect(await pane.find({ type: 'Text', text: /^▶$/ })).toBeDefined()
  expect(await pane.find({ type: 'Text', text: /4 Check by hand/ })).toBeDefined()
  await pane.unmount()

  // The details where SVG draws: design C as one drawing.
  const drawn = await $.ui.mount({ plugin: 'backlog-tools', surface: 'desktop', component: 'Pane', requestId: 'plan-progress', props: { bodyColumns: 60 } } as never)
  const svg = (await drawn.find({ type: 'Svg' } as never)) as unknown as { props: { source: string; alt: string } } | undefined
  expect(svg?.props.alt).toContain('1/3 done')
  expect(svg?.props.source).toContain('>scp<')
  expect(svg?.props.source).toContain('fill="#56c8e8"')
  await drawn.unmount()

  for (const surface of ['terminal', 'desktop'] as const) {

    // The band: only the entry in flow, with the stage it stands at.
    const band = await $.ui.mount({ plugin: 'backlog-tools', surface, component: 'AbovePrompt', props: { hasSurvey: false, isWorking: true, maxRows: 10, bodyColumns: 100 } } as never)
    expect(await band.find({ type: 'Text', text: /execute-plan/ })).toBeDefined()
    expect(await band.find({ type: 'Text', text: /2 Second prompt/ })).toBeDefined()
    expect(await band.find({ type: 'Text', text: /^implement$/ })).toBeDefined()
    expect(await band.find({ type: 'Text', text: /First prompt/ })).toBeUndefined()
    expect(await band.find({ type: 'Button', key: 'details' } as never)).toBeDefined()
    await band.unmount()
  }
})

test('the band stays away with nothing in flow', async ($, on) => {
  on('ui.render', () => ({ type: 'Box' }) as never)
  const ui = await $.ui.mount({ plugin: 'backlog-tools', surface: 'terminal', component: 'AbovePrompt', props: { hasSurvey: false, isWorking: false, maxRows: 10, bodyColumns: 100 } } as never)
  expect(await ui.find({ type: 'Text', text: /execute-plan/ })).toBeUndefined()
  await ui.unmount()
})

test('lays out the matrix and the band from the rows', () => {
  expect([abbrev('Personal Validation'), abbrev('Build & Test'), abbrev('phase-verify')]).toEqual(['pv', 'b&t', 'ver'])
  expect([abbrev('build-test'), abbrev('phase-create-pr'), abbrev('phase-check-review'), abbrev('Hand-off')]).toEqual(['b&t', 'pr', 'c&r', 'han'])
  expect(matrixLayout(80, 12)).toEqual({ cell: 3, gap: 1, hasHeadings: true, titleWidth: 29 })
  expect(matrixLayout(56, 12)).toEqual({ cell: 2, gap: 0, hasHeadings: false, titleWidth: 29 })
  const plan = parsePlan('+demo', PLAN)
  const w = { agentId: 'ag', entryId: 'b2', n: 2, title: 'Second prompt', state: 'gate' as const, note: '', flow: 'flow-code', runIds: ['r'], stages: ['Scope', 'Implement'], stageStatus: ['done', 'in_progress'], runStatus: '' }
  const rows = rowsOf(plan, [w])
  expect(liveRows(rows).map(r => r.n)).toEqual([2])
  expect(alsoLine(rows)).toBe('✓ 1 · ✎ 1')
  const many = Array.from({ length: 9 }, (_, i) => ({ ...rows[1]!, id: `x${i}` }))
  expect(bandRows(many, 5)).toEqual({ shown: many.slice(0, 3), more: 6 })
  expect(bandRows(many.slice(0, 4), 5).more).toBe(0)
  expect(columnsOf(rows)).toEqual(['Scope', 'Implement'])
})

test('a block the run moved past is parked, and only a block it stopped at is a failure', () => {
  const stages = ['Ready', 'Personal Validation', 'Create Pull Request', 'Summary']
  const base = { agentId: 'ag', entryId: 'b2', n: 2, title: 'Second prompt', note: '', flow: 'flow-code', runIds: ['r'], stages, runStatus: '' }
  // Unattended: Personal Validation moved onto the draft, reported as blocked, and the flow went on.
  const movedOn = { ...base, state: 'running' as const, stageStatus: ['done', 'blocked', 'in_progress', 'pending'] }
  expect(stageStatesOf(movedOn)).toEqual(['done', 'parked', 'in_progress', 'pending'])
  expect(currentStage(movedOn)).toBe('Create Pull Request')
  expect(cellsOf(movedOn)[1]).toEqual(CELL.parked!)
  // Returned with its draft before the later stages were reported.
  const drafted = { ...base, state: 'pr' as const, stageStatus: ['done', 'blocked', 'pending', 'pending'] }
  expect(stageStatesOf(drafted)[1]).toBe('parked')
  expect(stageStatesOf({ ...drafted, state: 'running', runStatus: 'parked' })[1]).toBe('parked')
  // A block the run stopped at stays the red cross.
  const failed = { ...base, state: 'blocked' as const, stageStatus: ['blocked', 'pending', 'pending', 'pending'] }
  expect(stageStatesOf(failed)[0]).toBe('blocked')
  expect(currentStage(failed)).toBe('Ready')
  // A run that failed still closes Summary, and a red stage skips the next on its way back.
  expect(stageStatesOf({ ...failed, stageStatus: ['done', 'blocked', 'skipped', 'done'] })[1]).toBe('blocked')
  expect(stageStatesOf({ ...movedOn, stageStatus: ['blocked', 'skipped', 'pending', 'pending'] })[0]).toBe('blocked')
  const svg = matrixSvg('+demo', '0/2 done', rowsOf(null, [movedOn, { ...failed, entryId: 'c3' }]), stages)
  const blocks = (bg: string) => svg.split(`height="18" rx="3" fill="${bg}"`).length - 1
  expect([blocks(BLOCK.parked!.bg), blocks(BLOCK.blocked!.bg)]).toEqual([1, 1])
})

test('a brief for another plan starts the pane over', async ($, on) => {
  on('tool.call', () => ({ result: PLAN, text: PLAN }))
  let spawned = 0
  on('agent.spawn', () => ({ model: 'x', agentId: `w${++spawned}` }))

  await $.tool.call({ tool: 'mcp__backlog__get_plan_items', planId: '+demo' } as never)
  await $.agent.spawn({ prompt: BRIEF } as never)
  await $.agent.spawn({ prompt: BRIEF.replace('+demo', '+other').replace('(id `b2`)', '(id `z9`)') } as never)

  const ui = await $.ui.mount({ plugin: 'backlog-tools', surface: 'terminal', component: 'Pane', requestId: 'plan-progress', props: {} } as never)
  expect(await ui.find({ type: 'Text', text: /\+other/ })).toBeDefined()
  expect(await ui.find({ type: 'Text', text: /0\/1 done/ })).toBeDefined()
  await ui.unmount()
})

// The surface calls the pane mirrors are what the worker said, not what a surface stored: a call
// the surface rejected still counts, and a call the host denied never ran.
const STAGES = ['scope', 'implement', 'build-test', 'personal-validation']
// The run status is drawn nowhere: a plugin beside this one reads the state it declares instead.
const PEEK = {
  name: 'peek',
  register: (on: On) => {
    on('command.run', { command: 'peek-workers' }, async $ => {
      const { value } = await $.state.get({ plugin: 'backlog-tools', key: 'workers' })
      return { text: JSON.stringify(value ?? []) }
    })
  },
}
const workerOf = async ($: { command: { run: (e: never) => Promise<{ text?: string }> } }) =>
  (JSON.parse((await $.command.run({ command: 'peek-workers', args: '' } as never)).text ?? '[]') as Worker[])[0]
// The session's state outlives a reload of the plugin's code: after /reload-plugins the module
// meets the workers 0.17.0 stored, with one `runId` and no `runIds`. This plugin turns the first
// worker written into one 0.17.0 left mid-run, its start_run confirmed as `r1`.
const LEGACY = {
  name: 'legacy',
  register: (on: On) => {
    let seeded = false
    on('state.set', { plugin: 'backlog-tools', key: 'workers' }, ($, e, next) => {
      if (seeded) return next(e)
      seeded = true
      const stages = ['scope', 'implement', 'build-test', 'personal-validation']
      const value = e.value.map(({ runIds: _, ...w }) => ({
        ...w, flow: 'flow-code', runId: 'r1', stages, stageStatus: stages.map(() => 'pending'), runStatus: 'in_progress',
      }))
      return next({ ...e, value } as never)
    })
  },
}
const REJECTED = { isError: true, result: null, text: "An error occurred invoking 'start_run'." } as const
const runIdAnswer = (id: string) => ({ result: `{"runId":"${id}"}`, text: `{"runId":"${id}"}` })

test('a start_run the surface rejected still draws the entry’s stages, and its reports move them', async ($, on) => {
  // The Backlog surface rejects every lifecycle call, as it did a start_run without a worktree.
  on('tool.call', ($, e) => (e.tool === 'mcp__backlog__get_plan_items' ? { result: PLAN, text: PLAN } : REJECTED) as never)
  on('agent.spawn', () => ({ model: 'x', agentId: 'worker-1' }))
  on('ui.render', () => ({ type: 'Box' }) as never)

  await $.tool.call({ tool: 'mcp__backlog__get_plan_items', planId: '+demo' } as never)
  await $.agent.spawn({ prompt: BRIEF, description: 'entry 2' } as never)
  // The observed call: stages JSON-encoded as a string, resume as a string, no worktree.
  await $.tool.call({
    tool: 'mcp__backlog__start_run',
    agentId: 'worker-1',
    skillId: 'flow-code',
    title: 'Second prompt',
    stages: JSON.stringify(STAGES),
    resume: 'false',
  } as never)

  const pane = await $.ui.mount({ plugin: 'backlog-tools', surface: 'terminal', component: 'Pane', requestId: 'plan-progress', props: { bodyColumns: 60 } } as never)
  for (const heading of [/^scp$/, /^imp$/, /^b&t$/, /^pv$/]) expect(await pane.find({ type: 'Text', text: heading })).toBeDefined()
  expect(await pane.findAll({ type: 'Text', text: /^·$/ })).toHaveLength(STAGES.length)
  await pane.unmount()

  // No surface confirmed the run, so a report without a run id is the worker's current run's.
  await $.tool.call({ tool: 'mcp__backlog__update_stage', agentId: 'worker-1', stageIndex: 1, status: 'in_progress' } as never)

  for (const surface of ['terminal', 'desktop'] as const) {
    const band = await $.ui.mount({ plugin: 'backlog-tools', surface, component: 'AbovePrompt', props: { hasSurvey: false, isWorking: true, maxRows: 10, bodyColumns: 100 } } as never)
    expect(await band.find({ type: 'Text', text: /^●$/ })).toBeDefined()
    expect(await band.find({ type: 'Text', text: /^implement$/ })).toBeDefined()
    await band.unmount()
  }
})

test('a finish_run the surface rejected still records how the run ended', { plugins: [PEEK] }, async ($, on) => {
  on('tool.call', () => REJECTED as never)
  on('agent.spawn', () => ({ model: 'x', agentId: 'worker-1' }))

  await $.agent.spawn({ prompt: BRIEF } as never)
  await $.tool.call({ tool: 'mcp__backlog__start_run', agentId: 'worker-1', skillId: 'flow-code', stages: STAGES } as never)
  await $.tool.call({ tool: 'mcp__backlog__finish_run', agentId: 'worker-1', status: 'done' } as never)

  expect((await workerOf($))?.runStatus).toBe('done')
})

test('once a surface confirmed the run, another run’s reports leave it alone', { plugins: [PEEK] }, async ($, on) => {
  on('tool.call', () => runIdAnswer('r1'))
  on('agent.spawn', () => ({ model: 'x', agentId: 'worker-1' }))

  await $.agent.spawn({ prompt: BRIEF } as never)
  await $.tool.call({ tool: 'mcp__backlog__start_run', agentId: 'worker-1', skillId: 'flow-code', stages: STAGES } as never)
  await $.tool.call({ tool: 'mcp__backlog__update_stage', agentId: 'worker-1', runId: 'r2', stageIndex: 0, status: 'done' } as never)
  await $.tool.call({ tool: 'mcp__backlog__finish_run', agentId: 'worker-1', runId: 'r2', status: 'failed' } as never)

  const w = await workerOf($)
  expect([w?.stageStatus, w?.runStatus]).toEqual([['pending', 'pending', 'pending', 'pending'], 'in_progress'])
})

test('a report the host denied moves nothing', { plugins: [PEEK] }, async ($, on) => {
  on('tool.call', ($, e) => (e.tool === 'mcp__backlog__update_stage' ? { deny: 'refused by the host' } : runIdAnswer('r1')) as never)
  on('agent.spawn', () => ({ model: 'x', agentId: 'worker-1' }))

  await $.agent.spawn({ prompt: BRIEF } as never)
  await $.tool.call({ tool: 'mcp__backlog__start_run', agentId: 'worker-1', skillId: 'flow-code', stages: STAGES } as never)
  await $.tool.call({ tool: 'mcp__backlog__update_stage', agentId: 'worker-1', runId: 'r1', stageIndex: 0, status: 'done' } as never)

  expect((await workerOf($))?.stageStatus).toEqual(['pending', 'pending', 'pending', 'pending'])
})

test('one run fanned out to two surfaces keeps one set of stages, moved by either run id', { plugins: [PEEK] }, async ($, on) => {
  // Each bound surface answers the same run with its own id.
  on('tool.call', ($, e) => runIdAnswer(String(e.tool).includes('dashboard') ? 'r2' : 'r1'))
  on('agent.spawn', () => ({ model: 'x', agentId: 'worker-1' }))

  await $.agent.spawn({ prompt: BRIEF } as never)
  const start = { agentId: 'worker-1', skillId: 'flow-code', stages: STAGES }
  await $.tool.call({ tool: 'mcp__backlog__start_run', ...start } as never)
  await $.tool.call({ tool: 'mcp__backlog__update_stage', agentId: 'worker-1', runId: 'r1', stageIndex: 0, status: 'done' } as never)
  await $.tool.call({ tool: 'mcp__dashboard__start_run', ...start } as never)
  await $.tool.call({ tool: 'mcp__dashboard__update_stage', agentId: 'worker-1', runId: 'r2', stageIndex: 1, status: 'in_progress' } as never)
  await $.tool.call({ tool: 'mcp__backlog__update_stage', agentId: 'worker-1', runId: 'r1', stageIndex: 2, status: 'skipped' } as never)

  expect((await workerOf($))?.stageStatus).toEqual(['done', 'in_progress', 'skipped', 'pending'])
})

test('a worker stored by 0.17.0 still moves after a reload', { plugins: [PEEK, LEGACY] }, async ($, on) => {
  on('tool.call', () => runIdAnswer('r1'))
  on('agent.spawn', () => ({ model: 'x', agentId: 'worker-1' }))

  await $.agent.spawn({ prompt: BRIEF } as never)
  await $.tool.call({ tool: 'mcp__backlog__update_stage', agentId: 'worker-1', runId: 'r1', stageIndex: 0, status: 'done' } as never)
  // The same run declared again (a reattach): it keeps the statuses and takes the answered id.
  await $.tool.call({ tool: 'mcp__backlog__start_run', agentId: 'worker-1', skillId: 'flow-code', stages: STAGES } as never)
  await $.tool.call({ tool: 'mcp__backlog__update_stage', agentId: 'worker-1', runId: 'r1', stageIndex: 1, status: 'in_progress' } as never)
  await $.tool.call({ tool: 'mcp__backlog__finish_run', agentId: 'worker-1', runId: 'r1', status: 'done' } as never)

  const w = await workerOf($)
  expect([w?.stageStatus, w?.runStatus, w?.runIds]).toEqual([['done', 'in_progress', 'pending', 'pending'], 'done', ['r1']])
})

test('the scripted run ends with a merge, a gate, a block and a manual step', () => {
  let tick = 0
  while (!demoAt(tick).isOver) tick += 1
  const end = demoAt(tick)
  const c = tally(end.plan, end.workers)
  expect([c.total, c.done, c.gate, c.blocked, c.manual, c.running]).toEqual([5, 1, 1, 1, 1, 1])
})
