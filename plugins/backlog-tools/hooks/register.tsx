import { atom, read, update } from 'claude-code'
import type { EngineInterface, Register } from 'claude-code'

import type { Worker, WorkerState } from '../types'
import {
  isEvents,
  isPlanItems,
  isRuns,
  isSessionRows,
  isStartSession,
  isTransition,
  messageTarget,
  newWorker,
  normStatus,
  parseBrief,
  parseEventsReturn,
  parsePlan,
  parseRuns,
  parseSessionId,
  parseSessionRows,
  sameWorktree,
  tally,
} from './parse'
import { GLYPH, KIND, abbrev, alsoLine, bandRows, cellsOf, columnsOf, currentStage, matrixLayout, matrixSvg, rowsOf, stageStatesOf } from './view'

// The execute-plan view: a band above the prompt with the entries still in flow, and a pane —
// the band's details — with the whole plan as a matrix of entries by delivery-flow stage. Each
// entry runs in a session of its own, whose calls never pass this session's hooks: both are
// read from the calls execute-plan makes about those sessions. Nothing here writes.
const PANE = 'plan-progress'
const plan = atom({ plugin: 'backlog-tools', key: 'plan' } as const, null)
const workers = atom({ plugin: 'backlog-tools', key: 'workers' } as const, [])

/**
 * A worker's session id and worktree. The session's state outlives a reload of this code, so a
 * worker an older release stored keys on `agentId` and has neither: it matches no session.
 */
const sessionOf = (w: Worker) => (w as Partial<Worker>).sessionId ?? ''
const worktreeOf = (w: Worker) => (w as Partial<Worker>).worktree ?? ''

const patchWorker = (list: Worker[], sessionId: string, fn: (w: Worker) => Worker) =>
  list.map(w => (sessionId !== '' && sessionOf(w) === sessionId ? fn(w) : w))

const textOf = (ran: object): string | undefined => ('text' in ran && typeof ran.text === 'string' ? ran.text : undefined)

async function refreshStatus($: EngineInterface) {
  const c = tally(await read($, plan), await read($, workers))
  if (c.total === 0) return
  const parts = [`plan ${c.done}/${c.total}`]
  if (c.running) parts.push(`${c.running} running`)
  if (c.gate) parts.push(`${c.gate} at gate`)
  if (c.blocked) parts.push(`${c.blocked} blocked`)
  $.ui.status(parts.join(' · '))
}

/** A worker that newly reached a state needing the person says so once. */
function toastOn($: EngineInterface, w: Worker | undefined, was: WorkerState | undefined) {
  if (!w || w.state === was) return
  if (w.state === 'gate') $.ui.toast(`${w.n} - ${w.title}: waiting at the gate`)
  if (w.state === 'blocked') $.ui.toast(`${w.n} - ${w.title}: blocked ${w.note}`)
}

