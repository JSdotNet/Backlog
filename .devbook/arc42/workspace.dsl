/*
 * The C4 model of Prompt Backlog.
 *
 * Authored in c4hero (https://github.com/c4hero/c4hero) and read by the desktop
 * app, which draws each view from this file. It is the only place an element or
 * a view is defined: the arc42 chapters carry no C4 fences of their own.
 *
 * Every view is embedded in the one chapter section it documents, by its key:
 *
 *     ![Container Diagram — Backlog](c4:container-view)
 *
 * That embed is the only statement of the link. The app reads it the other way
 * round to show, on a view, which chapter documents it.
 *
 * Editing: open this folder in c4hero, which writes the file back and keeps node
 * positions in a c4hero sidecar file beside it. The app does not read
 * that sidecar and does not need it. A large model may split into files under
 * `c4/`, which this workspace includes; none exist yet.
 *
 * See tools/diagrams/C4.md.
 */

workspace "Prompt Backlog" "Local-first, AI-first personal work management across desktop, mobile and IDE channels." {

    !identifiers hierarchical

    model {

        me = person "ME" "Personal owner of the system, across projects and devices."

        backlog = softwareSystem "Prompt Backlog" "Local-first personal productivity system: capture, triage, tasks, roadmap, devbook and monitoring." {

            desktop = container "Desktop App" "Windows client, and the only channel that runs the capture sources and manages every domain." ".NET MAUI Blazor Hybrid" {
                shell = component "Shell" "Hosts the panes, the settings screen and the Ask AI panel, and decides which areas are offered." "Blazor"
                tasks = component "Tasks" "Entries, sub-items and the entry text language." "Class Library, Razor Class Library"
                inbox = component "Inbox" "Triage of arriving items: classify, route, promote or discard." "Class Library, Razor Class Library"
                capture = component "Capture" "Source monitors for YouTube channels, website feeds and import manifests, delivering into the inbox." "Class Library, Razor Class Library"
                roadmap = component "Roadmap" "The timeline view over planned work." "Class Library, Razor Class Library"
                progress = component "Progress" "Goals and impediments, and the pull requests that move them." "Class Library, Razor Class Library"
                devbook = component "Devbook" "Builds and reads the devbook database and draws the chapters, diagrams and C4 model." "Razor Class Library"
                brain = component "Brain" "The personal notes folder, read as a devbook beside the repositories." "Razor Class Library"
                dashboard = component "Dashboard" "Monitoring signals, usage and cost, and saved dashboards." "Class Library, Razor Class Library"
                devPc = component "Dev PC Management" "The tools installed on this PC and their configuration." "Razor Class Library"
                sessions = component "Sessions" "The record of Claude and Copilot sessions on this PC." "Razor Class Library"
                notifications = component "Notifications" "Notifications raised on any device, read and dismissed here." "Class Library, Razor Class Library"
                repositoryManagement = component "Repository Management" "Pull-request state of the registered repositories: status, readiness verdict, stacks, merge commands and one cache." "Class Library"
                sync = component "Sync Client" "Device pairing and the push and pull loops against the cloud service." "Class Library, Razor Class Library"
                mcp = component "MCP Server" "Loopback-only MCP endpoint through which agents read and write tasks, roadmap, devbook and sessions." "ASP.NET Core, Kestrel"
                components = component "UI Components" "The shared control library every screen renders through." "Razor Class Library"
                workers = component "Background Workers" "The sync loops, the daily backup, the devbook import and the MCP listener host." "Background services"
                persistence = component "Local Persistence" "The task store, the backups and the settings files." "SQLite, JSON"
            }

            mobile = container "Mobile App" "Capture-first client with offline storage." ".NET MAUI Blazor Hybrid"

            ide = container "IDE Extension" "VS Code extension that captures into the inbox as a paired device." "TypeScript"

            cloud = container "Cloud Service" "Thin optional sync layer: device sync, attachments, dictation transcription and push." "ASP.NET Core" {
                gateway = component "API Gateway & Auth" "Minimal REST surface, device sessions and rate limiting." "ASP.NET Core"
                syncService = component "Sync Service" "The only domain-aware service: stores sync state and serves the delta feeds." "ASP.NET Core"
                transcription = component "Dictation Transcription" "Relays dictation audio to Foundry and keeps none of it." "ASP.NET Core"
                webhookReceiver = component "GitHub Webhook Receiver" "Validates and forwards GitHub webhook events; not built." "ASP.NET Core"
                notificationService = component "Notification Service" "Tells a device to pull notifications, and carries no document itself." "Web PubSub, Notification Hubs"
            }

            taskStore = container "Local Task Store" "Canonical desktop store for tasks, inbox and roadmap; a task's content is markdown text inside the database." "SQLite (backlog.db)" "Database"

            cloudDb = container "Cloud Database" "Sync state, the replica containers and the device registry." "Cosmos DB" "Database"
            attachments = container "Attachments" "Capture attachments, owner-scoped, beside the replica." "Azure Blob Storage" "Database"
        }

        group "Capture sources" {
            youtube = softwareSystem "YouTube" "Subscription feed for content capture." "External"
            email = softwareSystem "Email / IMAP" "Email capture inbox; not built." "External"
            websites = softwareSystem "Websites / RSS" "Web content monitoring by RSS and DOM diff." "External"
        }

        group "Work tracking" {
            github = softwareSystem "GitHub" "Issues, pull requests, repository management and webhook events." "External"
            specManager = softwareSystem "Spec Manager" "A product backlog whose items arrive as linked tasks." "External"
        }

        group "AI" {
            azureFoundry = softwareSystem "Azure AI Foundry" "Chat, plan drafting, triage advice and dictation transcription." "External"
            claude = softwareSystem "Claude" "Usage and cost reporting." "External"
            codingAgents = softwareSystem "Coding Agents" "Claude Code and Copilot sessions on this PC." "External"
        }

        group "Operations" {
            appInsights = softwareSystem "Application Insights" "Telemetry behind the monitoring dashboards." "External"
            pushProvider = softwareSystem "Push Provider" "Azure Notification Hubs, relaying to FCM, for Android alerts." "External"
        }

        # --- how the channels are used -------------------------------------------

        me -> backlog.desktop "Captures, triages, manages tasks, roadmap and devbook" "local"
        me -> backlog.mobile "Captures on the move" "touch / voice"
        me -> backlog.ide "Captures from the editor" "IDE commands"

        # --- desktop ------------------------------------------------------------

        backlog.desktop.shell -> backlog.desktop.tasks "Offers the pane"
        backlog.desktop.shell -> backlog.desktop.inbox "Offers the pane"
        backlog.desktop.shell -> backlog.desktop.capture "Offers the pane"
        backlog.desktop.shell -> backlog.desktop.roadmap "Offers the pane"
        backlog.desktop.shell -> backlog.desktop.progress "Offers the pane"
        backlog.desktop.shell -> backlog.desktop.devbook "Offers the pane"
        backlog.desktop.shell -> backlog.desktop.brain "Offers the pane"
        backlog.desktop.shell -> backlog.desktop.dashboard "Offers the pane"
        backlog.desktop.shell -> backlog.desktop.devPc "Offers the pane"
        backlog.desktop.shell -> backlog.desktop.sessions "Offers the pane"
        backlog.desktop.shell -> backlog.desktop.notifications "Offers the pane"
        backlog.desktop.shell -> backlog.desktop.sync "Offers the Devices settings"
        backlog.desktop.shell -> backlog.desktop.components "Renders through"
        backlog.desktop.shell -> backlog.desktop.repositoryManagement "Reads pull-request status and verdict; runs the merge commands"

        backlog.desktop.tasks -> backlog.desktop.persistence "Reads and writes entries"
        backlog.desktop.inbox -> backlog.desktop.persistence "Reads and writes items"
        backlog.desktop.roadmap -> backlog.desktop.persistence "Reads planned work"
        backlog.desktop.progress -> backlog.desktop.persistence "Reads and writes goals"
        backlog.desktop.inbox -> backlog.desktop.tasks "Routes items to the backlog, through a port"
        backlog.desktop.capture -> backlog.desktop.inbox "Delivers captured items"
        backlog.desktop.tasks -> backlog.desktop.repositoryManagement "Reads linked pull-request state, through an adapter"
        backlog.desktop.progress -> backlog.desktop.repositoryManagement "Reads goal pull-request state, through an adapter"
        backlog.desktop.repositoryManagement -> backlog.desktop.persistence "Remembers merged pull requests"
        backlog.desktop.brain -> backlog.desktop.devbook "Is indexed and read by the devbook builder"
        backlog.desktop.persistence -> backlog.taskStore "Reads and writes" "file system"

        backlog.desktop.workers -> backlog.desktop.sync "Runs the task, session, annotation and notification sync loops"
        backlog.desktop.workers -> backlog.desktop.persistence "Commits the daily backup"
        backlog.desktop.workers -> backlog.desktop.mcp "Hosts the listener"
        backlog.desktop.sync -> backlog.desktop.inbox "Hands over synced captures"
        backlog.desktop.sync -> backlog.desktop.notifications "Hands over synced notifications"
        backlog.desktop.mcp -> backlog.desktop.tasks "Reads and writes entries"
        backlog.desktop.mcp -> backlog.desktop.roadmap "Reads and writes plans"
        backlog.desktop.mcp -> backlog.desktop.devbook "Reads chapters and notes"
        backlog.desktop.mcp -> backlog.desktop.sessions "Records runs and questions"

        backlog.desktop.capture -> youtube "Polls the subscription feed" "HTTPS"
        backlog.desktop.capture -> websites "Monitors by RSS and DOM diff" "HTTPS"
        backlog.desktop.capture -> email "Ingests; not built" "IMAP"
        backlog.desktop.tasks -> github "Syncs issues as linked tasks" "HTTPS / gh CLI"
        backlog.desktop.tasks -> specManager "Links backlog items as tasks" "HTTPS, OAuth"
        backlog.desktop.repositoryManagement -> github "Reads pull requests, checks and reviews; sends merge commands" "HTTPS / gh CLI"
        backlog.desktop.inbox -> azureFoundry "Drafts plans and advises triage" "HTTPS, optional"
        backlog.desktop.shell -> azureFoundry "Answers Ask AI questions" "HTTPS, optional"
        backlog.desktop.dashboard -> appInsights "Reads telemetry signals" "HTTPS"
        backlog.desktop.dashboard -> claude "Reads usage and cost" "HTTPS, optional"
        backlog.desktop.sessions -> codingAgents "Reads session transcripts" "file system"
        backlog.desktop.tasks -> codingAgents "Starts a Copilot session" "CLI"
        backlog.desktop.devPc -> codingAgents "Writes tool configuration and hooks" "file system"
        codingAgents -> backlog.desktop.mcp "Calls tools" "HTTP on loopback"
        backlog.desktop.sync -> backlog.cloud.gateway "Pairs, pushes and pulls state" "HTTPS"

        # --- the other channels --------------------------------------------------

        backlog.mobile -> backlog.cloud.gateway "Syncs captures, notes and state" "HTTPS"
        backlog.ide -> backlog.cloud.gateway "Pairs and posts inbox items as a device" "HTTPS /api/sync/inbox, Bearer"

        # --- cloud ---------------------------------------------------------------

        backlog.cloud.gateway -> backlog.cloud.syncService "Routes requests"
        backlog.cloud.gateway -> backlog.cloud.transcription "Routes dictation audio"
        backlog.cloud.syncService -> backlog.cloudDb "Reads and writes sync state" "TCP"
        backlog.cloud.syncService -> backlog.attachments "Uploads and downloads, owner-prefixed" "Blob SDK"
        backlog.cloud.transcription -> azureFoundry "Transcribes dictation audio" "HTTPS"
        github -> backlog.cloud.webhookReceiver "Delivers webhooks; not built" "HTTPS"
        backlog.cloud.webhookReceiver -> backlog.cloud.syncService "Forwards the event"
        backlog.cloud.syncService -> backlog.cloud.notificationService "Asks for a nudge"
        backlog.cloud.notificationService -> pushProvider "Sends push notifications" "HTTPS"
        backlog.cloud.notificationService -> backlog.mobile "Pull now" "Notification Hubs to FCM"
        backlog.cloud.notificationService -> backlog.desktop "Pull now" "Web PubSub"
        backlog.cloud -> backlog.desktop "Forwards webhook events; not built" "SSE / WebSocket, optional"

        # --- deployment ----------------------------------------------------------

        deploymentEnvironment "Local machine" {
            pc = deploymentNode "Windows PC" "The canonical deployment. Everything needed for core workflows runs here." "Windows 11" {
                msix = deploymentNode "MSIX package" "Signed, sideloaded from GitHub Releases and updated through an App Installer manifest." "MSIX" {
                    containerInstance backlog.desktop
                }

                userRoot = deploymentNode "User-owned root" "The storage root, %LOCALAPPDATA%\Backlog by default, holding backlog.db and the per-device settings." "File system" {
                    containerInstance backlog.taskStore
                    clones = infrastructureNode "Repository working copies" "The devbook folders are read in place, wherever the repository was cloned." "Git"
                }

                vscode = deploymentNode "VS Code" "The IDE extension host on the same machine; the extension talks to the sync service, not the desktop store." "Extension host" {
                    containerInstance backlog.ide
                }
            }

            phone = deploymentNode "Android phone" "Runs the mobile app, which reaches the desktop through the cloud sync service." "Android" {
                containerInstance backlog.mobile
            }
        }

        deploymentEnvironment "Azure" {
            azure = deploymentNode "Azure" "Additive and optional: nothing in the core workflows depends on it being up." "Azure subscription" {
                containerApps = deploymentNode "Azure Container Apps" "Hosts the cloud sync service, on the consumption plan and scaling to zero." "Consumption" {
                    containerInstance backlog.cloud
                }
                data = deploymentNode "Managed data" "The cloud replica and attachment stores the sync service writes to." "PaaS" {
                    cosmos = deploymentNode "Azure Cosmos DB" "Serverless account with eight containers and a change feed per replica container." "Serverless" {
                        containerInstance backlog.cloudDb
                    }
                    storage = deploymentNode "Azure Storage" "Private attachments container with a 30-day lifecycle rule." "Standard LRS" {
                        containerInstance backlog.attachments
                    }
                    keyVault = infrastructureNode "Azure Key Vault" "Holds webhook secrets and OAuth tokens when they arrive." "Key Vault"
                }
                messaging = deploymentNode "Messaging" "The services that tell a device to pull." "PaaS" {
                    webPubSub = infrastructureNode "Azure Web PubSub" "The notifications hub desktops hold a socket to." "Free_F1"
                    notificationHubs = infrastructureNode "Azure Notification Hubs" "Namespace ntfns, hub backlog; relays pushes to FCM." "Free"
                }
            }
        }
    }

    views {

        systemContext backlog "business-context" "System Context — Backlog" {
            include *
            autolayout lr
        }

        container backlog "container-view" "Container Diagram — Backlog" {
            include *
            autolayout lr
        }

        component backlog.desktop "components-desktop" "Component Diagram — Desktop App" {
            include *
            autolayout tb
        }

        component backlog.cloud "components-cloud" "Component Diagram — Cloud Service" {
            include *
            autolayout tb
        }

        dynamic backlog "capture-to-issue" "Capturing an item and pushing it to GitHub" {
            me -> backlog.desktop "Captures an entry"
            backlog.desktop -> backlog.taskStore "Writes the entry"
            backlog.desktop -> github "Creates the issue"
            github -> backlog.cloud "Delivers the webhook (not built)"
            backlog.cloud -> backlog.desktop "Forwards the event (not built)"
            autolayout lr
        }

        dynamic backlog "state-sync" "Syncing task state between devices" {
            backlog.desktop -> backlog.cloud "Pushes changes since the watermark"
            backlog.cloud -> backlog.cloudDb "Upserts documents"
            backlog.mobile -> backlog.cloud "Pulls from its cursor"
            backlog.cloud -> backlog.cloudDb "Reads the change feed"
            github -> backlog.cloud "Delivers a webhook (not built)"
            backlog.cloud -> backlog.cloudDb "Stores the event (not built)"
            backlog.cloud -> backlog.desktop "Forwards the event (not built)"
            autolayout lr
        }

        dynamic backlog "capture-attachments" "A capture's files travelling beside it" {
            backlog.mobile -> backlog.cloud "Uploads a file"
            backlog.cloud -> backlog.attachments "Stages, then commits the blocks"
            backlog.mobile -> backlog.cloud "Posts the capture"
            backlog.cloud -> backlog.attachments "Finds each named file"
            backlog.cloud -> backlog.cloudDb "Upserts the capture document"
            backlog.desktop -> backlog.cloud "Downloads a file"
            backlog.cloud -> backlog.attachments "Downloads the blob"
            backlog.desktop -> backlog.cloud "Pushes the capture tombstone"
            backlog.cloud -> backlog.cloudDb "Finds the capture, upserts the tombstone"
            backlog.cloud -> backlog.attachments "Deletes the capture's files"
            autolayout lr
        }

        dynamic backlog "ide-capture" "Capturing from the VS Code extension" {
            me -> backlog.desktop "Generates a pairing code"
            backlog.desktop -> backlog.cloud "Requests the code"
            me -> backlog.ide "Pairs, then runs the capture command"
            backlog.ide -> backlog.cloud "Pairs, gets a token, posts the capture"
            autolayout lr
        }

        dynamic backlog "mobile-note-sync" "A note syncing both ways" {
            me -> backlog.mobile "Creates or edits a note"
            backlog.mobile -> backlog.cloud "Posts the note document"
            backlog.desktop -> backlog.cloud "Pulls the note, later pushes its edit or tombstone"
            me -> backlog.desktop "Edits, archives or deletes the note"
            backlog.mobile -> backlog.cloud "Pulls changed documents"
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
    }
}
