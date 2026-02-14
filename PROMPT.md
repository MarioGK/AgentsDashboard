 Explore and analyze the current project, create multiple tasks and run them in paralell, keep the CLAUDE.md updated, check what is missing and implement the plan below
 
 # AI Orchestrator

  ## Summary

  Build a self-hosted orchestration platform on .NET 10 where Blazor Server Side interactive is the control plane and execution is done only
  through CLI harnesses: codex, zai(which is claude code but configured for glm-5), opencode, and claude code.
  Use a hierarchy-first operating model: Projects -> Repositories -> Tasks, where each repository owns one-shot, cron, and event-
  driven tasks plus a triage inbox for findings.
  
  ## Harness
  1. To install zai use the following command `npx cc-mirror quick --provider zai --api-key "$Z_AI_API_KEY"`

  ## Product Model

  1. Project is an umbrella workspace containing multiple repositories and shared defaults.
  2. Repository is the operational unit for task templates, schedules, credentials mapping, findings, and run history.
  3. Task is the runnable object with three first-class kinds: OneShot, Cron, EventDriven.
  4. Run is an execution instance of a task with full logs, artifacts, status timeline, and remediation outcomes.
  5. Findings Inbox exists per repository and aggregates failed runs, high-severity QA issues, and pending approvals.

  ## UX Structure (MudBlazor + BlazorMonaco)

  1. Global shell: Projects list, repository switcher, global health.
  2. Project view: repository cards, aggregate reliability, active automations.
  3. Repository view: task catalog, schedules, findings inbox, run history, environment profiles.
  4. Task editor: template metadata + prompts/scripts in BlazorMonaco.
  5. Run detail: live logs, stage timeline, artifacts, proxy links, PR links.
  6. Findings inbox: severity filters, assign/acknowledge/retry/create-followup-task.
  7. Settings: harness availability, credential mapping checks, Docker policy, retention, observability endpoints.
  8. Container image builder: we must have an easy to use AI assisted zai to help me create container files

  ## Codex-Inspired Patterns to Adopt

  1. Isolated execution workspace per run (worktree-like isolation semantics for each job container).
  2. Project-scoped multitasking model where many task runs can execute concurrently per repo with limits.
  3. Automation triage workflow pattern: queue of outcomes needing review, with retry and promote actions.
  4. Approval and sandbox profiles per task template, not global-only.
  5. Structured non-interactive run contract so every harness execution emits machine-parseable results.
  6. Agent instruction layering by repository policy files (for example task-specific instruction files loaded into prompt context).

  ## Architecture

  1. ControlPlane (Blazor Server + APIs + SignalR + Scheduler + YARP + Docker client).
  2. WorkerGateway (ASP.NET Core gRPC + Channel<> local queues + container runner).
  3. mongodb as system of record.
  4. victoria-metrics and vmui with OpenTelemetry instrumentation via Aspire.
  5. Docker Compose single-host topology

  ## Execution Model (Harness-Only)

  1. No direct provider API usage.
  2. Worker launches ephemeral job containers with prebuilt tool images containing codex, opencode, claude code, gh, Playwright, test toolchain.
  3. Control plane dispatches durable job intents to worker; worker executes CLI subprocesses.
  4. Each harness invocation must produce standardized JSON envelope persisted by control plane.
  5. Standard envelope fields: runId, taskId, status, summary, actions, artifacts, metrics, rawOutputRef, error.
  6. Exit-code and schema validation gates determine stage success/failure.
  7. Create good and default Dockerfile container with all the harness installed, dotnet 10, bun and all other cli dev tools needed to have a full complete dev environment.

  ## Security and Privilege Model

  1. Control plane has full Docker socket access.
  2. Only control-plane orchestration service may create/stop/remove job containers.
  3. Mandatory container labels for ownership and lifecycle enforcement.
  5. Secret redaction required on logs/events/artifacts before storage and broadcast.
  6. This privilege model is supported only because this will run on a trusted computer and trusted network.

  ## Reverse Proxy (Embedded YARP)

  1. YARP runs inside control plane.
  2. Dynamic routes are published only for active run-owned endpoints/artifacts.
  3. Route creation requires ownership label verification.
  4. Route TTL and cleanup on run completion or timeout.
  5. Proxy audit records must include projectId, repoId, taskId, runId, upstream target, and latency.

  ## Data Model (MongoDB)

  1. Collections: projects, repositories, tasks, task_schedules, runs, run_events, findings, artifacts, workers, provider_configs.
  2. Required task fields: kind, harness, instructions, commands, timeouts, retryPolicy, approvalProfile, sandboxProfile, artifactPolicy.
  3. Required run fields: state, startedAt, endedAt, attempt, resultEnvelopeRef, failureClass, prUrl.
  4. TTL defaults: logs/events 30 days, run metadata 90 days.
  5. Findings states: New, Acknowledged, InProgress, Resolved, Ignored.

  ## Public Interfaces and Contracts

  1. REST endpoints: /api/projects, /api/repositories, /api/tasks, /api/runs, /api/findings, /api/schedules, /api/workers.
  2. Trigger endpoints: manual run creation, schedule management, webhook ingestion.
  3. SignalR events: run status/log chunks, findings updates, worker heartbeat, proxy route availability.
  4. gRPC between control plane and worker: DispatchJob, CancelJob, StreamJobEvents, Heartbeat.
  5. Harness adapter interface: PrepareContext, BuildCommand, Execute, ParseEnvelope, MapArtifacts, ClassifyFailure.
        Use a really good nuget package to manage CLI process like codex,opencode and claude code.

  ## Built-in

  1. QA Browser Sweep: Playwright flow execution with stress clicks/repeated actions, screenshot/video/trace output.
        Steps must be recorded in an easy to see file and videos and screenshots must be saved so we can replicate the problem manually
  2. Unit Test Guard: execute tests, invoke harness fix loop on failure, rerun tests, open GitHub PR on success.
  3. Dependency Health Check: run package audit and compatibility checks with actionable findings.
  4. Regression Replay: replay recent failure scenarios and compare outcomes.

  ## Scheduling and Reliability

  1. Trigger types: manual, cron, webhook event.
  2. Scheduler tick every 10 seconds with drift-safe next-run calculation.
  3. Concurrency controls: global cap, per-project cap, per-repository cap, per-task cap.
  4. Retries with exponential backoff and max-attempt policy per task.
  5. Restart recovery rehydrates pending/running intents from Mongo and reconciles orphan containers.
  6. Dead-run protection via per-stage timeout and forced termination policy.

  ## GitHub and PR Automation

  1. Use gh CLI only for branch/push/PR lifecycle.
  2. Branch naming convention: agent/<repo>/<task>/<runId>.
  3. PR creation requires passing validation stage results.
  4. PR body template includes issue summary, diffs summary, tests run, and risk notes.
  5. Failures in Git operations generate structured finding records in repository inbox.

  ## Observability

  1. Aspire defines service composition and local operational wiring.
  2. OpenTelemetry tracing/metrics/log correlation across control plane, worker, gRPC, Mongo, and harness execution.
  3. VictoriaMetrics stores metrics; VMUI dashboards cover throughput, latency, failures, queue depth, worker saturation.
  4. Alerts: missing heartbeat, failure-rate spike, queue backlog threshold, repeated PR failures, route-leak detection.

  ## Testing and Acceptance Criteria

  1. Functional: create project, add repos, define tasks, run one-shot, run cron, trigger webhook, view findings.
  2. Execution: all three harnesses run successfully in isolated containers with normalized envelopes.
  3. QA template: detects UI failures and publishes artifacts.
  4. Unit-test template: failing tests can be fixed and PR opened through gh.
  5. Reliability: control-plane restart preserves intent and no silent run loss.
  6. Security: secret redaction passes log scan tests; non-allowlisted images are blocked.
  7. Proxy: YARP routes appear/disappear correctly and are auditable.
  8. Performance target: 50 concurrent jobs and sub-2s p95 status update latency.

  ## Important API notes

  1. Introduce Repository and Task and everything must be async.
  2. Add Findings aggregate and triage operations.
  3. Standardize run result on harness JSON envelope contract.
  4. Expand trigger model to include webhook-based EventDriven tasks.

  ## Assumptions and Defaults

  1. Single-host, single-operator, trusted environment, everything must be accessible in LAN network.
  2. Docker is required and socket is mounted into control plane.
  3. Use the official docker sdk/nuget to control and communicate with the docker socket.
  4. No direct AI provider APIs are used; harness CLI tools like opencode,codex and claude code.
  5. MudBlazor and BlazorMonaco are mandatory UI/editor standards.
  6. Credentials for all tools must come from host configuration page and be injected into the worker container.
  7. MongoDB is the source of truth; queue transport is hybrid durable-intent + in-memory dispatch.

  ## Tests
  
  1. All features should have integrations tests
  2. All ui should have Playwright integration tests
