namespace Backlog.UI.Storybook.Components.Shared;

/// <summary>
/// The documents the three file-pane pages are drawn from: the container on
/// <c>FileViewPage</c>, and its two halves on <c>FileHeaderPage</c> and
/// <c>FileContentPage</c>.
/// </summary>
/// <remarks>
/// <para>
/// Written out by hand, and nothing here reads a repository — the same rule
/// <see cref="CompareFixtures"/> keeps, for the same reason: the storybook
/// references the component library and the service defaults and nothing else.
/// Where a body is a cut of a real file in this repository it says so, because
/// the story it feeds is about what a real file does and a made-up one would
/// only prove that a made-up one does it.
/// </para>
/// <para>
/// Shared so that a header, a body and the pane composed of both are shown over
/// the same text. A header story copying one version of a document while the
/// content page renders another is a drift nobody would notice until the two
/// disagreed about the file's size.
/// </para>
/// </remarks>
internal static class FileFixtures
{
    /// <summary>A short knowledge chapter with a title, a paragraph, a list and
    /// two sections: enough to scroll at a modest cap, and the body the header
    /// stories offer to copy.</summary>
    public const string SharedTechnologies = """
        # Shared Technologies

        What the whole product is built on, and why each choice was made.

        - The runtime, pinned
        - The UI framework
        - What was weighed against both

        ## Hosting

        Where it runs, and what it costs to move.

        ## Storage

        One file per chapter, and the index derived from them.
        """;

    /// <summary>The top of this repository's own <c>.tech/shared.md</c>, cut after
    /// the second chapter. Not invented: the point of the story is which of two
    /// real blocks lands where, and a made-up file would only prove that a made-up
    /// file does it.</summary>
    public const string TwoLevelFile = """
        # Shared Technologies

        ```meta
        status: candidate
        related: [".tech/technology-graph.md", ".arc42/02-constraints.md#technical-constraints"]
        ```

        Technologies used by more than one channel. Every layer file points at these
        chapters with `depends-on` instead of redefining them locally.

        ## Markdown

        ```meta
        status: adopted
        kind: format
        related: [".arc42/08-crosscutting-concepts.md#storage-and-sync"]
        ```

        The canonical storage format for all user content: inbox items, backlog
        items, knowledge notes, and prompts.

        ## .NET Runtime

        ```meta
        status: candidate
        kind: runtime
        version: "10.0"
        alternatives: ["Node.js only", "Rust + Tauri"]
        ```

        The primary managed runtime for the desktop, mobile, IDE and cloud channels.
        """;

    public const string CSharpBody = """
        namespace Backlog.UI.Components.Markdown;

        /// <summary>The parser the read view and the file view share.</summary>
        public static class MarkdownPreview
        {
            // A `#` here is a comment, and a `*` is a dereference nobody wrote.
            // Both are why a source file is never handed to the markdown parser.
            public static IReadOnlyList<MdBlock> ParseDocument(string? body)
            {
                if (string.IsNullOrEmpty(body)) return [];

                var lines = body.Replace("\r\n", "\n").Split('\n');

                return Read(lines, headingsAreHeadings: true);
            }
        }
        """;

    public const string JsonBody = """
        {
          "Logging": {
            "LogLevel": {
              "Default": "Information",
              "Microsoft.AspNetCore": "Warning"
            }
          },
          "AllowedHosts": "*"
        }
        """;

    public const string DockerfileBody = """
        FROM mcr.microsoft.com/dotnet/aspnet:10.0

        WORKDIR /app
        COPY . .

        # No extension to read, so the caller names the language.
        ENTRYPOINT ["dotnet", "Backlog.Cloud.dll"]
        """;

    public const string MermaidBody = """
        %% .mmd is on the list, so this one names itself.
        stateDiagram-v2
            [*] --> Captured
            Captured --> Refining
            Refining --> Active
            Active --> Done
            Done --> [*]
        """;

