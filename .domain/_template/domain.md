# Domain: <Bounded Context Name>

> One chapter per Aggregate or Domain Service in this bounded context.
> Aggregate chapters include sub-chapters for their owned Entities, Value
> Objects, and Enums. Value Objects/Enums shared across multiple aggregates
> get their own chapter at the end instead of being duplicated.

## Aggregate: <AggregateName>

Responsibility, lifecycle, and invariants of the aggregate (what it
guarantees to be true at all times, and why it exists as a consistency
boundary).

### Entities

#### <EntityName>

Role within the aggregate, identity, and lifecycle notes.

### Value Objects

#### <ValueObjectName>

Meaning, equality semantics, and validation rules.

### Enums

#### <EnumName>

Values and what each one means in business terms.

## Aggregate: <NextAggregateName>

...

## Domain Service: <DomainServiceName>

Responsibility of the service, which aggregates/policies it coordinates, and
why the behavior does not belong on a single aggregate.

## Shared Value Objects

> Value Objects used by more than one aggregate in this bounded context.

### <SharedValueObjectName>

Meaning, equality semantics, validation rules, and which aggregates use it.

## Shared Enums

> Enums used by more than one aggregate in this bounded context.

### <SharedEnumName>

Values and what each one means in business terms, and which aggregates use it.
