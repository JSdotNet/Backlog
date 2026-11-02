# Introduction

```meta
status: draft
```

An ordinary chapter: its status is the branch it is on, so it imports no state.

## Goals

```meta
```

The shop is local first.

```annotation
author: jobsc
date: 2026-09-02
body: Local first for the cart too?
```

Orders reach the warehouse within an hour.

```annotation
kind: question
author: jobsc
date: 2026-09-03
quote: within an hour
body: |
  Is the hour measured from payment
  or from placing the order?
replies:
  - author: claude/flow-spec
    date: 2026-09-04
    body: From payment.
```

```annotation
kind: comment
status: resolved
author: jobsc
date: 2026-09-01
body: Answered and swept on the next branch.
```

Refunds are manual.

```annotation
kind: nitpick
author: reviewer
date: 2026-09-05
body: An unknown kind reads as a comment.
```
