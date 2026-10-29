import { expect, test } from 'claude-code/testing'

// @ts-ignore -- a plain ES module asset of the skill, without declarations
import { decide, dispatcherName, endpointFrom, isNews, settingsPath } from '../skills/dispatch-sessions/assets/wait-logic.mjs'

// The decisions of the dispatch-sessions wait script. The script itself and the capacity probe
// spawn processes, which this sandbox cannot: tests/dispatch-sessions-assets.test.mjs runs them
// under `node --test`.

const OPTIONS = { retryMs: 30_000, retryAfterMs: undefined }
const request = (id: string) => JSON.stringify({ kind: 'request', id, requestKind: 'entry', repository: 'JSdotNet/Backlog', brief: '/backlog-tools:run-plan-item entry `x`:' })
const delivery = (id: string) => JSON.stringify({ kind: 'delivery', id, sessionId: 'cli-1', desktopSessionId: 'local_1', question: 'Which database?', answer: 'Postgres' })

test('prints a request once, and waits quietly while the route answers it again', () => {
  const printed = new Set<string>()
  expect(decide(200, request('r1'), printed, OPTIONS)).toEqual({ line: 'request r1 entry JSdotNet/Backlog', state: 'request', waitMs: 0 })
  expect(decide(200, request('r1'), printed, OPTIONS)).toEqual({ line: null, state: 'seen', waitMs: 30_000 })
  expect(decide(200, request('r2'), printed, OPTIONS).line).toBe('request r2 entry JSdotNet/Backlog')
  expect(decide(200, '{ half', printed, OPTIONS)).toEqual({ line: 'unavailable status=200', state: 'unavailable', waitMs: 30_000 })
})

test('prints a delivery before the request behind it, and the request kind the route names', () => {
  const printed = new Set<string>()
  expect(decide(200, delivery('d1'), printed, OPTIONS)).toEqual({ line: 'delivery d1 local_1', state: 'delivery', waitMs: 0 })
  expect(decide(200, delivery('d1'), printed, OPTIONS)).toEqual({ line: null, state: 'seen', waitMs: 30_000 })
  expect(decide(200, request('r1'), printed, OPTIONS).line).toBe('request r1 entry JSdotNet/Backlog')
  const plan = JSON.stringify({ kind: 'request', id: 'r2', requestKind: 'plan', repository: 'o/r' })
  expect(decide(200, plan, printed, OPTIONS).line).toBe('request r2 plan o/r')
})

test('prints a released delivery again once the route offers it again', () => {
  const printed = new Set<string>()
  expect(decide(200, delivery('d1'), printed, OPTIONS).line).toBe('delivery d1 local_1')
  expect(decide(204, '', printed, OPTIONS).line).toBeNull()
  expect(decide(200, delivery('d1'), printed, OPTIONS).line).toBe('delivery d1 local_1')
  expect(decide(200, request('r1'), printed, OPTIONS).line).toBe('request r1 entry JSdotNet/Backlog')
  expect(decide(200, request('r1'), printed, OPTIONS).line).toBeNull()
  expect(decide(200, delivery('d1'), printed, OPTIONS).line).toBe('delivery d1 local_1')
})

test('names every other answer the route gives, and backs off on each', () => {
  const printed = new Set<string>()
  expect(decide(204, '', printed, OPTIONS)).toEqual({ line: null, state: 'idle', waitMs: 0 })
  expect(decide(404, '', printed, OPTIONS)).toEqual({ line: 'flag-off', state: 'flag-off', waitMs: 30_000 })
  expect(decide(401, '', printed, OPTIONS).line).toBe('unauthorized')
  expect(decide(403, '', printed, OPTIONS).line).toBe('unauthorized')
  expect(decide(503, '', printed, { ...OPTIONS, retryAfterMs: 5_000 })).toEqual({ line: 'unavailable', state: 'unavailable', waitMs: 5_000 })
  expect(decide(500, '', printed, OPTIONS).line).toBe('unavailable status=500')
})

test('says a request every time, and a failure only when the state changes', () => {
  const request1 = { line: 'request r1 entry o/r', state: 'request', waitMs: 0 }
  const off = { line: 'flag-off', state: 'flag-off', waitMs: 1 }
  expect(isNews(request1, 'request')).toBe(true)
  expect(isNews({ line: 'delivery d1 local_1', state: 'delivery', waitMs: 0 }, 'delivery')).toBe(true)
  expect(isNews(off, 'idle')).toBe(true)
  expect(isNews(off, 'flag-off')).toBe(false)
  expect(isNews({ line: null, state: 'idle', waitMs: 0 }, 'flag-off')).toBe(false)
})

test('finds the port and token the way the telemetry forwarder does', () => {
  expect(settingsPath({ LOCALAPPDATA: 'C:\\Users\\a\\AppData\\Local' }, 'win32', 'C:\\Users\\a'))
    .toBe('C:\\Users\\a\\AppData\\Local\\Backlog\\settings.json')
  expect(settingsPath({}, 'win32', 'C:\\Users\\a')).toBe('C:\\Users\\a\\AppData\\Local\\Backlog\\settings.json')
  expect(settingsPath({}, 'darwin', '/Users/a')).toBe('/Users/a/Library/Application Support/Backlog/settings.json')
  expect(settingsPath({}, 'linux', '/home/a')).toBe('/home/a/.local/share/Backlog/settings.json')

  const settings = JSON.stringify({ mcpServer: { port: 6123, token: 'from-settings' } })
  expect(endpointFrom({}, settings)).toEqual({ url: 'http://127.0.0.1:6123/session-requests/wait', token: 'from-settings' })
  expect(endpointFrom({ BACKLOG_MCP_PORT: '7001', BACKLOG_MCP_TOKEN: 'from-env' }, settings))
    .toEqual({ url: 'http://127.0.0.1:7001/session-requests/wait', token: 'from-env' })
  expect(endpointFrom({ BACKLOG_MCP_PORT: '7001' }, settings)).toEqual({ url: 'http://127.0.0.1:7001/session-requests/wait', token: 'from-settings' })
  expect(endpointFrom({}, null)).toEqual({ url: 'http://127.0.0.1:5757/session-requests/wait', token: null })
  expect(endpointFrom({}, '{ half')).toEqual({ url: 'http://127.0.0.1:5757/session-requests/wait', token: null })
})

test('names the dispatcher after the machine, so a restarted Manager knows its claims', () => {
  expect(dispatcherName('JOB-PC.local')).toBe('dispatch-sessions-job-pc-local')
})
