<!-- devbook:begin -->
## Devbook folders

Written by `devbook:init` and kept by `devbook:update`. Edit outside these markers; an edit inside them makes the
next reconcile report the section as customized and leave it alone.

This repository keeps its devbook as addressed Markdown chapters. Treat the folders as
task-scoped context, never baseline context: load the chapters a task names, walk
`related` and `depends-on` from them, and never load a folder whole.

| Folder | Holds | Rules |
| --- | --- | --- |
| `.devbook/arc42/` | Structure, decisions, and technical debt | `devbook-arc42.md` |
| `.devbook/domain/` | Bounded contexts and the ubiquitous language | `devbook-domain.md` |
| `.devbook/tech/` | The technology graph and its ratings | `devbook-tech.md` |
| `.devbook/design/` | Design principles, tokens, and component guidelines | `devbook-design.md` |
| `.devbook/ai/` | How the team works with AI, stage by stage; it records a way of working and never instructs one | `devbook-ai.md` |

Every chapter carries a fenced `meta` block; write it in the same change as the content,
per `devbook-chapter-metadata.md`. Skip `annotation` fences when loading a
chapter as context: they hold review notes, not content.

Run the check before committing; it writes nothing:

    node .devbook/_tools/devbook-meta/build.mjs --check

An annotation fence is written only through `.devbook/_tools/devbook-meta/annotations.mjs`.

Nothing personal lives in this repository. Your own settings live under your devbook
config directory — `$XDG_CONFIG_HOME/devbook` when set, else `%APPDATA%\devbook` on
Windows and `~/.config/devbook` elsewhere — for every repository, or under `repos/<id>/`
there for this one, `<id>` being the `id` in `.devbook/config.json`. `AGENTS.local.md`
in either place holds instructions for your machine only: read it when it exists and
treat it as this file's last word. What else lives there, each plugin says for itself.
Put no secret in it — your home directory is not private.
<!-- devbook:end -->