    public const string ShortBody = """
        # Harness projects

        Neither project here is deployed. They exist so a component can be looked
        at without starting the application.

        - `Backlog.UI.Storybook` — every shared component, on its own
        - `Backlog.Desktop.WebHarness` — the desktop UI in a browser
        """;

    /// <summary>The file from the request that prompted the file pane, kept long
    /// enough that the story actually scrolls.</summary>
    public const string LongBody = """
        # Repository orchestration and context policy

        General orchestration routing — which `orch-*` skill or specialist agent
        handles which task category, and its fallbacks — is delivered globally by
        the `copilot-app` plugin and is no longer restated in this repository.

        This file covers only what is specific to Backlog: **the gate that forces
        code changes through an orchestration skill**, and **which checked-in
        knowledge folders a given workflow may read, and how much of them.**

        ## The gate

        Before the first `edit` or `create` to any file under `src/` or `tests/`,
        you must invoke the matching `orch-*` skill through the orchestrator
        agent. Exploration first is expected and does not consume the gate; the
        trigger is the first write, not the first action.

        Apply the gate literally:

        - **Size is not a criterion.** A one-control UI tweak and a multi-service
          feature route the same way.
        - **A missing specification is not an exemption.** Ad-hoc requests still
          route through `orch-feature` or `orch-bug`.
        - **Unmet preconditions are not an exemption.** Invoke the skill anyway
          and say so.
        - **No match means `orch-fallback`**, not direct implementation.

        ## Context loading

        > Treat the knowledge folders as task-scoped context, not baseline
        > context.

        1. Architecture workflows may load `.arc42/`, one chapter at a time.
        2. Domain workflows may load `.domain/`, one bounded context at a time.
        3. UI workflows should consult `.design/` when the change touches visual
           design, interaction, content editing or accessibility.

        ```csharp
        var blocks = MarkdownPreview.Parse(File.ReadAllText(path));
        ```

        ---

        Startup and QA expectations live alongside this file, and per-category
        model overrides live beside those.
        """;

    /// <summary>An arc42 chapter a host's own editor is handed, in the story
    /// where the body is the host's and the frame is the pane's.</summary>
    public const string ChapterSource = """
        # Building block view

        The desktop channel is one MAUI Blazor Hybrid shell over the shared
        component library. The shell owns the window; everything a reader looks
        at is Razor in a WebView.

        ## Knowledge panes

        A pane shows one folder or one file, and the two use the same furniture
        so that moving between them does not move the header.
        """;

    /// <summary>The chapter from the request that prompted the header actions,
    /// trimmed to the parts that show them off: two chapters with a status of
    /// their own, a diagram written inside one of them, and enough prose to have
    /// somewhere to hang a remark.</summary>
    public const string DomainChapter = """
        # Domain: Tasks

        ```meta
        status: draft
        order: ["features.md", "model.md", "flow.md"]
        ```

        Tasks maintains a personal backlog of prompts, tasks, ideas and
        follow-ups across multiple projects and repos. It converts triaged Inbox
        Items into actionable, prioritised Backlog Entries and projects them to
        external systems such as GitHub and the Copilot CLI.

        ## Aggregate: Backlog Entry

        ```meta
        status: proposed
        related: [.domain/inbox/domain.md#aggregate-inbox-item]
        ```

        A refined, actionable item in the personal backlog and the consistency
        boundary for all of its sub-items, projections and usage history. It is the
        single source of truth: one logical item with one priority and one status
        even when it targets multiple repositories.

        ```mermaid
        stateDiagram-v2
            [*] --> Draft
            Draft --> Ready
            Ready --> InProgress: EntryProjected
            InProgress --> Done: EntryCompleted
            Done --> [*]
        ```

        Invariants: status only moves through the defined lifecycle; all mutations
        to sub-items go through the root; parent progress reflects sub-item
        completion.
        """;

