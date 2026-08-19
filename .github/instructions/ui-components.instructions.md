---
applyTo: "src/App/**,src/Modules/**,src/Core/Backlog.UI.Components/**"
description: An application screen renders the shared component library's components rather than growing its own copies; how to make a component fit, and what to do when it cannot.
---

# UI components

`src/Core/Backlog.UI.Components` is the product's own component library. It is a
deliberate choice over a third-party suite
(`.design/component-libraries.md#materialization`), and the price of that choice
is that accessibility semantics, keyboard support, and focus handling are the
product's own work rather than something inherited. That price is paid once, in
the library. A hand-rolled copy in a screen does not pay it at all.

## The rule

**A screen under `src/App/**` or `src/Modules/**` renders the library's
component. It does not write its own version of one.**

This holds regardless of how small the copy is or how exactly it matches today.
Exactly is how a copy starts; matching is what it stops doing.

The rule applies to both shapes a copy takes:

- a **raw interactive element** where the library ships a component —
  `<button>`, `<input>`, `<select>`, `<textarea>`;
- a **plain element wearing a component's own class** — a `div`, `span`, or `p`
  carrying `save-indicator`, `badge`, `empty-state`, `card`, or any other class a
  library component draws.

The second is the easier one to write by accident, because nothing about a
`<span>` looks wrong.

## Making a component fit

The usual reason to reach for a copy is that the component does not wear the
screen's classes. It almost always can:

- **`BaseClass`** replaces the class every instance of that component carries —
  `<Alert BaseClass="tools-panel__message" />` emits `<p class="tools-panel__message">`.
- **`CssClass`** appends to it, for a modifier on top of the component's own.
- **`Bare`**, on the components that shape a wrapper, drops the wrapper so the
  screen keeps its own layout — `Tabs`, `TextField`, `TextArea`.
- The per-part class parameters (`TitleCssClass`, `DescriptionCssClass`,
  `ListCssClass`, …) hand over the inner class names, and passing `null` drops
  that element's class entirely.

Between them, a component can usually emit the exact markup the screen was
hand-rolling — same element, same classes, no stylesheet change.

**If it genuinely cannot, add the hook to the library.** A new parameter on one
component is a smaller change than a second implementation, and it is the change
that leaves the storybook telling the truth. Adding a hook means the storybook
too: `StorybookCoverageTests` requires every component to be rendered there.

## When there is nothing to adopt

Sometimes the answer really is that the library has no component for the job, or
that adopting the one it has is separate work with its own visual consequences.
That is a legitimate answer, and it is recorded in one place rather than left
implicit: the exception lists in
`tests/Backlog.ArchitectureTests/SharedControlAdoptionTests.cs`. Each entry is
keyed on a class the element carries and carries a written reason. A companion
test deletes exceptions that stop matching anything, so the list cannot quietly
become a loophole.

## What is enforced, and what is not

`SharedControlAdoptionTests` fails the build on:

1. a raw `button`, `input`, `select`, or `textarea` in an application screen;
2. an element wearing a class that a library component renders. The list of
   those classes is derived from the library on every run — the intersection of
   what the components' sources name and what `components.css` styles — so it
   tracks the library instead of being a copy of it.

What the test **cannot** see is a copy wearing entirely app-owned class names: a
`<p class="pane__loading" role="status">` duplicating `Alert`, or a
`<span class="pane__count">` duplicating `Badge`, are invisible to it because
nothing about those names belongs to the library. Those are a review concern.
When adding markup to a screen, the question to ask is not "does this class name
collide with the library" but **"is the library already drawing this shape?"**

## Where the library is reviewed

The storybook (`src/Harness/Backlog.UI.Storybook`, or the `ui-storybook` Aspire
resource) renders every component with no application behind it. It is where a
component's behaviour is checked and where a new hook is demonstrated. See
`.design/README.md#living-reference-the-ui-storybook`.

`UiLibraryBoundaryTests` keeps that possible by proving the library references no
module, adapter, or application — a component that reads state instead of taking
a parameter cannot be rendered there.
