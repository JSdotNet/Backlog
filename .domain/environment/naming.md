# Naming: Environment

```meta
status: draft
```

> Canonical ubiquitous-language terms for this bounded context and their aliases.
> Each term links to where it is modeled (`related`); the surface names it is also
> known by are recorded in the `aliases` metadata field so a synonym can always be
> resolved back to one canonical concept.

## Term: Environment Catalog

```meta
status: draft
aliases: [EnvironmentCatalog]
related: [.domain/environment/domain.md#aggregate-environment-catalog]
```

The personal catalog of named, launchable environments and their shortcuts.

## Term: Environment

```meta
status: draft
aliases: [Environment, environment_id]
related: [.domain/environment/domain.md#environment]
```

A named destination the person wants quick access to.

## Term: Environment Shortcut

```meta
status: draft
aliases: [EnvironmentShortcut, shortcut_id]
related: [.domain/environment/domain.md#environment-shortcut]
```

A pinned, grouped, or ordered quick-access entry for an Environment.

## Term: Launch Target

```meta
status: draft
aliases: [LaunchTarget, target_ref]
related: [.domain/environment/domain.md#launch-target]
```

The non-secret value used to open an Environment, such as a URL, command,
workspace path, dashboard id, or cloud resource id.

## Term: Access Hint

```meta
status: draft
aliases: [AccessHint]
related: [.domain/environment/domain.md#access-hint]
```

A non-secret reminder about how to access an Environment.