    /// <summary>The same chapter with the references a real one carries: written
    /// in prose as code spans, and once as a markdown link, which is how the
    /// checked-in folders actually write them.</summary>
    public const string ReadingChapter = """
        # Domain: Tasks

        ```meta
        status: draft
        order: ["features.md", "model.md", "flow.md"]
        ```

        Tasks converts triaged Inbox Items into actionable, prioritised
        Backlog Entries. What an Inbox Item is belongs to `.domain/inbox/domain.md`,
        and the system this sits inside is described in
        [the context and scope chapter](.arc42/03-context-and-scope.md).

        The word `order` in the block above is a field name, and `dotnet build` is a
        command. Neither is a place, so neither becomes a link.

        ## Aggregate: Backlog Entry

        ```meta
        status: proposed
        related: [.domain/inbox/domain.md#aggregate-inbox-item]
        ```

        The consistency boundary for an entry's sub-items, projections and usage
        history.

        ```mermaid
        stateDiagram-v2
            [*] --> Draft
            Draft --> Ready
            Ready --> InProgress: EntryProjected
            InProgress --> Done: EntryCompleted
            Done --> [*]
        ```
        """;

    /// <summary>The same chapter as a commit has it: one status agreed, one
    /// paragraph shorter, and a lifecycle state nobody had added yet.</summary>
    public const string CommittedDomainChapter = """
        # Domain: Tasks

        ```meta
        status: active
        order: ["features.md", "model.md", "flow.md"]
        ```

        Tasks maintains a personal backlog of prompts, tasks, ideas and
        follow-ups across multiple projects and repos.

        ## Aggregate: Backlog Entry

        ```meta
        status: proposed
        related: [.domain/inbox/domain.md#aggregate-inbox-item]
        ```

        A refined, actionable item in the personal backlog and the consistency
        boundary for all of its sub-items, projections and usage history.

        ```mermaid
        stateDiagram-v2
            [*] --> Draft
            Draft --> Ready
            Ready --> InProgress: EntryProjected
            InProgress --> Done: EntryCompleted
            Done --> [*]
        ```
        """;

    /// <summary>A context map before and after a chapter was added, for the pane
    /// that aligns two versions by heading.</summary>
    public const string ContextMapBefore = """
        # Context map

        Two bounded contexts, one of them shared.

        ## Tasks

        Owns the personal backlog and everything projected out of it.

        ## Second Brain

        Owns the checked-in knowledge folders.
        """;

    public const string ContextMapAfter = """
        # Context map

        Three bounded contexts, and the language each of them owns.

        ## Tasks

        Owns the personal backlog and everything projected out of it.

        ## Second Brain

        Owns the checked-in knowledge folders and the chapters inside them.

        ## Shell

        Owns the window, the panes in it, and nothing a pane renders.
        """;

    /// <summary>An instruction file as this repository writes one: several globs
    /// in a single quoted scalar, and a description carrying punctuation of its
    /// own.</summary>
    public const string InstructionFile = """
        ---
        applyTo: "src/App/**,src/Modules/**,src/Core/Backlog.UI.Components/**"
        description: An application screen renders the shared component library's components rather than growing its own copies; how to make a component fit, and what to do when it cannot.
        ---

        # UI components

        `src/Core/Backlog.UI.Components` is the product's own component library. It
        is a deliberate choice over a third-party suite, and the price of that
        choice is that accessibility semantics, keyboard support and focus handling
        are the product's own work.

        ## The rule

        A screen renders the library's component. It does not write its own version
        of one.
        """;

    /// <summary>The other shape: a list-valued `tools` key, no globs at all, and a
    /// `mode` the strip has no field of its own for — which is a row of its own
    /// rather than a line quietly taken out of the file.</summary>
    public const string PromptFile = """
        ---
        description: Walk one request through the modules it touches and say where the boundaries are.
        mode: agent
        tools: ['codebase', 'search/codebase', 'read/readFile']
        ---

        # Explain a flow

        Name the entry point, then follow it until it leaves the process.
        """;
}