export const register: Register = on => {
  on('session.start', async ($, e, next) => {
    await $.command.register({
      name: 'plan-progress',
      description: 'Show execute-plan progress and each entry’s delivery flow in a pane',
    })
    return next(e)
  })

  on('command.run', { command: 'plan-progress' }, async $ => {
    await $.ui.open({ id: PANE, title: 'Plan progress' })
    return { text: 'Plan progress pane opened.' }
  })

  // A pane never fails the call it watches: a hook that throws hands the call on untouched.
  on('tool.call', async ($, e, next) => {
    const tool = e.tool as string
    const args = e as unknown as Record<string, unknown>

    if (isPlanItems(tool)) {
      const ran = await next(e)
      const parsed = 'text' in ran ? parsePlan(String(args.planId ?? ''), ran.text) : null
      if (parsed && parsed.entries.length > 0 && !ran.isError) {
        // Another plan's items: the workers of the previous one are not this plan's.
        if ((await read($, plan))?.tag !== parsed.tag) await update($, workers, () => [])
        await update($, plan, () => parsed)
        await refreshStatus($)
      }
      return ran
    }

    if (isTransition(tool) && args.id) {
      const ran = await next(e)
      if (ran.deny !== undefined || ran.isError) return ran
      const status = normStatus(args.status)
      await update($, plan, p =>
        p ? { ...p, entries: p.entries.map(x => (x.id === args.id ? { ...x, status } : x)) } : p,
      )
      await refreshStatus($)
      return ran
    }

    // An entry session starts: the brief names the entry, the answer the session's id.
    const brief = isStartSession(tool) ? parseBrief(String(args.prompt ?? '')) : null
    if (brief) {
      const ran = await next(e)
      if (ran.deny !== undefined || ran.isError) return ran
      const sessionId = parseSessionId(textOf(ran))
      if (sessionId === '') return ran
      // A brief for another plan starts the pane over: the previous run's plan and workers go.
      const isNewPlan = (await read($, plan))?.tag !== brief.tag
      await update($, workers, list => [
        ...(isNewPlan ? [] : list.filter(w => w.entryId !== brief.entryId)),
        newWorker(sessionId, brief),
      ])
      await update($, plan, p => (p && p.tag === brief.tag ? p : { tag: brief.tag, entries: [] }))
      await refreshStatus($)
      return ran
    }

    // The session rows say where each entry session works: its worktree.
    if (isSessionRows(tool)) {
      const ran = await next(e)
      if (ran.deny !== undefined || ran.isError) return ran
      const rows = parseSessionRows(textOf(ran)).filter(r => r.worktree !== '')
      if (rows.length > 0 && (await read($, workers)).some(w => rows.some(r => r.sessionId === sessionOf(w)))) {
        await update($, workers, list =>
          list.map(w => {
            const row = rows.find(r => r.sessionId === sessionOf(w))
            return row ? { ...w, worktree: row.worktree } : w
          }),
        )
      }
      return ran
    }

    // A surface's runs for an entry session's worktree are that entry's flow, as it stands.
    const worktree = isRuns(tool) ? String(args.worktree ?? '') : ''
    if (worktree !== '' && (await read($, workers)).some(w => sameWorktree(worktreeOf(w), worktree))) {
      const ran = await next(e)
      if (ran.deny !== undefined || ran.isError) return ran
      const run = parseRuns(textOf(ran))
      if (run && run.stages.length > 0) {
        await update($, workers, list =>
          list.map(w =>
            sameWorktree(worktreeOf(w), worktree)
              ? {
                  ...w,
                  flow: run.flow,
                  runIds: run.id ? [run.id] : [],
                  stages: run.stages,
                  stageStatus: run.stageStatus,
                  runStatus: run.status,
                }
              : w,
          ),
        )
        await refreshStatus($)
      }
      return ran
    }

    // A session's events end with its return line once its turn is over.
    const eventsOf = isEvents(tool) ? String(args.session_id ?? '') : ''
    if (eventsOf !== '' && (await read($, workers)).some(w => sessionOf(w) === eventsOf)) {
      const ran = await next(e)
      if (ran.deny !== undefined || ran.isError) return ran
      const back = parseEventsReturn(textOf(ran))
      if (back) {
        const was = (await read($, workers)).find(w => sessionOf(w) === eventsOf)?.state
        await update($, workers, list => patchWorker(list, eventsOf, w => ({ ...w, ...back })))
        await refreshStatus($)
        toastOn($, (await read($, workers)).find(w => sessionOf(w) === eventsOf), was)
      }
      return ran
    }

    // A message to an entry session that stopped — the person's answer at its gate, a fix
    // pass — sets it working again. A call with no message only subscribes to the session.
    const said = typeof args.message === 'string' && args.message.trim() !== ''
    const to = said ? messageTarget(tool, args) : ''
    if (to !== '' && (await read($, workers)).some(w => sessionOf(w) === to)) {
      const ran = await next(e)
      if (ran.deny !== undefined || ran.isError) return ran
      const waiting = (w: Worker) => w.state === 'gate' || w.state === 'blocked' || w.state === 'stopped'
      await update($, workers, list => patchWorker(list, to, w => (waiting(w) ? { ...w, state: 'running', note: '' } : w)))
      await refreshStatus($)
      return ran
    }

    return next(e)
  }).catch(($, e, next) => next(e))

  // The details: every entry of the plan, one row each, one block per stage of its flow. Every
  // column is a Box of a set width, so the matrix holds on the desktop's proportional text too.
  on('ui.render', { component: 'Pane', requestId: PANE }, async ($, e) => {
    const { Box, Text } = $.ui.resolve(e)
    const p = await read($, plan)
    const list = await read($, workers)
    const cols = Math.max(30, e.props.bodyColumns ?? e.viewport?.columns ?? 60)

    if (!p && list.length === 0) {
      return (
        <Box flexDirection="column">
          <Text dimColor>No plan running yet.</Text>
          <Text dimColor>Run /backlog-tools:execute-plan +tag to start one.</Text>
        </Box>
      )
    }

    const c = tally(p, list)
    const rows = rowsOf(p, list)
    const stages = columnsOf(rows)
    const attention = (['gate', 'blocked'] as const).filter(k => c[k] > 0).map(k => `${c[k]} ${KIND[k].label}`)
    const summary = [`${c.done}/${c.total} done`, ...attention].join(' · ')

    // A surface that draws SVG gets design C as drawn: rounded blocks with gaps between them.
    if (e.surface !== 'terminal') {
      const { Svg } = $.ui.resolve(e)
      return (
        <Svg
          source={matrixSvg(p?.tag ?? 'Plan', summary, rows, stages)}
          alt={`${p?.tag ?? 'Plan'}: ${summary}; ${rows.length} entries by delivery stage`}
        />
      )
    }

    const layout = matrixLayout(cols, stages.length)
    const notes = rows.filter(r => r.w && r.w.note !== '' && r.w.state !== 'running')

    return (
      <Box flexDirection="column">
        <Box flexDirection="row" justifyContent="space-between">
          <Text bold wrap="truncate-end">{p?.tag ?? 'Plan'}</Text>
          <Text>{summary}</Text>
        </Box>
        <Box height={1} />
        {layout.hasHeadings && stages.length > 0 && (
          <Box flexDirection="row">
            <Box width={2 + layout.titleWidth + 1} />
            {stages.map((s, i) => (
              <Box key={`h${i}`} width={layout.cell} marginRight={layout.gap} justifyContent="center">
                <Text dimColor wrap="truncate-end">{abbrev(s)}</Text>
              </Box>
            ))}
          </Box>
        )}
        {rows.map(r => (
          <Box key={r.id} flexDirection="row">
            <Box width={2}>
              <Text color={KIND[r.kind].color}>{KIND[r.kind].mark}</Text>
            </Box>
            <Box width={layout.titleWidth} marginRight={1}>
              <Text wrap="truncate-end">{`${r.n} ${r.title}`}</Text>
            </Box>
            {(r.w ? stageStatesOf(r.w) : []).map((s, i) => {
              const cell = GLYPH[s] ?? GLYPH.pending!
              return (
                <Box key={`c${i}`} width={layout.cell} marginRight={layout.gap} justifyContent="center">
                  <Text color={cell.color} dimColor={cell.dim}>{cell.g}</Text>
                </Box>
              )
            })}
            {r.kind === 'manual' && <Text dimColor>{r.type}</Text>}
            {r.w && r.w.stages.length === 0 && r.w.state === 'running' && <Text dimColor>starting…</Text>}
          </Box>
        ))}
        {notes.length > 0 && <Box height={1} />}
        {notes.map(r => (
          <Text key={`n${r.id}`} dimColor wrap="truncate-end">
            <Text color={KIND[r.kind].color}>{KIND[r.kind].mark}</Text> {r.n} {r.w ? currentStage(r.w) || r.w.state : ''}: {r.w?.note ?? ''}
          </Text>
        ))}
      </Box>
    )
  })

  // The band above the prompt (design F): the entries still in flow, three columns — the entry,
  // its stage marks, where it stands — each a Box of a set width so the rows line up. Whatever
  // the plugins beneath draw there (a flow band of this session's own run) stays, under it.
  on('ui.render', { component: 'AbovePrompt' }, async ($, e, next) => {
    if (e.props.hasSurvey) return next(e)
    const p = await read($, plan)
    const list = await read($, workers)
    const rows = rowsOf(p, list)
    const { shown, more } = bandRows(rows, e.props.maxRows)
    if (shown.length === 0) return next(e)

    const { Box, Text, Button } = $.ui.resolve(e)
    const c = tally(p, list)
    const also = alsoLine(rows)
    const labelWidth = Math.min(32, Math.max(14, Math.floor(e.props.bodyColumns / 3)))
    const markWidth = Math.max(...shown.map(r => r.w?.stages.length ?? 0)) + 2
    const band = (
      <Box flexDirection="column">
        <Box flexDirection="row" justifyContent="space-between">
          <Text wrap="truncate-end">
            <Text bold>execute-plan</Text>  <Text dimColor>{p?.tag ?? ''}</Text>  {c.done}/{c.total} done
            {also !== '' && <Text dimColor>  also: {also}</Text>}
          </Text>
          <Button key="details" label="details" onPress={() => $.ui.open({ id: PANE, title: 'Plan progress' })} />
        </Box>
        {shown.map(r => {
          const stage = r.w ? currentStage(r.w) : ''
          return (
            <Box key={r.id} flexDirection="row">
              <Box width={labelWidth} marginRight={2}>
                <Text dimColor wrap="truncate-end">{`${r.n} ${r.title}`}</Text>
              </Box>
              <Box width={markWidth} flexDirection="row">
                {cellsOf(r.w).map((cell, i) => (
                  <Box key={`c${i}`} width={1}>
                    <Text color={cell.color} dimColor={cell.dim}>{cell.g}</Text>
                  </Box>
                ))}
              </Box>
              <Text wrap="truncate-end">
                <Text color={r.kind === 'blocked' ? 'red' : 'yellow'}>{stage || (r.w && r.w.stages.length === 0 ? 'starting…' : '')}</Text>
                {r.kind === 'gate' && <Text color="magenta"> gate</Text>}
                {r.kind === 'blocked' && <Text color="red"> blocked</Text>}
              </Text>
            </Box>
          )
        })}
        {more > 0 && <Text dimColor>+{more} more in flow — details</Text>}
      </Box>
    )
    const below = await next(e)
    return below ? (
      <Box flexDirection="column">
        {band}
        {below}
      </Box>
    ) : band
  })
}
