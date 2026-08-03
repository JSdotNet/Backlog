# Dependencies: <Bounded Context Name>

> Dependencies this bounded context has on other bounded contexts or
> modules, and known dependents. Note the integration pattern for each
> relationship (synchronous call, domain/integration event, shared kernel,
> anti-corruption layer, etc.).

## Outbound dependencies

| Depends on (context/module) | Integration pattern | Why |
|---|---|---|
| <OtherContext> | <e.g. async event, REST call, shared kernel> | <reason this context needs it> |

## Inbound dependents (known)

| Consumer (context/module) | Integration pattern | What it relies on |
|---|---|---|
| <OtherContext> | <e.g. subscribes to event X> | <what would break if changed> |

## Notes

- Flag any dependency that crosses a bounded-context boundary without an
  anti-corruption layer or published language, so it can be revisited.
- Link to the relevant `domain-interaction-diagram` / `context-mapping`
  artifact if one exists for this relationship, instead of duplicating it.
