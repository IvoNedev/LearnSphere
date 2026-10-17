# prescreen-brief-ivo-nedev — AI Training Content Generator (POC)

Source brief: `Task/prescreen-brief-ivo-nedev.md` (supplied as a direct path; there is no `GlobalHelperX/StartHere/` copy).

## 1. Summary

Build a proof-of-concept web app for LearnSphere L&D managers. A manager pastes a long video-course transcript and gets back:

- an AI-generated **summary**, and
- an AI-generated **multiple-choice quiz**.

Required deliverables:

- A C#/.NET REST API that orchestrates the LLM calls. This is the main focus and what will be judged.
- A simple web UI that shows the end-to-end flow.
- A thorough `README.md` covering setup, design choices and trade-offs, and which AI assistants were used.
- The **complete, unedited** AI-assistant conversation logs, committed to the repo.
- Delivery as one public GitHub repo.

Constraints: due in 7 days, about 8 hours of work. The emphasis is on architecture, API design, the quality of the LLM interaction, testability, and how well trade-offs are explained.

## 2. Domain / context

- **User:** an L&D manager at a corporate client, not a developer. The UI must explain long waits and failures in plain language.
- **Input:** transcripts of hour-long videos, roughly 8k–20k words (50k–150k characters). Size matters here: one prompt may not fit, and it would be slow and expensive. The design has to handle long input on purpose.
- **Output quality:** the summary has to be faithful to the transcript, and every quiz question has to be answerable from it. Hallucinated facts are the main risk to quality.
- **Evaluation:** a reviewer will clone the repo and run it, probably **without an API key**. A deterministic mock LLM that works out of the box is therefore required, not optional.

## 3. Current repository state

`C:\Users\inede\Documents\repos\LearnSphere` is **greenfield**:

| Path | Contents |
|---|---|
| `Task/prescreen-brief-ivo-nedev.md` | The brief |
| `AGENTS.md` | Codex instructions: archive conversations to `Context/Codex/` |
| `Context/Codex/` | Codex conversation logs (README and one log) |
| `Context/Claude/` | Claude Code conversation logs, auto-generated as Markdown |
| `.claude/settings.json`, `.claude/hooks/save-transcript.js` | Stop and SessionEnd hooks that render the transcripts |

- The folder is **not a git repo** yet.
- There is no existing application code, so there are no in-repo patterns to follow. The plan uses standard ASP.NET Core conventions (Minimal APIs, Options pattern, `ProblemDetails`, `BackgroundService`, EF Core).

**Tooling on this machine:**

- .NET SDK **10.0.201 / 10.0.401** only; there is no .NET 8 SDK.
- Node 24.11, npm 11.6
- Docker 29.7
- git 2.51
- **`gh` is not installed**

## 4. Decisions made while planning

