import { expect, test } from 'claude-code/testing'
import type { On } from 'claude-code'

import type { Worker } from '../types'

import {
  isEvents,
  isRuns,
  isSessionRows,
  isStartSession,
  kindOf,
  messageTarget,
  parseBrief,
  parseEventsReturn,
  parsePlan,
  parseReturn,
  parseRuns,
  parseSessionId,
  parseSessionRows,
  parseStages,
  sameWorktree,
  tally,
} from '../hooks/parse'
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

// What the host's session tools and a delivery surface answer about an entry session.
const SESSION = 'local_7f3a-b2c1'
const WORKTREE = 'D:\\Repos\\Backlog\\.claude\\worktrees\\entry-b2'
const STARTED = `Started session ${SESSION} ("+demo:2 - Second prompt"). It runs in the background.`
const SESSIONS = JSON.stringify([
  { sessionId: 'local_other', title: 'Something else', cwd: 'D:\\Repos\\Other', isRunning: false },
  { sessionId: SESSION, title: '+demo:2 - Second prompt', cwd: WORKTREE, isRunning: true },
])
const runsAnswer = (worktree: string, stages: { name: string; status: string }[]) =>
  JSON.stringify({ worktree, count: 1, runs: [{ id: 'run-1', skillId: 'flow-code', status: 'in_progress', stages }] })
const RUNS = runsAnswer('D:/Repos/Backlog/.claude/worktrees/entry-b2', [
  { name: 'Scope', status: 'done' },
  { name: 'Implement', status: 'in_progress' },
  { name: 'Build & Test', status: 'pending' },
  { name: 'Personal Validation', status: 'pending' },
])
const EVENTS_AT_GATE = [
  `Session "+demo:2 - Second prompt" (idle) — showing 5 events`,
  '[user] You run one entry of the Backlog plan `+demo`.',
  '[assistant] Starting on the entry.',
  '(called Bash)',
  '[assistant] gate',
  'Review at http://localhost:5173 — check the new filter chip.',
  '[assistant] (called Bash)',
].join('\n')

type Answer = { result: unknown; text: string }
const answer = (text: string): Answer => ({ result: text, text })
/** The host and the surfaces, answering each call the way they do. */
const host = (e: { tool: string }): Answer => {
  if (e.tool === 'mcp__backlog__get_plan_items') return answer(PLAN)
  if (isStartSession(e.tool)) return answer(STARTED)
  if (isSessionRows(e.tool)) return answer(SESSIONS)
  if (isRuns(e.tool)) return answer(RUNS)
  if (isEvents(e.tool)) return answer(EVENTS_AT_GATE)
  return answer('ok')
}

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

test('recognises the brief, the session tools and the returns', () => {
  expect(parseBrief(BRIEF)).toEqual({ tag: '+demo', n: 2, title: 'Second prompt', entryId: 'b2' })
  expect(isStartSession('mcp__ccd_session__start_session')).toBe(true)
  expect([isSessionRows('mcp__ccd_session__list_sessions'), isSessionRows('mcp__x__get_session')]).toEqual([true, true])
  expect(isRuns('mcp__plugin_delivery-surface-backlog_delivery-surface-backlog__list_runs')).toBe(true)
  expect([isRuns('mcp__dashboard__get_run'), isRuns('mcp__backlog__start_run')]).toEqual([true, false])
  expect([isEvents('mcp__ccd_session__list_events'), isStartSession('start_session')]).toEqual([true, false])
  expect(messageTarget('SendMessage', { to: 'local_a' })).toBe('local_a')
  expect(messageTarget('mcp__ccd_session__send_message', { session_id: 'local_b', to: 'local_x' })).toBe('local_b')
  expect([messageTarget('mcp__x__send_message', { to: 'local_a' }), messageTarget('send_message', { session_id: 'local_a' })]).toEqual(['', ''])
  expect(parseReturn('pr https://github.com/o/r/pull/7')).toEqual({ state: 'pr', note: 'https://github.com/o/r/pull/7' })
  expect(parseReturn('Some notes.\n\nblocked implement: empty diff').state).toBe('blocked')
  expect(parseReturn('gate\nReview at http://localhost:5000').state).toBe('gate')
  expect(parseBrief(BRIEF.replace('`2 - Second', '`2 - 2 - Second'))?.title).toBe('Second prompt')
})

