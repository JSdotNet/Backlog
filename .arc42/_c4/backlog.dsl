/*
 * The C4 model of Prompt Backlog.
 *
 * Authored in c4hero (https://github.com/c4hero/c4hero) and read by the desktop
 * app, which draws each view from this file. It is a parallel, richer view of what
 * the arc42 chapters already say — the chapters keep their own mermaid C4 fences
 * and those stay canonical. Nothing here is generated from them and nothing here
 * generates them.
 *
 * Every view is referenced from the chapter it documents, by that chapter naming
 * the view in its own `related` list:
 *
 *     related: [".arc42/_c4/backlog.dsl#containers-backlog"]
 *
 * That is the only statement of the link. The app reads it the other way round to
 * show, on a view, which chapters it documents — so there is no index beside this
 * file that could fall out of step with the chapters.
 *
 * Editing: open this folder in c4hero, which writes the file back and keeps node
 * positions in a `backlog.c4hero.json` sidecar beside it. The app does not read
 * that sidecar and does not need it.
 *
 * See tools/diagrams/C4.md.
 */

workspace "Prompt Backlog" "Local-first, AI-first personal work management across desktop, mobile and IDE channels." {

    !identifiers hierarchical

    model {

        me = person "ME" "Personal owner of the system, across projects and devices."

        backlog = softwareSystem "Prompt Backlog" "Local-first personal productivity system: capture, triage, backlog, roadmap, knowledge and monitoring." {

            desktop = container "Desktop App" "Windows client, and the only channel that runs the fetch workers and manages every domain." ".NET MAUI Blazor Hybrid" {
                shell = component "Shell" "Hosts the panes, holds the pane selection, and decides which sections are offered." "Blazor"
                inbox = component "Inbox" "Triage of arriving items: classify, route, promote or discard." "Razor Class Library"
                backlogModule = component "Backlog" "Entries, sub-items and the entry text language." "Razor Class Library"
                roadmap = component "Roadmap" "The timeline view over planned work." "Razor Class Library"
                knowledge = component "Knowledge" "Reads the repository knowledge folders and draws their chapters and diagrams." "Razor Class Library"
                dashboard = component "Dashboard" "Monitoring signals and saved dashboards." "Razor Class Library"
                devPc = component "Dev PC Management" "The machines this system knows about and what is installed on them." "Razor Class Library"
                sessions = component "Sessions" "The record of Claude and Copilot sessions on this PC." "Razor Class Library"
                components = component "UI Components" "The shared control library every screen renders through." "Razor Class Library"
                workers = component "Fetch Workers" "Polling for YouTube, websites, email, GitHub sync and stale detection." "Background services"
                persistence = component "Local Persistence" "The task store and the settings files." "SQLite, JSON"
            }

            mobile = container "Mobile App" "Capture-first client with offline storage." ".NET MAUI Blazor Hybrid"

            ide = container "IDE Extensions" "VS Code, Visual Studio and Copilot App integrations." "TypeScript, C#"

            cloud = container "Cloud Service" "Thin optional sync layer: device sync, webhook forwarding, push, PC registry." "ASP.NET Core"

            taskStore = container "Local Task Store" "Canonical store for tasks; a task's content is markdown text inside the database." "SQLite" "Database"

            cloudDb = container "Cloud Database" "Sync state, webhook events, machine registry." "Cosmos DB / PostgreSQL" "Database"
        }

        github = softwareSystem "GitHub" "Issue tracking, repository management and webhook events." "External"
        youtube = softwareSystem "YouTube" "Subscription feed for content capture." "External"
        email = softwareSystem "Email / IMAP" "Email capture inbox." "External"
        websites = softwareSystem "Websites / RSS" "Web content monitoring by RSS and DOM diff." "External"
        appInsights = softwareSystem "Application Insights" "Telemetry behind the monitoring dashboards." "External"
        pushProvider = softwareSystem "Push Provider" "FCM, for Android alerts." "External"

        # --- how the channels are used -------------------------------------------

        me -> backlog.desktop "Captures, triages, manages backlog, roadmap and knowledge" "local"
        me -> backlog.mobile "Captures on the move" "touch / voice"
        me -> backlog.ide "Browses backlog and knowledge; captures from IDE and Copilot sessions" "IDE commands"

        # --- desktop ------------------------------------------------------------

        backlog.desktop.shell -> backlog.desktop.inbox "Offers the pane"
        backlog.desktop.shell -> backlog.desktop.backlogModule "Offers the pane"
        backlog.desktop.shell -> backlog.desktop.roadmap "Offers the pane"
        backlog.desktop.shell -> backlog.desktop.knowledge "Offers the pane"
        backlog.desktop.shell -> backlog.desktop.dashboard "Offers the pane"
        backlog.desktop.shell -> backlog.desktop.devPc "Offers the pane"
        backlog.desktop.shell -> backlog.desktop.sessions "Offers the pane"

        backlog.desktop.inbox -> backlog.desktop.components "Renders through"
        backlog.desktop.backlogModule -> backlog.desktop.components "Renders through"
        backlog.desktop.roadmap -> backlog.desktop.components "Renders through"
        backlog.desktop.knowledge -> backlog.desktop.components "Renders through"
        backlog.desktop.dashboard -> backlog.desktop.components "Renders through"

        backlog.desktop.inbox -> backlog.desktop.persistence "Reads and writes items"
        backlog.desktop.backlogModule -> backlog.desktop.persistence "Reads and writes entries"
        backlog.desktop.roadmap -> backlog.desktop.persistence "Reads planned work"
        backlog.desktop.workers -> backlog.desktop.inbox "Delivers captured items"
        backlog.desktop.persistence -> backlog.taskStore "Reads and writes" "file system"

        backlog.desktop.workers -> youtube "Polls the subscription feed" "HTTPS"
        backlog.desktop.workers -> websites "Monitors by RSS and DOM diff" "HTTPS"
        backlog.desktop.workers -> email "Ingests" "IMAP"
        backlog.desktop.workers -> github "Syncs issues" "HTTPS / gh CLI"
        backlog.desktop.dashboard -> appInsights "Reads telemetry signals" "HTTPS"
        backlog.desktop -> backlog.cloud "Pushes state snapshots" "HTTPS, optional"

        # --- the other channels --------------------------------------------------

        backlog.mobile -> backlog.cloud "Syncs items and pulls state" "HTTPS"
        backlog.ide -> backlog.desktop "Reads backlog and knowledge" "local API / file system"

        backlog.cloud -> backlog.cloudDb "Reads and writes sync state" "TCP"
        backlog.cloud -> pushProvider "Sends notifications" "HTTPS"
        github -> backlog.cloud "Delivers webhooks" "HTTPS"
        backlog.cloud -> backlog.desktop "Forwards webhook events" "SSE / WebSocket, optional"

        # --- deployment ----------------------------------------------------------

        deploymentEnvironment "Local machine" {
            pc = deploymentNode "Windows PC" "The canonical deployment. Everything needed for core workflows runs here." "Windows 11" {
                msix = deploymentNode "MSIX package" "Signed, sideloaded from GitHub Releases and updated through an App Installer manifest." "MSIX" {
                    containerInstance backlog.desktop
                }

                userRoot = deploymentNode "User-owned root" "" "File system" {
                    containerInstance backlog.taskStore
                    clones = infrastructureNode "Repository working copies" "The knowledge folders are read in place, wherever the repository was cloned." "Git"
                }

                vscode = deploymentNode "VS Code / Visual Studio" "" "Extension host" {
                    containerInstance backlog.ide
                }
            }

            phone = deploymentNode "Android phone" "" "Android" {
                containerInstance backlog.mobile
            }
        }

        deploymentEnvironment "Azure" {
            azure = deploymentNode "Azure" "Additive and optional: nothing in the core workflows depends on it being up." "Azure subscription" {
                appService = deploymentNode "App Service" "" "Linux, .NET" {
                    containerInstance backlog.cloud
                }

                data = deploymentNode "Managed data" "" "PaaS" {
                    containerInstance backlog.cloudDb
                }
            }
        }
    }

    views {

        systemLandscape "landscape" "Everything Prompt Backlog touches" {
            include *
            autolayout lr
        }

        systemContext backlog "context-backlog" "System Context — Prompt Backlog" {
            include *
            autolayout lr
        }

        container backlog "containers-backlog" "Container Diagram — Prompt Backlog" {
            include *
            autolayout tb
        }

        component backlog.desktop "components-desktop" "Component Diagram — Desktop App" {
            include *
            autolayout tb
        }

        dynamic backlog "capture-to-issue" "Capturing an item and pushing it to GitHub" {
            me -> backlog.desktop "Captures an entry"
            backlog.desktop -> backlog.taskStore "Writes the entry"
            backlog.desktop -> github "Creates the issue"
            github -> backlog.cloud "Delivers the webhook"
            backlog.cloud -> backlog.desktop "Forwards the event"
            autolayout lr
        }

        deployment backlog "Local machine" "deployment-local" "Local Deployment — the canonical one" {
            include *
            autolayout tb
        }

        deployment backlog "Azure" "deployment-azure" "Cloud Deployment — optional and additive" {
            include *
            autolayout tb
        }

        styles {
            element "External" {
                background #7a7a7a
                color #ffffff
            }
            element "Database" {
                shape Cylinder
            }
        }
    }
}
