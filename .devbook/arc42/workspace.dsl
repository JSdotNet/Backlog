/*
 * The C4 model of Prompt Backlog.
 *
 * Authored in c4hero (https://github.com/c4hero/c4hero) and read by the desktop
 * app, which draws each view from this file.
 *
 * Every view is embedded in the one chapter section it documents, by its key:
 *
 *     ![Container Diagram — Backlog](c4:container-view)
 *
 * That embed is the only statement of the link. The app reads it the other way
 * round to show, on a view, which chapter documents it.
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
                devbook = component "Devbook" "Reads the repository knowledge folders and draws their chapters and diagrams." "Razor Class Library"
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
            localStore = container "Local Storage" "Desktop canonical data store — tasks, inbox and roadmap plans; task content is markdown text in the database" "SQLite (backlog.db)" "Database"
            attachments = container "Attachments" "Capture attachments, owner-scoped, beside the replica" "Azure Blob Storage, container attachments" "Database"
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
        backlog.desktop.shell -> backlog.desktop.devbook "Offers the pane"
        backlog.desktop.shell -> backlog.desktop.dashboard "Offers the pane"
        backlog.desktop.shell -> backlog.desktop.devPc "Offers the pane"
        backlog.desktop.shell -> backlog.desktop.sessions "Offers the pane"

        backlog.desktop.inbox -> backlog.desktop.components "Renders through"
        backlog.desktop.backlogModule -> backlog.desktop.components "Renders through"
        backlog.desktop.roadmap -> backlog.desktop.components "Renders through"
        backlog.desktop.devbook -> backlog.desktop.components "Renders through"
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

                userRoot = deploymentNode "User-owned root" "The storage root, %LOCALAPPDATA%\Backlog by default, holding backlog.db and the per-device settings." "File system" {
                    containerInstance backlog.taskStore
                    clones = infrastructureNode "Repository working copies" "The knowledge folders are read in place, wherever the repository was cloned." "Git"
                }

                vscode = deploymentNode "VS Code / Visual Studio" "The IDE extension host on the same machine; the extension talks to the sync service, not the desktop store." "Extension host" {
                    containerInstance backlog.ide
                }
            }

            phone = deploymentNode "Android phone" "Runs the mobile app, which reaches the desktop through the cloud sync service." "Android" {
                containerInstance backlog.mobile
            }
        }

        deploymentEnvironment "Azure" {
            azure = deploymentNode "Azure" "Additive and optional: nothing in the core workflows depends on it being up." "Azure subscription" {
                appService = deploymentNode "App Service" "Hosts the cloud sync service." "Linux, .NET" {
                    containerInstance backlog.cloud
                }

                data = deploymentNode "Managed data" "The cloud replica and attachment stores the sync service writes to." "PaaS" {
                    containerInstance backlog.cloudDb
                }
            }
        }
        externalSources = softwareSystem "External Sources" "YouTube, Email, Websites / RSS" "External"
        azureFoundry = softwareSystem "Azure AI Foundry" "Chat, embeddings and plan drafting; resource cost" "External"
        claude = softwareSystem "Claude" "Usage and cost reporting" "External"

        me -> backlog "Captures, triages, manages backlog and knowledge"
        backlog -> github "Syncs issues; receives webhook events"
        backlog -> youtube "Polls subscription feed"
        backlog -> email "Ingests via IMAP"
        backlog -> websites "Monitors via RSS and DOM diff"
        backlog -> appInsights "Reads telemetry signals"
        backlog -> pushProvider "Sends push notifications"
        backlog.desktop -> backlog.localStore "Reads and writes" "SQLite"
        backlog.desktop -> github "Syncs issues" "HTTPS / gh CLI"
        backlog.desktop -> externalSources "Polls for new content" "HTTPS / IMAP"
        backlog.desktop -> azureFoundry "Drafts plans and reads cost" "HTTPS, optional"
        backlog.desktop -> claude "Reads usage" "HTTPS, optional"
        backlog.ide -> backlog.cloud "Reads and posts inbox items as a paired device" "HTTPS /api/sync/inbox, Bearer"
        backlog.cloud -> backlog.attachments "Stores and serves attachments" "Blob SDK"
        backlog.cloud -> github "Receives webhooks" "HTTPS"
    }

    views {

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
        systemContext backlog business-context {
            title "System Context — Backlog"
            include me github youtube email websites appInsights pushProvider
            autolayout lr
        }
        container backlog container-view {
            title "Container Diagram — Backlog"
            include me backlog.desktop backlog.mobile backlog.ide backlog.cloud backlog.localStore backlog.cloudDb backlog.attachments github pushProvider externalSources azureFoundry claude
            autolayout lr
        }
    }
}