test('reads a stage list in any shape a run spells it', () => {
  const stages = ['scope', 'implement', 'build-test']
  expect(parseStages(stages)).toEqual(stages)
  expect(parseStages('["scope","implement","build-test"]')).toEqual(stages)
  expect(parseStages(' scope, implement ,,build-test ')).toEqual(stages)
  expect(parseStages([{ name: 'scope' }, { title: 'implement' }, { name: 'build-test' }])).toEqual(stages)
  for (const other of [undefined, null, 3, {}, '', '{"stages":1}', [1, 2]]) expect(parseStages(other)).toEqual([])
})

test('takes the session id start_session answered', () => {
  expect(parseSessionId(STARTED)).toBe(SESSION)
  expect(parseSessionId('{"sessionId":"local_abc-123","title":"x"}')).toBe('local_abc-123')
  // A JSON answer's own id wins over a parent session it names first.
  expect(parseSessionId('{"parentSessionId":"local_p","sessionId":"local_s1"}')).toBe('local_s1')
  expect(parseSessionId('Started: {"parent":"local_p","session_id":"local_s2"}')).toBe('local_s2')
  // Its own id field, not a local id: no session — a local id beside it is another session's.
  expect(parseSessionId('{"parentSessionId":"local_p","sessionId":"remote_9"}')).toBe('')
  expect(parseSessionId('{"parentSessionId":"local_p","session_id":null}')).toBe('local_p')
  for (const other of [undefined, '', 'Started.', 'session remote_9']) expect(parseSessionId(other)).toBe('')
})

test('reads each session row with its worktree, the worktree path before the cwd', () => {
  expect(parseSessionRows(SESSIONS)).toEqual([
    { sessionId: 'local_other', worktree: 'D:\\Repos\\Other' },
    { sessionId: SESSION, worktree: WORKTREE },
  ])
  const one = JSON.stringify({ sessionId: SESSION, cwd: 'D:/a', worktreePath: 'D:/b' })
  expect(parseSessionRows(one)).toEqual([{ sessionId: SESSION, worktree: 'D:/b' }])
  expect(parseSessionRows(`Linked sessions:\n${SESSIONS}`)).toHaveLength(2)
  expect(parseSessionRows(JSON.stringify({ sessions: [{ sessionId: SESSION }] }))).toEqual([{ sessionId: SESSION, worktree: '' }])
  for (const other of [undefined, '', 'none', '[]', '{}', '[1,"x"]']) expect(parseSessionRows(other)).toEqual([])
})

test('compares worktrees however the path is spelled', () => {
  expect(sameWorktree(WORKTREE, 'd:/repos/backlog/.claude/worktrees/entry-b2/')).toBe(true)
  expect(sameWorktree('D:/a/', 'D:\\a')).toBe(true)
  expect(sameWorktree('D:/a', 'D:/ab')).toBe(false)
  expect([sameWorktree('', ''), sameWorktree(undefined, ''), sameWorktree('', 'D:/a')]).toEqual([false, false, false])
})

test('reads the run in progress, else the newest, from list_runs and get_run', () => {
  expect(parseRuns(RUNS)).toEqual({
    id: 'run-1',
    flow: 'flow-code',
    status: 'in_progress',
    stages: ['Scope', 'Implement', 'Build & Test', 'Personal Validation'],
    stageStatus: ['done', 'in_progress', 'pending', 'pending'],
  })
  // The real list_runs answer, with doneCount beside each stage.
  const real = '{"worktree":"x","count":1,"runs":[{"id":"run-1","skillId":"flow-code","status":"in_progress","stages":[{"name":"Update Base","status":"done","doneCount":1},{"name":"Scope","status":"in_progress","doneCount":0}]}]}'
  expect(parseRuns(real)?.stageStatus).toEqual(['done', 'in_progress'])
  const run = (id: string, status: string) => ({ id, skillId: 'flow-code', status, stages: [{ name: 'Scope', status: 'done' }] })
  expect(parseRuns(JSON.stringify({ runs: [run('r3', 'done'), run('r2', 'in_progress')] }))?.id).toBe('r2')
  expect(parseRuns(JSON.stringify({ runs: [run('r3', 'done'), run('r2', 'failed')] }))?.id).toBe('r3')
  expect(parseRuns(JSON.stringify(run('r4', 'done')))?.id).toBe('r4')
  expect(parseRuns(JSON.stringify({ run: run('r5', 'in_progress') }))?.id).toBe('r5')
  for (const other of [undefined, '', 'No runs.', '{"runs":[]}', '{"id":"r"}']) expect(parseRuns(other)).toBeNull()
})