| # | Decision | Reasoning |
|---|---|---|
| D1 | Target **`net10.0`** (C# 14) rather than .NET 8 | .NET 10 is the current LTS and the only SDK installed. The brief says the stack is a *suggestion*. Explain this in the README. See open question Q1. |
| D2 | **Asynchronous job API**: `POST` returns `202 Accepted` and an id, the client polls `GET` | Map-reduce over long transcripts can take 30–120 s, so a synchronous request risks proxy and browser timeouts. A job model is also the honest production shape. It stays simple with an in-process `Channel<Guid>` and a `BackgroundService`. |
| D3 | **Map-reduce generation** for long transcripts, with a single pass when the transcript fits the budget | Keeps each prompt within the context and cost budget and lets chunk calls run in parallel. Short transcripts skip the extra round trip. |
| D4 | LLM access goes through **`Microsoft.Extensions.AI` `IChatClient`**, with a provider chosen in config: `Mock` (default), **`OpenAICompatible`** (primary real provider: **local Qwen** behind Ollama, LM Studio, vLLM or llama.cpp at a configurable `Endpoint`, no key required), `OpenAI`, `AzureOpenAI` | **User decision: test against local Qwen.** One OpenAI-compatible client (the `OpenAI` SDK with a custom `Endpoint`) covers every local server and also OpenRouter, Groq and OpenAI itself. Makes it trivial to fake in tests. Swapping providers is a config change. |
| D5 | **Structured JSON output** with a defined schema, then **server-side validation** and **one repair retry** | LLM output is untrusted. The validator enforces the quiz invariants (§7.4). If it still fails after the repair attempt, the job fails visibly rather than storing bad data. |
| D6 | **Deterministic `MockChatClient`** is the default provider | The app runs end-to-end with no API key, and integration tests are deterministic. |
| D7 | Storage: **EF Core + SQLite** (file DB), created at startup with `EnsureCreated` | Survives restarts, inspectable, no server needed. `EnsureCreated` instead of migrations is a deliberate POC trade-off; document it in the README. |
| D8 | Frontend: **Angular** (standalone components, signals, `HttpClient`), developed in `src/LearnSphere.Web`. In development it runs through the CLI proxy. In production the build output is **served from the API's `wwwroot`**. | **User requirement.** Serving the build from the API keeps one origin (no CORS in production) and one container. The dev proxy (`proxy.conf.json`) avoids CORS locally too. It costs about 1–1.5 h more than plain JS, which the time budget absorbs (§20). |
| D9 | Solution layout: **Api / Core / Infrastructure + two test projects** | Shows layered, testable design without over-engineering. Core has no infrastructure dependencies. |

## 5. Proposed solution structure

```text
LearnSphere/
├─ LearnSphere.sln
├─ Directory.Build.props              # net10.0, Nullable, TreatWarningsAsErrors, ImplicitUsings
├─ src/
│  ├─ LearnSphere.Api/                # ASP.NET Core host, Minimal API endpoints, wwwroot UI
│  │  ├─ Program.cs
│  │  ├─ Endpoints/GenerationEndpoints.cs
│  │  ├─ Contracts/                   # request/response DTOs (API surface, versioned)
│  │  ├─ Validation/CreateGenerationRequestValidator.cs
│  │  ├─ appsettings.json / appsettings.Development.json
│  │  └─ wwwroot/                     # Angular production build output (git-ignored, produced by build/Docker)
│  ├─ LearnSphere.Web/                # Angular app (ng new --standalone --routing --style=scss)
│  │  ├─ proxy.conf.json              # /api -> http://localhost:<api-port> for `ng serve`
│  │  └─ src/app/
│  │     ├─ core/api/ generations.api.ts, models.ts   # typed HttpClient service + DTO interfaces
│  │     ├─ features/new-generation/  # form: title, transcript, options, sample loader
│  │     ├─ features/generation-detail/ # polling status, summary, interactive quiz
│  │     ├─ features/history/         # recent generations list
│  │     └─ shared/ problem-details.ts, error-banner component
│  ├─ LearnSphere.Core/               # domain + application logic, no EF/HTTP deps
│  │  ├─ Domain/ Generation.cs, GenerationStatus.cs, Quiz.cs, QuizQuestion.cs
│  │  ├─ Abstractions/ IGenerationRepository.cs, IGenerationQueue.cs
│  │  ├─ Generation/ ContentGenerationService.cs (orchestrator)
│  │  ├─ Generation/ TranscriptChunker.cs
│  │  ├─ Generation/ Prompts.cs (versioned prompt templates)
│  │  ├─ Generation/ LlmJson.cs (schema + parse helpers)
│  │  ├─ Generation/ QuizValidator.cs
│  │  └─ Options/ GenerationOptions.cs
│  └─ LearnSphere.Infrastructure/
│     ├─ Persistence/ AppDbContext.cs, GenerationRepository.cs
│     ├─ Queue/ ChannelGenerationQueue.cs, GenerationWorker.cs (BackgroundService)
│     ├─ Llm/ LlmServiceCollectionExtensions.cs (provider switch)
│     ├─ Llm/ MockChatClient.cs
│     └─ DependencyInjection.cs
├─ tests/
│  ├─ LearnSphere.Core.Tests/         # xUnit: chunker, validator, orchestrator with fake IChatClient
│  └─ LearnSphere.Api.Tests/          # WebApplicationFactory integration tests (mock LLM, SQLite in-memory)
├─ samples/ transcript-short.txt, transcript-long.txt   # for demo + tests
├─ Dockerfile, docker-compose.yml, .dockerignore
├─ .gitignore, .editorconfig
├─ README.md
└─ Context/Claude, Context/Codex, AI-LOGS.md (see §11)
```

**NuGet packages** (pin to current stable versions at implementation time):

| Project | Packages |
|---|---|
| Api | `Microsoft.AspNetCore.OpenApi`, `Scalar.AspNetCore` (or Swashbuckle) for API docs UI |
| Infrastructure | `Microsoft.EntityFrameworkCore.Sqlite`, `Microsoft.Extensions.AI`, `Microsoft.Extensions.AI.OpenAI`, `Azure.AI.OpenAI` (optional), `OllamaSharp` (optional) |
| Tests | `xunit`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`, `Microsoft.AspNetCore.Mvc.Testing`, `FluentAssertions` (or Shouldly; note the FluentAssertions v8 licence change, so prefer **Shouldly** or plain asserts) |

Validation: use hand-rolled endpoint validation or `MiniValidation`. **Skip FluentValidation** to keep the dependency count low.

## 6. API design

Base path is `/api/v1`, all JSON, camelCase. Errors use RFC 7807 `ProblemDetails`.

| Method | Route | Purpose | Responses |
|---|---|---|---|
| `POST` | `/api/v1/generations` | Submit a transcript | `202` + `Location: /api/v1/generations/{id}` + body `{ id, status }`; `400` validation ProblemDetails; `413` too large |
| `GET` | `/api/v1/generations/{id}` | Status and result | `200` with full resource; `404` |
| `GET` | `/api/v1/generations?page=&pageSize=` | Recent jobs, without transcript or result bodies | `200` |
| `POST` | `/api/v1/generations/{id}/retry` *(optional, stretch)* | Re-queue a failed job | `202`; `409` if not failed |
| `GET` | `/health` | Liveness, including a DB check | `200` |
| `GET` | `/openapi/v1.json` and `/scalar` | API docs | |

**Request:**

```json
{
  "title": "Onboarding: Data Privacy Basics",
  "transcript": "…",
  "options": { "questionCount": 8, "summaryLength": "medium" }
}
```

**Response (succeeded):**

```json
{
  "id": "…", "title": "…", "status": "Succeeded",
  "createdAt": "…", "completedAt": "…",
  "summary": { "overview": "…", "keyPoints": ["…"] },
  "quiz": { "questions": [ { "question": "…", "options": ["A","B","C","D"], "correctOptionIndex": 2, "explanation": "…" } ] },
  "metadata": { "provider": "OpenAI", "model": "gpt-4o-mini", "promptVersion": "v1", "chunkCount": 4, "inputTokens": 0, "outputTokens": 0, "durationMs": 0 },
  "error": null
}
```

**Status values:** `Queued`, `Processing`, `Succeeded`, `Failed`. A failed job has `error: { code, message }` with a user-safe message. Raw provider errors are logged, never returned.

**Design notes for the README:**

- **Why `202` + polling and not SSE or WebSockets:** it's the simplest correct model and cache- and proxy-friendly. SSE is the upgrade path.
- **The answer key is included in the response.** The consumer is the manager authoring the quiz, not the learner. A learner-facing endpoint would strip `correctOptionIndex`. State this explicitly.
- **Idempotency is out of scope.** Mention `Idempotency-Key` as future work.

## 7. Generation pipeline (core of the evaluation)

### 7.1 Flow

1. The endpoint validates the request.
2. It persists `Generation { Status = Queued }`, then enqueues the id to `Channel<Guid>` and returns `202`.
3. `GenerationWorker` reads the channel and runs at most `MaxConcurrentJobs` jobs in parallel (default 2, through a `SemaphoreSlim`). For each job it creates a DI scope and calls `ContentGenerationService.ProcessAsync(id, ct)`.
4. `ProcessAsync`:
   1. Set `Processing`.
   2. Normalise the transcript: trim, collapse whitespace, strip common timestamp patterns such as `[00:12:34]` and `00:12:34.000 -->`.
   3. Compare the estimated tokens (`chars / 4` heuristic) with `SinglePassMaxTokens` (default 12k).
      - **Single pass:** one call returns `{summary, quiz}` JSON.
      - **Map-reduce:** first chunk the transcript (§7.2). The **map** step makes one call per chunk, in parallel with `MaxParallelChunkCalls` (default 4); each returns `{ chunkSummary, keyFacts[] }`. The **reduce** step makes one call that takes all chunk summaries and key facts and returns `{summary, quiz}` JSON.
   4. Parse the JSON and run `QuizValidator`. If it fails, make **one repair call** that sends the validation errors back to the model. If it fails again, set the job to `Failed` (`code: "invalid_model_output"`).
   5. Persist the result and metadata (token usage from `ChatResponse.Usage`, model, duration, chunk count, prompt version), then set `Succeeded`.
5. On an exception, set `Failed` with a mapped error code: `llm_timeout`, `llm_rate_limited`, `llm_unavailable`, `invalid_model_output` or `internal_error`.
6. **Startup recovery:** on start, the worker re-enqueues jobs left in `Queued` or `Processing`. This covers a restart while a job was running and keeps the DB state honest.

### 7.2 Chunking (`TranscriptChunker`)

- Target chunk size is about 3k tokens (12k characters), with about 10% overlap.
- Split preferably on paragraph boundaries, then on sentence boundaries (`. ! ?` followed by whitespace), then fall back to a hard cut.
- It is a pure function, so it's easy to unit-test exhaustively.
- Trade-off: a chars/4 estimate vs a real tokenizer (`Microsoft.ML.Tokenizers` with Tiktoken). Use the heuristic with a safety margin and mention the tokenizer as an upgrade.

### 7.3 Prompting (`Prompts.cs`, versioned `PromptVersion = "v1"`)

- **The system prompt** sets the role (an instructional designer writing for corporate training) and these rules:
  - use only facts in the transcript;
  - no outside knowledge;
  - neutral professional tone;
  - output must be valid JSON matching the schema.
- **Prompt-injection defence:** the transcript goes inside clear delimiters (`<transcript>…</transcript>`). The system prompt says the content is data, and that any instructions inside it must be ignored. Mention in the README that this is mitigation, not a guarantee.
- **Quiz rules in the prompt:**
  - exactly N questions;
  - 4 options each, exactly one correct;
  - plausible distractors;
  - no "all/none of the above";
  - spread across the transcript's topics;
  - each with a one-sentence explanation that cites the transcript;
  - vary the position of the correct answer.
- **Structured output:** use `ChatOptions.ResponseFormat = ChatResponseFormat.ForJsonSchema(...)`, with the schema generated from the C# record types through `AIJsonUtilities.CreateJsonSchema`. Provider support varies: OpenAI has strict JSON schema, Ollama has a JSON mode, the mock ignores it. Parsing and validation still run in every case, so correctness never depends on provider support.
- **Temperature:** low (0.2–0.3) for faithfulness.

### 7.3a Local Qwen specifics (primary real provider)

- **Context window:** Ollama's default `num_ctx` is small (a few thousand tokens), and it silently truncates input beyond that.
  - Set it explicitly: either with a Modelfile (`PARAMETER num_ctx 16384`) or with `OLLAMA_CONTEXT_LENGTH`.
  - Alternatively, keep `ChunkTokens` and `SinglePassMaxTokens` below the configured window.
  - Document this in the README. **This is the most likely cause of bad output.**
- **Thinking output:** Qwen3 models may emit `<think>…</think>` before the answer. `LlmJson` must strip it before parsing. Optionally turn it off with `/no_think` in the system prompt, or with the server's think flag.
- **Structured output:** Ollama ≥ 0.5 honours `response_format: json_schema` on `/v1/chat/completions`, and LM Studio supports it too. Parsing, validation and the repair call remain the safety net, because smaller Qwen models still drift.
- **Throughput:** local inference is slow and has little parallelism.
  - Use separate `appsettings.Local.json` defaults: `MaxParallelChunkCalls: 1`, `JobTimeoutSeconds: 900`, and a per-call timeout of about 300 s.
  - The UI should show elapsed time while processing.
- **Docker:** from inside the container, the host's Qwen is reached at `http://host.docker.internal:11434/v1` (Ollama) or `:1234/v1` (LM Studio). The compose file sets `Llm__Endpoint` accordingly.
- **Model name:** configurable (`Llm:Model`, for example `qwen3:8b` or `qwen2.5:14b-instruct`). Record the exact model and quantisation used in the README.
- **Status:** when checked on 2026-10-17, no server was listening on 11434 or 1234. Confirm which server hosts Qwen, and on which port, before the first real run (Q2).

### 7.4 Quiz validation invariants (`QuizValidator`)

- Question count equals the requested N. Alternative: accept between N−1 and N and log it. **Decision: require exactly N**, and let the repair call fix any mismatch.
- Each question has exactly 4 non-empty, distinct options (case-insensitive and trimmed).
- `0 <= correctOptionIndex < 4`.
- Question text is non-empty and unique across the quiz.
- The summary overview is non-empty, and there are at least 3 key points.
- The validator returns a list of human-readable errors, which feed the repair prompt.

### 7.5 Resilience

- Use `Microsoft.Extensions.Http.Resilience` (the standard handler) on the HttpClient behind the OpenAI client. It gives retry with backoff on 429/5xx, a timeout of about 60 s per attempt, and a circuit breaker.
  - Alternative: rely on the OpenAI SDK's built-in retries. **Decision: use the SDK's built-in retry policy** to avoid double retries, and document it. *Verify the SDK's defaults during implementation.*
- Set an overall per-job timeout (`JobTimeout`, default 5 min) through a linked `CancellationTokenSource`.
- Honour cancellation on app shutdown.

## 8. Data model

`Generation` table:

| Column | Notes |
|---|---|
| `Id` (Guid, PK) | |
| `Title` (string, ≤200, nullable) | |
| `Transcript` (TEXT) | |
| `TranscriptCharCount` | |
| `QuestionCount`, `SummaryLength` | |
| `Status` | Stored as a string |
| `CreatedAt`, `StartedAt`, `CompletedAt` | |
| `ErrorCode`, `ErrorMessage` | |
| `Summary` | Owned type, JSON column (`OwnsOne(...).ToJson()`) |
| `Quiz` | Owned type with a questions collection, JSON column |
| `Provider`, `Model`, `PromptVersion`, `ChunkCount`, `InputTokens`, `OutputTokens`, `DurationMs` | Metadata |

Index on `CreatedAt` for the list endpoint.

**Data notes:**

- SQLite file at `App_Data/learnsphere.db`, path set in config. In Docker it lives on a named volume.
- `EnsureCreated` at startup means **no migrations**. Any schema change means deleting the DB, which is acceptable for a POC and must be stated in the README.
- **Privacy:** transcripts may contain sensitive corporate content.
  - Note retention and deletion as future work.
  - **Do not log transcript text.** Log only its length and a hash.

## 9. Configuration

`appsettings.json`:

```json
"Llm": { "Provider": "Mock", "Model": "gpt-4o-mini", "Endpoint": null, "ApiKey": null, "Temperature": 0.2 },
"Generation": { "SinglePassMaxTokens": 12000, "ChunkTokens": 3000, "ChunkOverlapRatio": 0.1, "MaxParallelChunkCalls": 4,
                "MaxConcurrentJobs": 2, "JobTimeoutSeconds": 300, "MinTranscriptChars": 500, "MaxTranscriptChars": 300000,
                "MinQuestions": 3, "MaxQuestions": 20, "DefaultQuestions": 8 },
"ConnectionStrings": { "Default": "Data Source=App_Data/learnsphere.db" }
```

- **API keys** come only from environment variables (`Llm__ApiKey`) or `dotnet user-secrets`. They are never committed, and the README says so.
- **Validation:** use `ValidateOnStart` for the options. If the provider isn't `Mock` and there's no key, startup fails fast with a clear message.
- **Request size:** set Kestrel `MaxRequestBodySize` to about 1 MB, which matches the max transcript size plus headroom.

## 10. Frontend (Angular, `src/LearnSphere.Web`)

**Stack:**

- Current stable Angular CLI. Check its Node support range against the installed Node 24.11.
- Standalone components with signals for state.
- `HttpClient` with `provideHttpClient(withFetch())`.
- Reactive Forms with validators that mirror the server rules: min/max length and the `questionCount` range.
- Router with three routes: `/` (new), `/generations/:id` (detail), `/history`.
- Styling: plain SCSS. *No Angular Material* unless time allows, because it adds weight and decisions for no grading benefit.

**Polling:**

- `timer(0, 1500).pipe(switchMap(() => api.get(id)), takeWhile(r => !isTerminal(r.status), true))`. Exposed as a signal with `toSignal`.
- The subscription is torn down automatically when the user leaves the page.
- Backoff is optional.

**Errors:** an HTTP interceptor or helper maps `ProblemDetails` (`title`, `errors{field: [..]}`) to form-field errors and an error banner.

**Dev and prod wiring:**

- Dev: `ng serve --proxy-config proxy.conf.json` runs alongside `dotnet run`.
- Prod: `ng build` outputs to `src/LearnSphere.Api/wwwroot`, through `outputPath` in `angular.json` or by copying in the Dockerfile. The API adds `UseStaticFiles()` plus `MapFallbackToFile("index.html")` so that SPA deep links work.
- An MSBuild target to run `npm ci && ng build` on `dotnet publish` is optional. **Decision:** keep the Angular build in the Dockerfile and a documented script, not MSBuild, so `dotnet build` and `dotnet test` stay fast and don't need Node.

**Tests:**

- A few component and service tests with the CLI's default runner:
  - polling stops at a terminal status;
  - the quiz "check answers" logic;
  - mapping ProblemDetails to form errors.
- Keep the frontend tests light: backend quality is what's graded.

**Features (same as before):**

- **Form:** a title, a transcript textarea with a live character count and "≈ tokens", a question-count number input, a summary-length select, and a "Load sample" button that fetches `samples/…` served as static files.
- **Submit:** `fetch POST` the form. Poll `GET` every 1.5 s, with backoff up to 5 s. Show "Queued", then "Processing…" with a spinner. Stop polling on a final status.
- **Results:** the summary overview and key-point list, then the quiz as radio groups. A "Check answers" button reveals right and wrong answers with explanations. A "Show answer key" toggle is available for managers.
- **Errors:** show the `ProblemDetails` `title` and `errors` inline. A `Failed` job shows its friendly message.
- **Recent generations** list from `GET /generations`; clicking one loads it.
- **Escaping:** render model output through normal Angular interpolation (`{{ }}`), which escapes by default. **Never** bind model output to `[innerHTML]` or call `bypassSecurityTrust*`, because LLM output is untrusted (XSS).

## 11. AI-assistant conversation logs (deliverable)

The brief requires the **complete, unedited** logs. **This is done (2026-10-17).** `.claude/hooks/save-transcript.js` runs after every Claude Code turn and at the end of each session, and syncs every session:

| Path | Contents |
|---|---|
| `Context/Claude/raw/*.jsonl` | Verbatim Claude Code transcripts, plus any subagent folders |
| `Context/Claude/*.md` | Full renderings: every message, injected context, tool call input and tool result |
| `Context/Codex/raw/*.jsonl` | Verbatim Codex sessions whose cwd is this project, from `~/.codex/sessions` |
| `Context/Codex/full/*.md` | Full renderings of those Codex sessions |
| `Context/README.md` | Index of all of the above |

- Codex sessions are only picked up when a Claude turn ends, or by running `node .claude/hooks/save-transcript.js` manually. **Run it once before the final commit.**
- Remaining step: point the README at `Context/README.md`.
- **Before publishing,** scan the raw logs for secrets: API keys, tokens, and the `Llm__ApiKey` value. Also check that personal data is acceptable to publish (paths contain the Windows username; the email address doesn't appear in the logs but should be checked).
  - Redacting a secret technically "edits" the log, so state it in `AI-LOGS.md` if it ever happens. Better: never paste secrets into a session.
- **Include the planning artefacts** (`GlobalHelperX/`) in the repo as evidence of process, or reference them in `AI-LOGS.md`. See Q4.

## 12. Testing strategy

**`LearnSphere.Core.Tests` (unit):**

- `TranscriptChunker`:
  - empty or short input returns a single chunk;
  - long input respects the size bound and overlap;
  - prefers paragraph and sentence boundaries;
  - handles a giant input with no punctuation;
  - output is deterministic.
- `QuizValidator`: one test per invariant (wrong count, duplicate options, index out of range, empty text, duplicate questions), plus the valid case.
- `ContentGenerationService` with a scripted fake `IChatClient` that returns queued responses:
  - single-pass happy path;
  - map-reduce path makes N+1 calls, checked through a call counter;
  - invalid JSON, then a valid repair, ends `Succeeded`;
  - invalid output twice ends `Failed` with `invalid_model_output`;
  - an `HttpRequestException` or timeout maps to the right error code;
  - token usage is aggregated across calls.
- Transcript normalisation: timestamp stripping.

**`LearnSphere.Api.Tests` (integration):** `WebApplicationFactory<Program>` with the Mock provider and a SQLite in-memory connection kept open.

- `POST` a valid transcript returns `202`, a `Location` header and an id. Polling `GET` reaches `Succeeded` with a quiz of the requested size.
- `POST` with an empty, too-short or too-long transcript, or `questionCount` out of range, returns `400` ProblemDetails with field errors.
- `GET` of an unknown id returns `404`.
- The list endpoint returns the created item without the transcript body.
- `/health` returns `200`.

**Polling in tests:** use a helper that polls with a timeout of about 10 s. Alternatively, an "inline queue" test double processes jobs synchronously; that is more deterministic and preferred.

**Mock LLM determinism:** `MockChatClient` builds a valid response from the transcript itself. It takes the first sentences as key points and builds questions from sentences, with distractors drawn from other sentences. The demo then looks plausible and the tests stay stable.

## 13. Logging & observability

- Use `ILogger` with structured properties: `GenerationId`, `Provider`, `Model`, `ChunkCount`, `DurationMs`, `InputTokens`, `OutputTokens`, `Attempt`.
- Log each LLM call at `Information` level with its timing. Log full prompts and responses only at `Debug`, and only behind a config flag (`Llm:LogPayloads`, default false), because of the privacy concern.
- Optionally wrap the `IChatClient` with `UseLogging()` and `UseOpenTelemetry()` from `Microsoft.Extensions.AI`. That costs one line and shows operational awareness.
- Health check: `AddHealthChecks().AddDbContextCheck<AppDbContext>()`.

## 14. Docker

- **Multi-stage `Dockerfile`**, in three stages:
  1. `node:<lts>-alpine`: run `npm ci && npx ng build` for `src/LearnSphere.Web`.
  2. `mcr.microsoft.com/dotnet/sdk:10.0`: run `dotnet publish`, copying the Angular `dist/.../browser` output into `wwwroot`.
  3. `aspnet:10.0`: the runtime, as a non-root user (`USER $APP_UID`), exposing 8080.
- **`docker-compose.yml`:**
  - one `api` service on port `8080:8080`;
  - a volume for `App_Data`;
  - env `Llm__Provider=${LLM_PROVIDER:-Mock}` and `Llm__ApiKey=${OPENAI_API_KEY:-}`.
- An optional `ollama` service profile for running a local model.

## 15. README outline (graded as heavily as the code)

1. What it does (with a screenshot or GIF).
2. Quick start, as three paths:
   - `docker compose up`, with the Mock provider and no key;
   - `dotnet run`;
   - using a real provider: env vars, or `user-secrets`.
3. API reference with a short table, a link to `/scalar`, and sample `curl` calls.
4. Architecture: a diagram of layers and job flow, and the generation pipeline (single pass vs map-reduce).
5. Design decisions and trade-offs: D1–D9 plus the prompt and validation strategy.
6. Quality and safety of AI output: grounding, the validator and repair loop, prompt-injection mitigation, escaping output.
7. Operational thinking: timeouts, retries, startup recovery, logging, token-cost metadata, privacy.
8. Testing: how to run the tests, and what is covered.
9. Limitations and future work:
   - auth / multi-tenant;
   - real queue (Azure Service Bus);
   - SSE;
   - real tokenizer;
   - eval harness for output quality;
   - human-in-the-loop editing and approval;
   - export to LMS formats (SCORM / QTI);
   - video → transcript ingestion with Whisper;
   - rate limiting with `AddRateLimiter`;
   - data retention.
10. AI assistants used: which tools (Claude Code, Codex), and for what (planning, scaffolding, tests, README review). Link to `AI-LOGS.md`.

## 16. Assumptions

- A1: Input is plain text pasted into the UI. File upload (`.txt`/`.vtt`/`.srt`) is a stretch goal.
- A2: English-only output is acceptable. Alternatively, the prompt says "respond in the transcript's language", which costs one line; **include it.**
- A3: A single user with no authentication is acceptable for a POC; this is documented as a limitation.
- A4: The reviewer may not have an LLM key, so the Mock is the default and must give a coherent result.
- A5: 4 options per question (a common standard).

## 17. Open questions

- ~~Q1~~ **Resolved:** target `net10.0`.
- **Q2 (partly resolved):** the real provider is **local Qwen** through the `OpenAICompatible` provider (§7.3a). Still open:
  - which server hosts it (Ollama or LM Studio), on which port;
  - the exact model tag;
  - its configured context length.
- ~~Q3~~ **Resolved:** the user handles `Task/` personally, and everything else is committed. GitHub repo creation happens at the end.
- ~~Q4~~ **Resolved:** commit everything: planning artefacts, `AGENTS.md`, `.claude/`, `Context/`.
- **Q5:** Should the summary support length options (short/medium/long), or should it be fixed to keep scope down? *The plan includes it; it's cheap.*

## 18. Edge cases & failure paths

| Case | Handling |
|---|---|
| Empty, whitespace-only, or very short transcript | `400` (`MinTranscriptChars`) |
| Transcript over the maximum size | `400` from validation, or `413` from Kestrel for an oversized body |
| `questionCount` outside 3–20 | `400` |
| Transcript too short to support N questions | Prompt allows the model to say so? → *Decision:* the validator requires N questions. The UI caps N based on length: fewer than 2k chars means at most 5. |
| Model returns markdown-fenced JSON (```` ```json ````) | `LlmJson` strips the fences before parsing |
| Model returns extra or unknown fields | Ignored (lenient deserialisation); a missing required field fails validation |
| Model returns invalid JSON or breaks an invariant | One repair attempt, then `Failed` / `invalid_model_output` |
| 429 or 5xx from the provider | SDK retry with backoff, then `Failed` / `llm_rate_limited` or `llm_unavailable` |
| Provider timeout or job timeout exceeded | `Failed` / `llm_timeout` |
| App restarts mid-job | Startup recovery re-queues it |
| Unknown id in `GET` | `404` ProblemDetails |
| Non-JSON or malformed request body | `400` ProblemDetails (framework default) |
| Prompt injection inside the transcript | Delimiters plus system instruction; output is still validated and escaped |
| XSS in model output | `textContent` rendering in the UI |
| Non-English transcript | Respond in the transcript's language (A2) |
| Real provider configured but no key | Startup fails fast with a clear error |

## 19. Verification (after implementation)

```bash
dotnet build LearnSphere.sln -warnaserror
dotnet test LearnSphere.sln
dotnet run --project src/LearnSphere.Api        # API on http://localhost:5xxx
cd src/LearnSphere.Web && npm ci && npx ng test --watch=false && npx ng build   # frontend tests + prod build
npx ng serve --proxy-config proxy.conf.json     # open http://localhost:4200, run the sample end-to-end
curl -s -X POST localhost:5xxx/api/v1/generations -H "Content-Type: application/json" \
     -d '{"title":"t","transcript":"<600+ chars>","options":{"questionCount":5}}' -i   # expect 202 + Location
curl -s localhost:5xxx/api/v1/generations/<id>  # expect Succeeded, 5 questions
curl -s -X POST localhost:5xxx/api/v1/generations -H "Content-Type: application/json" -d '{"transcript":""}' -i  # expect 400
docker compose up --build                       # then repeat the UI flow on :8080
```

- Run once against a real provider with `samples/transcript-long.txt` so the map-reduce path is exercised. Record the duration and token metadata in the README.
- Check that `/scalar` renders and that `/health` returns 200.
- Do a fresh-clone test: clone into a temp dir and follow the README verbatim.
- Run a secret scan of the repo and the logs before pushing, for example `git grep -iE "sk-|api[_-]?key"`.

## 20. Risks & trade-offs

- **Time budget (about 8 h)** is the main risk. Suggested allocation:

  | Hours | Work |
  |---|---|
  | 0.5 | Scaffold |
  | 2.5 | Core pipeline and validator |
  | 1 | API and persistence |
  | 2 | Angular UI: scaffold, 3 routes, polling, quiz |
  | 1.5 | Tests |
  | 0.5 | Docker |
  | 1 | README and logs |

  That totals about 9 h with Angular, against the ~8 h guide. Cut in this order:
  1. the history route and list endpoint;
  2. the retry endpoint;
  3. length options;
  4. Angular unit tests beyond the polling and quiz logic;
  5. Ollama/Azure wiring;
  6. OpenTelemetry.
- **Map-reduce loses some detail** compared with a single giant-context call. Modern models with 128k context could take a whole transcript in one call. Explain why chunking is still chosen: cost, latency, parallelism, and lower quality on very long inputs ("lost in the middle"). Keep `SinglePassMaxTokens` configurable.
- **The in-process queue loses queued jobs if the process dies before they are persisted.** This is mitigated because jobs are persisted *before* they are enqueued, and the startup recovery re-queues them. It still isn't horizontally scalable; documented.
- **`Microsoft.Extensions.AI` API churn:** confirm the current stable package API (`GetResponseAsync`, `ChatResponseFormat`, `AIJsonUtilities`) at implementation time.
- **The "unedited logs" requirement versus secrets:** never type keys into an assistant session.

## 21. Deviations to explore before step two

These are alternatives to the decisions above. Settle them during the AN/OP merge.

| # | Deviation | Current plan | Why consider it | Cost / risk |
|---|---|---|---|---|
| X1 | **SSE streaming** of job progress (`GET /generations/{id}/events`), consumed by an Angular `EventSource` wrapper | `202` + polling | Live "chunk 3/5 summarised" progress is a strong demo and a talking point. It works well with RxJS. | About 1 h more. Polling is still needed as a fallback. |
| X2 | **Separate summary and quiz pipelines** (two reduce calls, quiz generated *from* the summary and key facts) | One reduce call returns both | Smaller, more focused prompts. You can regenerate only the quiz. Easier to evaluate each part. | One more LLM call. Some risk that quiz questions only cover what made it into the summary. |
| X3 | **Single long-context call** (no map-reduce) | Map-reduce above 12k tokens | Simpler code. Modern models take 128k+ tokens. | Weaker "long-input handling" story. Cost and latency go up. Lost-in-the-middle risk. Keep as a config switch either way. |
| X4 | **Generate the Angular API client from OpenAPI** (`openapi-typescript` or NSwag) | Hand-written DTO interfaces | Shows a contract-first workflow. No drift between C# and TypeScript. | Codegen tooling setup, about 30 min. |
| X5 | **Separate frontend container** (nginx serving Angular, reverse-proxying `/api`) | Angular served from the API's `wwwroot` | More production-like separation of concerns. | Two images and more compose config. Needs CORS or nginx proxy config. |
| X6 | **Synchronous `POST`** returning the result directly | Async jobs | Much less code: no queue, worker or recovery. | Timeouts on long transcripts. Weaker operational story. *Not recommended.* |
| X7 | **Editable output / regenerate a single question** (`PATCH` quiz, `POST …/questions/{n}/regenerate`) | Read-only result | Matches the real L&D workflow (human in the loop). A good product-thinking signal. | About 1–1.5 h. Scope creep against an 8 h budget. |
| X8 | **Grounding check:** each question returns a `sourceQuote`, and the server verifies it appears (fuzzily) in the transcript | Prompt-only grounding | Concrete hallucination mitigation. A strong answer to "how do you trust the output?" | About 45 min. Fuzzy matching needs tuning. |
| X9 | **Anthropic Claude as the real provider**, via an `IChatClient` adapter | OpenAI-first | Claude Code is already in use, and an existing account may exist (see Q2). | Need to confirm the .NET SDK's `IChatClient` support. Structured output works differently: tool-use or JSON prompting. |
| X10 | **Upload transcript files** (`.txt`/`.vtt`/`.srt`) with server-side parsing | Paste text only | Realistic input format from video platforms. | About 45 min. Multipart endpoint plus parser tests. |
| X11 | **Clean Architecture / CQRS (MediatR)** | 3 projects, plain services | Some reviewers expect it. | Ceremony without benefit at this size. *Not recommended.* Plain services are easier to defend. |

**Recommended to adopt:**

- **X8** (grounding check) for best value per hour.
- **X1** (SSE) if time remains after the core.
- **X2** is worth adopting if the reduce prompt proves unreliable in testing.

Treat X4 and X7 as stretch goals. Avoid X6 and X11.

## Implementation Checklist

- [x] Q1 (net10.0), Q3/Q4 (commit everything) resolved
- [ ] Q2: confirm the Qwen server, port, model tag and context length
- [x] Full conversation archiving (raw + full Markdown, Claude + Codex)
- [ ] `git init`, `.gitignore` (VS/.NET + `App_Data/`, `*.db`), `.editorconfig`, `Directory.Build.props`
- [ ] Scaffold the solution: Api, Core, Infrastructure, Core.Tests, Api.Tests
- [ ] Core: domain records, `GenerationOptions`, `TranscriptChunker`, `Prompts` v1, `LlmJson`, `QuizValidator`
- [ ] Core: `ContentGenerationService` (single-pass, map-reduce, repair loop, error mapping, metadata)
- [ ] Infrastructure: `AppDbContext` + JSON owned types, repository, `EnsureCreated`
- [ ] Infrastructure: `ChannelGenerationQueue`, `GenerationWorker` with concurrency limit and startup recovery
- [ ] Infrastructure: `IChatClient` provider switch (Mock / OpenAI / AzureOpenAI / Ollama) and a deterministic `MockChatClient`
- [ ] Api: Minimal API endpoints, request validation, ProblemDetails, request size limit, OpenAPI + Scalar, health checks
- [ ] Api: options validation on start (fail fast with no key for real providers); secrets only from env or user-secrets
- [ ] Angular: scaffold `src/LearnSphere.Web`, dev proxy, typed API service, routes (new / detail / history)
- [ ] Angular: form with validators, sample loader, RxJS polling, summary and interactive quiz, ProblemDetails display, no `[innerHTML]`
- [ ] API: `UseStaticFiles` + `MapFallbackToFile("index.html")`; Angular build output to `wwwroot` (git-ignored)
- [ ] Unit tests (chunker, validator, orchestrator with fake client) and integration tests (WebApplicationFactory)
- [ ] Dockerfile (non-root, multi-stage) + docker-compose (Mock default, key via env, volume)
- [ ] Real-provider run against local Qwen with the long sample; capture timing, tokens, model and quantisation
- [ ] Run `node .claude/hooks/save-transcript.js` before the final commit; secret-scan the logs; link `Context/README.md` from the README
- [ ] README per §15; fresh-clone verification; push the public GitHub repo
