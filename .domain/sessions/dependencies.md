# Dependencies: Sessions

```meta
status: active
related: [.domain/context-map.md, .domain/dev-pc-management/domain.md#aggregate-machine-registry]
```

> Dependencies this bounded context has on other bounded contexts or modules, and
> known dependents. Use explicit DDD relationship semantics, integration mechanism
> details, and contract references.

## Outbound dependencies

| Depends on (context/module) | DDD pattern | Integration mechanism | Contract | Why |
|---|---|---|---|---|
| Collections MCP | ACL (Sessions = customer) | Optional append/read of sanitized session-activity documents keyed by `Session Identity` | `.domain/sessions/domain.md#session-activity-stream`, `.domain/sessions/domain.md#domain-service-session-activity-publishing` | To layer externally reported milestone activity over the locally read session record without making the collection authoritative for session existence. Sessions owns the meaning of the stream and translates to and from the MCP's stored shape. **Planned.** Missing configuration is ordinary; configured-but-unreachable reporting reads as degraded rather than failed. |
| Dev PC Management | Customer/Supplier (Sessions = customer) | Machine-name lookup against the machine registry | `.domain/dev-pc-management/domain.md#aggregate-machine-registry` | To say which registered machine an `Environment` corresponds to, when the two name the same box. **Not built.** A locally read session is stamped with the name of the environment that read it, so nothing is asked of the registry today; the dependency becomes real the first time sessions arrive from an environment other than the one reading them. |
| Repository Management | Conformist | None — the `owner/name` string an agent wrote is held verbatim | `.domain/repository-management/domain.md#aggregate-repository-registry` | A session's `Working Location` names a repository in that context's form. Conformist and deliberately inert: the string is displayed, never resolved, so a repository this product does not know about still reads correctly. |

## Inbound dependents (known)

| Consumer (context/module) | DDD pattern | Integration mechanism | Contract | What it relies on |
|---|---|---|---|---|
| Productivity | Customer/Supplier (Sessions = supplier) | Session facts per environment | `.domain/sessions/domain.md#aggregate-session-log` | AI-assisted work needs to know which agent worked where and for how long. **Not built.** Productivity's session signal is documented against Monitoring today; that row moves here as the two contexts are wired, and this context publishes no event until it does. |
| Monitoring & Dashboard | Customer/Supplier (Sessions = supplier) | Stalled-session observation | `.domain/sessions/domain.md#session-state` | Monitoring alerts on sessions that have gone quiet, which is exactly what `stalled` means here. **Not built.** Monitoring keeps its own `copilot_session` signal kind until it consumes this context instead. |

## Notes

- **This context still publishes no bounded-context domain event yet, and the table
  above says so rather than inventing one early.** The planned Collections MCP
  integration is a module-level reporting path, not a published language into another
  bounded context. When Productivity or Monitoring is wired up, the event belongs in
  `domain.md` as a first-class chapter and in the context map's published-language
  table at the same time.

- **`Environment` here is not the Environment context's Environment.** That context
  owns launchable destinations — a dashboard, a staging app, a resource group. This
  one means "where an agent ran", which today is a development PC. The words collide
  and the concepts do not, which is why each is defined in its own `naming.md` and why
  neither context references the other. See
  `.domain/sessions/naming.md#term-environment`.

- **The overlap with Dev PC Management is resolved, not tolerated.** That context
  modelled Copilot sessions on the Machine while Copilot was the only agent the
  machines ran; the subject moved here in full and Dev PC Management's session chapters
  were removed rather than left as a second model of the same thing. Its
  `dependencies.md` now records this context as the dependent that took them.

- **No dependency here crosses a boundary without a published language or an
  anti-corruption layer.** The planned Collections MCP path is explicitly an ACL so the
  storage module never becomes the authority for session meaning; the Repository
  Management row stays conformist because the string is held verbatim, and the Dev PC
  Management row remains a future lookup rather than a live call.