test('reads the return from the last assistant message with text', () => {
  expect(parseEventsReturn(EVENTS_AT_GATE)).toEqual({ state: 'gate', note: '' })
  const pr = `Session ${SESSION}\n[assistant] Opened it.\n\npr https://github.com/o/r/pull/9\n[user] thanks`
  expect(parseEventsReturn(pr)).toEqual({ state: 'pr', note: 'https://github.com/o/r/pull/9' })
  // A markdown checkbox inside a message is the message's text, not a new message.
  const box = '[assistant] blocked build-test: 2 failing\n[x] reproduced\n[assistant] (called Bash)'
  expect(parseEventsReturn(box)).toEqual({ state: 'blocked', note: 'build-test: 2 failing' })
  // A session still in its turn has not returned: its latest text is not read.
  const running = EVENTS_AT_GATE.replace('(idle)', '(running)')
  expect(parseEventsReturn(running)).toBeNull()
  expect(parseEventsReturn(`Session "x" (idle) — showing 1 event
[assistant] pr https://x/pull/1`)?.state).toBe('pr')
  for (const none of [undefined, '', 'Session x — 0 events', '[user] go', '[assistant] (called Bash)\n(called Read)'])
    expect(parseEventsReturn(none)).toBeNull()
})

test('tallies entries with workers overriding stored status', () => {
  const plan = parsePlan('+demo', PLAN)
  const w = { sessionId: SESSION, worktree: '', entryId: 'b2', n: 2, title: 'Second prompt', state: 'gate' as const, note: '', flow: '', runIds: [], stages: [], stageStatus: [], runStatus: '' }
  expect(kindOf(plan?.entries[2], undefined)).toBe('manual')
  const c = tally(plan, [w])
  expect([c.total, c.done, c.gate, c.manual]).toEqual([3, 1, 1, 1])
  // A plan read from a file never passed get_plan_items: the workers are the count.
  const fromFile = tally({ tag: '+demo', entries: [] }, [w, { ...w, entryId: 'x', state: 'stopped' }])
  expect([fromFile.total, fromFile.gate, fromFile.blocked]).toEqual([2, 1, 1])
})

test('the pane follows an entry session through its flow stages', async ($, on) => {
  on('tool.call', ($, e) => host(e as never))
  // What the plugins beneath the band draw: nothing here.
  on('ui.render', () => ({ type: 'Box' }) as never)

  await $.tool.call({ tool: 'mcp__backlog__get_plan_items', planId: '+demo' } as never)
  await $.tool.call({ tool: 'mcp__ccd_session__start_session', title: '+demo:2 - Second prompt', prompt: BRIEF } as never)
  await $.tool.call({ tool: 'mcp__ccd_session__list_sessions', linked: true } as never)
  await $.tool.call({ tool: 'mcp__backlog__list_runs', worktree: 'd:/repos/backlog/.claude/worktrees/entry-b2/' } as never)

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
    expect(await band.find({ type: 'Text', text: /^Implement$/ })).toBeDefined()
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
  const w = { sessionId: SESSION, worktree: WORKTREE, entryId: 'b2', n: 2, title: 'Second prompt', state: 'gate' as const, note: '', flow: 'flow-code', runIds: ['r'], stages: ['Scope', 'Implement'], stageStatus: ['done', 'in_progress'], runStatus: '' }
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
  const base = { sessionId: SESSION, worktree: WORKTREE, entryId: 'b2', n: 2, title: 'Second prompt', note: '', flow: 'flow-code', runIds: ['r'], stages, runStatus: '' }
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
  let started = 0
  on('tool.call', ($, e) => (isStartSession(e.tool as string) ? answer(`Started local_s${++started}.`) : host(e as never)))

  await $.tool.call({ tool: 'mcp__backlog__get_plan_items', planId: '+demo' } as never)
  await $.tool.call({ tool: 'mcp__ccd_session__start_session', prompt: BRIEF } as never)
  await $.tool.call({ tool: 'mcp__ccd_session__start_session', prompt: BRIEF.replace('+demo', '+other').replace('(id `b2`)', '(id `z9`)') } as never)

  const ui = await $.ui.mount({ plugin: 'backlog-tools', surface: 'terminal', component: 'Pane', requestId: 'plan-progress', props: {} } as never)
  expect(await ui.find({ type: 'Text', text: /\+other/ })).toBeDefined()
  expect(await ui.find({ type: 'Text', text: /0\/1 done/ })).toBeDefined()
  await ui.unmount()
})

