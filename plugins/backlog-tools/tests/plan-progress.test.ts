import { expect, test } from 'claude-code/testing'

import { kindOf, parseBrief, parsePlan, parseReturn, parseRunId, surfaceOp, tally } from '../hooks/parse'
import { abbrev, alsoLine, bandRows, columnsOf, liveRows, matrixLayout, rowsOf } from '../hooks/view'
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

test('recognises the brief, the surface tools and the returns', () => {
  expect(parseBrief(BRIEF)).toEqual({ tag: '+demo', n: 2, title: 'Second prompt', entryId: 'b2' })
  expect(surfaceOp('mcp__plugin_delivery-surface-backlog_delivery-surface-backlog__update_stage')).toBe('update_stage')
  expect(surfaceOp('mcp__backlog__start_run')).toBe('start_run')
  expect(surfaceOp('mcp__backlog__transition')).toBe(null)
  expect(parseRunId('{"runId":"r-9","resumed":false}')).toBe('r-9')
  expect(parseReturn('pr https://github.com/o/r/pull/7')).toEqual({ state: 'pr', note: 'https://github.com/o/r/pull/7' })
  expect(parseReturn('Some notes.\n\nblocked implement: empty diff').state).toBe('blocked')
  expect(parseReturn('gate\nReview at http://localhost:5000').state).toBe('gate')
  expect(parseBrief(BRIEF.replace('`2 - Second', '`2 - 2 - Second'))?.title).toBe('Second prompt')
})

test('tallies entries with workers overriding stored status', () => {
  const plan = parsePlan('+demo', PLAN)
  const w = { agentId: 'ag', entryId: 'b2', n: 2, title: 'Second prompt', state: 'gate' as const, note: '', flow: '', runId: '', stages: [], stageStatus: [], runStatus: '' }
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
  const w = { agentId: 'ag', entryId: 'b2', n: 2, title: 'Second prompt', state: 'gate' as const, note: '', flow: 'flow-code', runId: 'r', stages: ['Scope', 'Implement'], stageStatus: ['done', 'in_progress'], runStatus: '' }
  const rows = rowsOf(plan, [w])
  expect(liveRows(rows).map(r => r.n)).toEqual([2])
  expect(alsoLine(rows)).toBe('✓ 1 · ✎ 1')
  const many = Array.from({ length: 9 }, (_, i) => ({ ...rows[1]!, id: `x${i}` }))
  expect(bandRows(many, 5)).toEqual({ shown: many.slice(0, 3), more: 6 })
  expect(bandRows(many.slice(0, 4), 5).more).toBe(0)
  expect(columnsOf(rows)).toEqual(['Scope', 'Implement'])
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

test('the scripted run ends with a merge, a gate, a block and a manual step', () => {
  let tick = 0
  while (!demoAt(tick).isOver) tick += 1
  const end = demoAt(tick)
  const c = tally(end.plan, end.workers)
  expect([c.total, c.done, c.gate, c.blocked, c.manual, c.running]).toEqual([5, 1, 1, 1, 1, 1])
})
