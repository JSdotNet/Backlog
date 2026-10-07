import { atom, read, update } from 'claude-code'
import type { EngineInterface, Register } from 'claude-code'

import type { Worker } from '../types'
import {
  isPlanItems,
  isTransition,
  newWorker,
  normStatus,
  parseBrief,
  parsePlan,
  parseReturn,
  parseRunId,
  surfaceOp,
  tally,
} from './parse'
import { GLYPH, KIND, abbrev, alsoLine, bandRows, cellsOf, columnsOf, currentStage, matrixLayout, matrixSvg, rowsOf } from './view'

// The execute-plan view: a band above the prompt with the entries still in flow, and a pane —
// the band's details — with the whole plan as a matrix of entries by delivery-flow stage. Both
// are read from the calls execute-plan and its sub-agents already make; nothing here writes.
const PANE = 'plan-progress'
const plan = atom({ plugin: 'backlog-tools', key: 'plan' } as const, null)
const workers = atom({ plugin: 'backlog-tools', key: 'workers' } as const, [])
const parents = atom({ plugin: 'backlog-tools', key: 'parents' } as const, {})

const patchWorker = (list: Worker[], agentId: string, fn: (w: Worker) => Worker) =>
  list.map(w => (w.agentId === agentId ? fn(w) : w))

/** The execute-plan worker a loop belongs to, walking nested spawns up. */
async function rootOf($: EngineInterface, agentId: string | undefined) {
  if (!agentId) return undefined
  const list = await read($, workers)
  const map = await read($, parents)
  let id: string | undefined = agentId
  for (let hop = 0; id && hop < 8; hop++) {
    if (list.some(w => w.agentId === id)) return id
    id = map[id]
  }
  return undefined
}

async function refreshStatus($: EngineInterface) {
  const c = tally(await read($, plan), await read($, workers))
  if (c.total === 0) return
  const parts = [`plan ${c.done}/${c.total}`]
  if (c.running) parts.push(`${c.running} running`)
  if (c.gate) parts.push(`${c.gate} at gate`)
  if (c.blocked) parts.push(`${c.blocked} blocked`)
  $.ui.status(parts.join(' · '))
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

    const op = surfaceOp(tool)
    const owner = op ? await rootOf($, e.agentId) : undefined
    if (!op || !owner) return next(e)

    const ran = await next(e)
    if (ran.deny !== undefined || ran.isError) return ran
    // Any report from the worker's loop means it is working again, after a gate answered too.
    const resume = (w: Worker): Worker => (w.state === 'gate' ? { ...w, state: 'running', note: '' } : w)
    if (op === 'start_run') {
      const runId = parseRunId(ran.text)
      const stages = Array.isArray(args.stages) ? args.stages.map(String) : []
      await update($, workers, list =>
        patchWorker(list, owner, w => {
          // A reattach (resumed:true) keeps the statuses already seen.
          const same = w.runId === runId && w.stages.length === stages.length
          return {
            ...resume(w),
            flow: String(args.skillId ?? ''),
            runId,
            stages,
            stageStatus: same ? w.stageStatus : stages.map(() => 'pending'),
            runStatus: 'in_progress',
          }
        }),
      )
    } else if (op === 'update_stage') {
      const i = Number(args.stageIndex)
      const status = String(args.status ?? '')
      await update($, workers, list =>
        patchWorker(list, owner, w => {
          // Another run of the same worker (a second flow) is not this one's stages.
          if (args.runId !== w.runId || !(i >= 0 && i < w.stages.length)) return w
          const stageStatus = [...w.stageStatus]
          stageStatus[i] = status
          return { ...resume(w), stageStatus }
        }),
      )
    } else {
      await update($, workers, list =>
        patchWorker(list, owner, w =>
          args.runId === w.runId ? { ...w, runStatus: String(args.status ?? '') } : w,
        ),
      )
    }
    return ran
  }).catch(($, e, next) => next(e))

  on('agent.spawn', async ($, e, next) => {
    const ran = await next(e)
    const agentId = ran.agentId
    if (!agentId) return ran

    const brief = parseBrief(e.prompt)
    if (brief) {
      // A brief for another plan starts the pane over: the previous run's plan and workers go.
      const isNewPlan = (await read($, plan))?.tag !== brief.tag
      await update($, workers, list => [
        ...(isNewPlan ? [] : list.filter(w => w.entryId !== brief.entryId)),
        newWorker(agentId, brief),
      ])
      if (isNewPlan) await update($, parents, () => ({}))
      await update($, plan, p => (p && p.tag === brief.tag ? p : { tag: brief.tag, entries: [] }))
      await refreshStatus($)
    } else if (e.parentAgentId && (await rootOf($, e.parentAgentId))) {
      const parent = e.parentAgentId
      await update($, parents, map => ({ ...map, [agentId]: parent }))
    }
    return ran
  }).catch(($, e, next) => next(e))

  on('turn.complete', async ($, e, next) => {
    const agentId = e.agentId
    if (agentId && (await read($, workers)).some(w => w.agentId === agentId)) {
      const back = e.isAborted ? { state: 'stopped' as const, note: 'aborted' } : parseReturn(e.answer)
      await update($, workers, list => patchWorker(list, agentId, w => ({ ...w, ...back })))
      await refreshStatus($)
      const w = (await read($, workers)).find(x => x.agentId === agentId)
      if (w && back.state === 'gate') $.ui.toast(`${w.n} - ${w.title}: waiting at the gate`)
      if (w && back.state === 'blocked') $.ui.toast(`${w.n} - ${w.title}: blocked ${back.note}`)
    }
    return next(e)
  })

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
            {(r.w?.stages ?? []).map((_, i) => {
              const cell = GLYPH[r.w?.stageStatus[i] ?? 'pending'] ?? GLYPH.pending!
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