// The workers are drawn only in part: a plugin beside this one reads the state they declare.
const PEEK = {
  name: 'peek',
  register: (on: On) => {
    on('command.run', { command: 'peek-workers' }, async $ => {
      const { value } = await $.state.get({ plugin: 'backlog-tools', key: 'workers' })
      return { text: JSON.stringify(value ?? []) }
    })
  },
}
const workersOf = async ($: { command: { run: (e: never) => Promise<{ text?: string }> } }) =>
  JSON.parse((await $.command.run({ command: 'peek-workers', args: '' } as never)).text ?? '[]') as Worker[]
const workerOf = async ($: Parameters<typeof workersOf>[0]) => (await workersOf($))[0]

test('start_session with a brief adds a worker keyed by the answered session id', { plugins: [PEEK] }, async ($, on) => {
  on('tool.call', ($, e) => host(e as never))

  await $.tool.call({ tool: 'mcp__ccd_session__start_session', prompt: BRIEF } as never)

  const w = await workerOf($)
  expect([w?.sessionId, w?.worktree, w?.entryId, w?.n, w?.state]).toEqual([SESSION, '', 'b2', 2, 'running'])
})

test('a start_session whose prompt is no brief, or that answered no session, adds nothing', { plugins: [PEEK] }, async ($, on) => {
  let rejected = false
  on('tool.call', ($, e) => (rejected ? { isError: true, result: null, text: `Failed for ${SESSION}.` } : host(e as never)) as never)

  await $.tool.call({ tool: 'mcp__ccd_session__start_session', prompt: 'Look into the flaky test.' } as never)
  rejected = true
  await $.tool.call({ tool: 'mcp__ccd_session__start_session', prompt: BRIEF } as never)

  expect(await workersOf($)).toEqual([])
})

test('the session rows set the worktree, and only that worktree’s runs fill the stages', { plugins: [PEEK] }, async ($, on) => {
  on('tool.call', ($, e) => host(e as never))

  await $.tool.call({ tool: 'mcp__ccd_session__start_session', prompt: BRIEF } as never)
  await $.tool.call({ tool: 'mcp__ccd_session__get_session', session_id: SESSION } as never)
  expect((await workerOf($))?.worktree).toBe(WORKTREE)

  // Another worktree's runs are another session's.
  await $.tool.call({ tool: 'mcp__backlog__list_runs', worktree: 'D:/Repos/Backlog/.claude/worktrees/entry-z9' } as never)
  await $.tool.call({ tool: 'mcp__backlog__list_runs' } as never)
  expect((await workerOf($))?.stages).toEqual([])

  await $.tool.call({ tool: 'mcp__plugin_delivery-surface-dashboard_delivery-surface-dashboard__get_run', worktree: WORKTREE } as never)
  const w = await workerOf($)
  expect([w?.flow, w?.runIds, w?.stages, w?.stageStatus, w?.runStatus]).toEqual([
    'flow-code',
    ['run-1'],
    ['Scope', 'Implement', 'Build & Test', 'Personal Validation'],
    ['done', 'in_progress', 'pending', 'pending'],
    'in_progress',
  ])
})

test('the session’s events set its return, and a message to it sets it running again', { plugins: [PEEK] }, async ($, on) => {
  let denied = false
  let running = false
  on('tool.call', ($, e) =>
    (denied
      ? { deny: 'refused by the host' }
      : running && isEvents(e.tool as string)
        ? answer(EVENTS_AT_GATE.replace('(idle)', '(running)'))
        : host(e as never)) as never,
  )
  on('ui.render', () => ({ type: 'Box' }) as never)

  await $.tool.call({ tool: 'mcp__ccd_session__start_session', prompt: BRIEF } as never)
  // Another session's events leave this one alone.
  await $.tool.call({ tool: 'mcp__ccd_session__list_events', session_id: 'local_other' } as never)
  expect((await workerOf($))?.state).toBe('running')

  await $.tool.call({ tool: 'mcp__ccd_session__list_events', session_id: SESSION } as never)
  expect((await workerOf($))?.state).toBe('gate')

  const band = await $.ui.mount({ plugin: 'backlog-tools', surface: 'terminal', component: 'AbovePrompt', props: { hasSurvey: false, isWorking: true, maxRows: 10, bodyColumns: 100 } } as never)
  expect(await band.find({ type: 'Text', text: /gate/ })).toBeDefined()
  await band.unmount()

  // A message the host refused never reached the session.
  denied = true
  await $.tool.call({ tool: 'SendMessage', to: SESSION, message: 'Approved — go on.' } as never)
  expect((await workerOf($))?.state).toBe('gate')

  denied = false
  await $.tool.call({ tool: 'SendMessage', to: 'local_other', message: 'Approved — go on.' } as never)
  expect((await workerOf($))?.state).toBe('gate')
  await $.tool.call({ tool: 'SendMessage', to: SESSION, message: 'Approved — go on.' } as never)
  const w = await workerOf($)
  expect([w?.state, w?.note]).toEqual(['running', ''])

  // While its turn runs, the events say nothing about a return.
  running = true
  await $.tool.call({ tool: 'mcp__ccd_session__list_events', session_id: SESSION } as never)
  expect((await workerOf($))?.state).toBe('running')
})

test('a send_message to a stopped session sets it running again', { plugins: [PEEK] }, async ($, on) => {
  on('tool.call', ($, e) => host(e as never))

  await $.tool.call({ tool: 'mcp__ccd_session__start_session', prompt: BRIEF } as never)
  await $.tool.call({ tool: 'mcp__ccd_session__list_events', session_id: SESSION } as never)
  expect((await workerOf($))?.state).toBe('gate')

  await $.tool.call({ tool: 'mcp__ccd_session__send_message', session_id: 'local_other', message: 'Approved.' } as never)
  expect((await workerOf($))?.state).toBe('gate')
  // Subscribing to the session sends it nothing.
  await $.tool.call({ tool: 'mcp__ccd_session__send_message', session_id: SESSION, notify_when_idle: true } as never)
  await $.tool.call({ tool: 'SendMessage', to: SESSION, message: '  ', notify_when_idle: true } as never)
  expect((await workerOf($))?.state).toBe('gate')
  await $.tool.call({ tool: 'mcp__ccd_session__send_message', session_id: SESSION, message: 'Approved.' } as never)
  expect((await workerOf($))?.state).toBe('running')
})

// The session's state outlives a reload of the plugin's code: after /reload-plugins the module
// meets the workers an older release stored, keyed on `agentId`, with no session or worktree.
// This plugin turns the first worker written into one of those.
const LEGACY = {
  name: 'legacy',
  register: (on: On) => {
    let seeded = false
    on('state.set', { plugin: 'backlog-tools', key: 'workers' }, ($, e, next) => {
      if (seeded) return next(e)
      seeded = true
      const value = e.value.map(({ sessionId: _, worktree: __, ...w }) => ({ ...w, agentId: 'agent-1', state: 'gate' }))
      return next({ ...e, value } as never)
    })
  },
}

test('a worker an older release stored matches no session and breaks nothing', { plugins: [PEEK, LEGACY] }, async ($, on) => {
  on('tool.call', ($, e) =>
    isStartSession(e.tool as string) && String((e as { prompt?: string }).prompt).includes('`c3`') ? answer('Started local_c3.') : host(e as never),
  )

  await $.tool.call({ tool: 'mcp__ccd_session__start_session', prompt: BRIEF } as never)
  await $.tool.call({ tool: 'mcp__ccd_session__start_session', prompt: BRIEF.replace('`2 - Second prompt` (id `b2`)', '`3 - Third prompt` (id `c3`)') } as never)
  for (const call of [
    { tool: 'mcp__ccd_session__list_sessions', linked: true },
    { tool: 'mcp__backlog__list_runs', worktree: WORKTREE },
    { tool: 'mcp__ccd_session__list_events', session_id: '' },
    { tool: 'SendMessage', to: '' },
    { tool: 'SendMessage', to: 'local_c3' },
  ])
    await $.tool.call(call as never)

  const [legacy, fresh] = await workersOf($)
  expect([legacy?.entryId, legacy?.state, legacy?.stages]).toEqual(['b2', 'gate', []])
  expect([fresh?.sessionId, fresh?.entryId, fresh?.state]).toEqual(['local_c3', 'c3', 'running'])

  const pane = await $.ui.mount({ plugin: 'backlog-tools', surface: 'terminal', component: 'Pane', requestId: 'plan-progress', props: { bodyColumns: 60 } } as never)
  expect(await pane.find({ type: 'Text', text: /3 Third prompt/ })).toBeDefined()
  await pane.unmount()
})

test('the scripted run ends with a merge, a gate, a block and a manual step', () => {
  let tick = 0
  while (!demoAt(tick).isOver) tick += 1
  const end = demoAt(tick)
  const c = tally(end.plan, end.workers)
  expect([c.total, c.done, c.gate, c.blocked, c.manual, c.running]).toEqual([5, 1, 1, 1, 1, 1])
})
