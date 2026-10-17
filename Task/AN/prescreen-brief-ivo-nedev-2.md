# prescreen-brief-ivo-nedev — AI Training Content Generator: combined plan (step 2)

## Sources compared

| Source | Author | Notes |
|---|---|---|
| `Task/AN/prescreen-brief-ivo-nedev.md` | Claude Code | Detailed. Async jobs, SQLite, map-reduce chunking, 3 projects, Docker, deviations X1–X11. The Qwen section is now superseded. |
| `Task/OP/prescreen-brief-ivo-nedev.md` | Codex | Leaner. Synchronous, no storage, 40k-character cap without chunking, one project, strict privacy and logging, no automatic retries, Gemini as provider. |
| `Task/prescreen-brief-ivo-nedev.md` | Brief | Source of truth for the deliverables. |

The skill's default `GlobalHelperX/` paths don't exist here. The planning files live under `Task/`, and this output path was set by the user.

**User decisions that override both plans (2026-10-17):**

- .NET 10;
- an Angular frontend;
- **Gemini Developer API** as the live provider; Qwen and local models are dropped because the reviewer can't reach them;
- a mock for reviewers who have no key;
- commit everything (the user handles `Task/` personally);
- GitHub at the end;
- complete, unedited AI logs. This is already implemented in `Context/`.

## Executive summary

**The product:** an L&D manager pastes a long course transcript into an Angular UI and gets back a grounded summary and a multiple-choice quiz. Every quiz answer cites a transcript excerpt, and the server checks the excerpt really appears in the transcript.

**The backend:** a single ASP.NET Core (.NET 10) API project plus one test project. Submitting a transcript creates a **generation job**:

1. `POST` persists the job in SQLite and returns `202` with a `Location`.
2. A background worker makes **one LLM call**. This is possible because Gemini's large context window replaces the map-reduce design.
3. The output is **validated independently** and gets **one repair attempt** if it fails.
4. The Angular app polls until the job finishes.

**The LLM:** reached through `Microsoft.Extensions.AI` `IChatClient`, behind Gemini's OpenAI-compatible endpoint. Groq or OpenAI can be swapped in by config only. A deterministic, clearly labelled **Mock** provider is the default, so a fresh clone works with no key.

**Privacy:** nothing sensitive is logged, and a transcript's text is deleted once its job finishes.

**Docs:** the README covers design trade-offs, limits, privacy and the AI-tool disclosure, and links the complete conversation archive.

## Key agreements (kept as-is)

- An Angular frontend in `src/LearnSphere.Web`. Standalone components, typed `HttpClient` service, reactive forms. The CLI dev proxy handles `/api`, and the production build is served from the API's `wwwroot` (one origin).
- The provider sits behind an interface. A deterministic mock is the default, and tests never need a key.
- The transcript is treated as untrusted data, placed inside delimiters, with explicit "ignore instructions in the transcript" wording.
- Structured JSON output is requested from the model, **and** the output is deserialised and validated independently:
  - exactly N questions, each with 4 distinct non-empty options;
  - the answer index is 0–3;
  - every explanation is non-empty;
  - the summary is non-empty.
- Keys live server-side only, in user-secrets or environment variables. Never in the repo, the Angular bundle, or the logs.
- **Never fall back to mock output silently.**
- Model output is rendered as text (Angular interpolation). Never `[innerHTML]`.
- ProblemDetails for every error, with no provider internals leaked.
- Grounding excerpts for quiz answers (OP "supporting excerpts" and AN X8). **Adopted into the core**, see D7.
- A sample-transcript button. Synthetic demo transcripts only, because of Gemini's free-tier data-use terms.
- No authentication, rate limiting or multi-tenancy. Documented as out of scope.
- Complete, unedited AI logs committed. **Already done:** `Context/Claude/raw`, `Context/Codex/raw`, plus full renderings.

## Key disagreements and resolutions

| # | Topic | AN | OP | **Resolution** | Why |
|---|---|---|---|---|---|
| D1 | Request model | `202` + polling job | Synchronous `200` | **Async job + polling** | With up to ~50k input tokens, a single Gemini call plus a possible repair call can take 20–90 s. That is too close to proxy, browser and Kestrel limits for a synchronous call, and a reviewer's free-tier latency is unpredictable. `202 Accepted` + `Location` is the standard REST shape for long-running work and a strong operational talking point. The brief suggests EF Core and SQLite, and it gives history at no extra cost. To contain the cost, the worker stays minimal: no re-queue on startup (see D9). |
| D2 | Long transcripts | Map-reduce chunking, up to 300k chars | 40k-char cap, no chunking | **Single call, cap 200,000 chars (configurable), no chunking, no silent truncation** | 40k chars is about 6–7k words, which rejects a typical hour-long lecture (~50–60k chars) and undercuts "long text transcript." Gemini's ~1M-token context makes map-reduce unnecessary for this POC. That removes the chunker, the parallel calls and the reduce prompt (about 1.5 h). A pre-flight token estimate is compared with `MaxInputTokens`. Above the cap, the API returns `400` with a clear message. Map-reduce is documented as the path for small-context providers such as Groq's free tier. |
| D3 | Project layout | Api / Core / Infrastructure + 2 test projects | 1 API project + 1 test project | **1 API project with strict folder boundaries + 1 test project** | Fits the 8 h budget and Angular adds its own project. Testability comes from interfaces and DI, not assembly count. The README explains when the split into separate projects would be worth it. |
| D4 | Retries | Repair retry + SDK transport retry | No automatic retries | **One repair attempt for invalid output; no automatic transport retries** (OpenAI SDK `MaxRetries = 0`) | A repair attempt fixes the model drift we expect, at a bounded cost of 1 extra call. On a free tier, retrying a 429 just burns quota and adds latency. Failing fast with `provider_rate_limited` plus a **Retry** button in the UI is more honest. |
| D5 | Customisation | `questionCount` 3–20, summary length | Fixed 5 questions | **Optional `questionCount` 3–10, default 5; no summary-length option** | Question count is a single validated integer that exercises the validator meaningfully. Summary length adds prompt variants for little gain. |
| D6 | Payload logging | Debug-only flag | Never log transcript or output | **Never** (OP) | Simpler and safer. Log ids, lengths, the transcript's SHA-256 hash, timings, token usage, error categories. |
| D7 | Grounding check | Stretch (X8) | Stretch | **Core-lite:** each question carries a `sourceExcerpt` (≤ 40 words). The server checks it is a normalised substring of the transcript and sets `excerptVerified`. A mismatch **flags** the question; it doesn't fail the job. | It is the best answer to "how do you trust the output?" for about 45 min of work. Flagging rather than failing avoids repair loops over punctuation differences. The UI shows "✓ found in transcript" or "⚠ excerpt not found". |
| D8 | Storage and privacy | Stores transcript permanently | No storage | **SQLite. Transcript text is nulled when the job reaches a terminal state. Only the hash, length, title and results are kept. Adds `DELETE /generations/{id}`** | The async model needs the transcript until the job is processed. Deleting it afterwards keeps OP's privacy stance. |
| D9 | Restart handling | Re-queue `Queued`/`Processing` on startup | n/a | **On startup, mark stale `Queued`/`Processing` jobs as `Failed` (`interrupted`)** | Correct and honest, and simpler than re-queueing. The user can resubmit. |
| D10 | Docker | Core deliverable | Optional | **Required deliverable (user decision, 2026-10-17).** Multi-stage Node → SDK → runtime image, plus `docker-compose.yml`. **Not on the cut list.** | Lets a reviewer run the whole app with one command, without installing .NET 10 or Node. See "Docker" below. |
| D11 | API path | `/api/v1/generations` resource | `/api/training-content` | **`/api/v1/generations`** | Resource-oriented, versioned, and fits the job model. |
| D12 | Error surface | Error codes on the job | HTTP 502/503/504 | **Job `error.code`** (`provider_invalid_output`, `provider_rate_limited`, `provider_unavailable`, `provider_timeout`, `provider_truncated`, `interrupted`, `internal_error`). HTTP status codes only for request-level problems: `400`, `404`, `413`. | In an async model, provider failures happen after the request returns. OP's error taxonomy is kept as codes. |
| D13 | Mock labelling | Implicit | `generationMode: live/mock` | **Add `generationMode` (OP)** and show a "MOCK OUTPUT" banner in the UI | So no one mistakes mock output for model quality. |
| D14 | Gemini adapter | n/a | Typed HTTP client against the official contract | **`IChatClient` via the OpenAI SDK pointed at Gemini's OpenAI-compatible base URL.** Fallback: Google's native .NET SDK or REST if `json_schema` response format isn't honoured. | One abstraction, trivial to fake in tests, and switching to Groq or OpenAI is a config change. **Verify on day one** (step 3). |

**Dropped from AN:**

- map-reduce and the chunker;
- the retry endpoint;
- summary-length options;
- Azure, Ollama and Qwen wiring;
- the `LogPayloads` flag;
- the 3-project layout;
- SSE (kept as future work).

**Dropped from OP:**

- synchronous generation;
- the 40k cap;
- the "no storage" stance.

## Final implementation plan

### Structure

```text
LearnSphere.sln
Directory.Build.props            # net10.0, Nullable, ImplicitUsings, TreatWarningsAsErrors
.gitignore / .editorconfig       # ignore bin/obj, App_Data/, *.db, wwwroot build output, node_modules, dist
src/LearnSphere.Api/
  Program.cs                     # composition root only
  Endpoints/GenerationEndpoints.cs
  Contracts/                     # CreateGenerationRequest, GenerationResponse, GenerationSummaryItem (DTOs)
  Domain/                        # Generation, GenerationStatus, TrainingContent, QuizQuestion, GenerationError
  Generation/
    ITrainingContentGenerator.cs # GenerateAsync(transcript, questionCount, ct) -> GenerationResult
    TrainingContentGenerator.cs  # prompt -> IChatClient -> parse -> validate -> repair once -> verify excerpts
    Prompts.cs                   # PromptVersion = "v1", system + user + repair templates
    LlmJson.cs                   # strip ``` fences / leading prose, deserialize, finish-reason checks
    TrainingContentValidator.cs  # pure; returns list of human-readable errors
    ExcerptVerifier.cs           # normalise (case, whitespace, punctuation, quotes) + substring
    TranscriptNormalizer.cs      # trim, collapse whitespace, strip common timestamps
    TokenEstimator.cs            # chars/4 with margin (documented heuristic)
  Providers/
    LlmServiceCollectionExtensions.cs  # Provider: Mock | Gemini | OpenAICompatible
    MockChatClient.cs            # deterministic, builds valid JSON from the transcript's own sentences
  Jobs/
    IGenerationQueue.cs, ChannelGenerationQueue.cs
    GenerationWorker.cs          # BackgroundService, scoped processing, MaxConcurrentJobs, startup stale-job sweep
  Persistence/
    AppDbContext.cs              # SQLite, EnsureCreated, owned JSON column for content
    GenerationRepository.cs
  Configuration/ LlmOptions.cs, GenerationOptions.cs   # ValidateOnStart
  wwwroot/                       # Angular build output (generated, git-ignored)
src/LearnSphere.Web/             # Angular (current stable CLI; check Node 24.11 is in its supported range)
  proxy.conf.json                # /api -> API dev port
  src/app/core/generations-api.service.ts, models.ts, problem-details.ts
  src/app/features/generate/     # form + sample button + history list
  src/app/features/generation/   # polling, summary, quiz, answer-key toggle, excerpt badges, Retry
tests/LearnSphere.Api.Tests/
  Unit/ (validator, excerpt verifier, normaliser, LlmJson, generator with scripted fake IChatClient)
  Integration/ (WebApplicationFactory, Mock provider, in-memory SQLite connection, inline queue)
samples/ synthetic-short.txt, synthetic-long.txt, recorded-gemini-run.json
Dockerfile, docker-compose.yml, .dockerignore
README.md
Context/ (existing archive)  .claude/ (existing hook)  AGENTS.md
```

**Dependency rule:** `Domain` and `Generation` must not reference ASP.NET Core or EF Core. `Endpoints` and `Jobs` may reference anything. This is enforced by review and stated in the README.

### API

| Method | Route | Request | Responses |
|---|---|---|---|
| `POST` | `/api/v1/generations` | `{ "title"?: string≤200, "transcript": string, "questionCount"?: 3–10 }` | `202` + `Location` + `{ id, status }`; `400` ProblemDetails with field errors; `413` |
| `GET` | `/api/v1/generations/{id}` | n/a | `200` full resource; `404` |
| `GET` | `/api/v1/generations?limit=20` | n/a | `200` `[ { id, title, status, createdAt, generationMode } ]` |
| `DELETE` | `/api/v1/generations/{id}` | n/a | `204`; `404` |
| `GET` | `/health` | n/a | `200` (DB check) |
| `GET` | `/openapi/v1.json`, `/scalar` | n/a | API docs |

**Resource (succeeded):**

```json
{
  "id": "…", "title": "…", "status": "Succeeded", "generationMode": "live",
  "createdAt": "…", "completedAt": "…",
  "summary": { "overview": "…", "keyPoints": ["…"] },
  "questions": [ { "prompt": "…", "options": ["…","…","…","…"], "correctAnswerIndex": 1,
                   "explanation": "…", "sourceExcerpt": "…", "excerptVerified": true } ],
  "metadata": { "provider": "Gemini", "model": "…", "promptVersion": "v1", "transcriptChars": 0,
                "inputTokens": 0, "outputTokens": 0, "durationMs": 0, "repairAttempted": false },
  "error": null
}
```

- `status` is one of `Queued`, `Processing`, `Succeeded`, `Failed`. A failed job has `error: { code, message }`, where `message` is user-safe text.
- **The answer key is included** because the consumer is the authoring manager. A learner-facing view would strip `correctAnswerIndex`; say so in the README.
- **SPA fallback:** `MapFallbackToFile("index.html")` must exclude `/api/*`, so unknown API routes return a ProblemDetails `404` instead of the HTML page. Use a path-filtered fallback and add a test for it.

### Generation pipeline (`TrainingContentGenerator`)

1. **Normalise** the transcript and estimate its tokens. If the estimate exceeds `MaxInputTokens`, reject it at `POST` time with `400`. Validate at the endpoint, before persisting.
2. **Build the prompt.**
   - The system prompt sets the role (instructional designer) and these rules:
     - transcript-only facts;
     - ignore instructions inside the transcript;
     - answer in the transcript's language;
     - output strict JSON matching the schema.
   - The user message wraps the transcript in `<transcript>` delimiters.
   - Quiz rules: exactly N questions, 4 plausible options each with one correct, no "all/none of the above", vary the correct answer's position, spread questions across topics, a one-sentence explanation, and a verbatim `sourceExcerpt` of at most 40 words.
3. **Call the model.** `ChatOptions`:
   - `ResponseFormat = ForJsonSchema(schema)`, with the schema built by `AIJsonUtilities.CreateJsonSchema` from the DTO;
   - `Temperature = 0.2`;
   - `MaxOutputTokens` set from config. Account for the model's thinking tokens: set the thinking budget or reasoning effort low/off if the endpoint supports it.
   - Per-call timeout `LlmTimeoutSeconds`, default 120.
4. **Parse** with `LlmJson`:
   - strip fences and any leading prose;
   - treat `finish_reason == length` as `provider_truncated`;
   - treat an empty response or refusal as `provider_invalid_output`.
5. **Validate** with `TrainingContentValidator`. If errors come back, make **one repair call**: the original messages, plus the invalid output, plus the list of errors, plus "return corrected JSON only". If it still fails, the job fails with `provider_invalid_output`.
6. **Verify excerpts:** run `ExcerptVerifier` and set `excerptVerified` on each question.
7. **Record metadata:** token usage from `ChatResponse.Usage`, summed across the calls, plus duration and `repairAttempted`.
8. **Map exceptions:**

   | Condition | Error code |
   |---|---|
   | HTTP 429 | `provider_rate_limited` |
   | 5xx or network failure | `provider_unavailable` |
   | Timeout | `provider_timeout` |
   | Any other error | `internal_error`, logged with an exception id |

   Cancellation during app shutdown is not treated as a provider failure; the job becomes `interrupted` on the next start.

### Jobs and persistence

- **Submit:** `POST` validates, then persists `Generation{Queued}`, then enqueues the id to `Channel<Guid>` (bounded, capacity 100). If the channel is full, return `503` ProblemDetails "busy, try again".
- **Worker:** `GenerationWorker` processes jobs with a `SemaphoreSlim(MaxConcurrentJobs = 2)`, using one DI scope per job.
  - Status goes `Processing`, then `Succeeded` or `Failed`.
  - The transcript is nulled when the job reaches a terminal state.
- **Startup sweep:** stale `Queued`/`Processing` jobs become `Failed`/`interrupted`.
- **Entity:**

  | Field | Notes |
  |---|---|
  | `Id`, `Title`, `Status`, `GenerationMode` | |
  | `Transcript` (nullable TEXT) | Nulled when the job finishes |
  | `TranscriptChars`, `TranscriptSha256`, `QuestionCount` | |
  | `CreatedAt`, `StartedAt`, `CompletedAt` | Index on `CreatedAt` |
  | `ErrorCode`, `ErrorMessage` | |
  | `Content` | Owned type, JSON column (`OwnsOne(...).ToJson()`) |
  | Metadata fields | |

- **Database:** SQLite at `App_Data/learnsphere.db`, created with `EnsureCreated`. There are **no migrations**, so a schema change means deleting the DB file. This is a stated POC trade-off.

### Configuration

```json
"Llm": { "Provider": "Mock", "Model": "<gemini flash model id — verify in AI Studio>",
         "Endpoint": "https://generativelanguage.googleapis.com/v1beta/openai/", "ApiKey": null,
         "Temperature": 0.2, "MaxOutputTokens": 4096, "TimeoutSeconds": 120 },
"Generation": { "MinTranscriptChars": 500, "MaxTranscriptChars": 200000, "MaxInputTokens": 60000,
                "DefaultQuestionCount": 5, "MinQuestionCount": 3, "MaxQuestionCount": 10,
                "MaxConcurrentJobs": 2, "QueueCapacity": 100 },
"ConnectionStrings": { "Default": "Data Source=App_Data/learnsphere.db" }
```

- **Keys:** `dotnet user-secrets set Llm:ApiKey …`, or the `Llm__ApiKey` environment variable.
- **Validation:** `ValidateOnStart`. A provider other than `Mock` with no key fails fast at startup.
- **Request size:** Kestrel `MaxRequestBodySize` is 1 MB, which covers 200k characters of UTF-8 plus JSON-escaping overhead.
- **Groq as an alternative:** `Provider=OpenAICompatible` with `Endpoint=https://api.groq.com/openai/v1`. Documented with its low free tokens-per-minute limit, so short transcripts only.

### Frontend (Angular)

- **Generate page:**
  - title field and a transcript textarea with a live character count and token estimate against the limit;
  - question-count select, defaulting to 5;
  - "Load sample" button and Generate button;
  - the button is disabled while submitting, which prevents duplicate requests;
  - recent-generations list.
- **Generation page:**
  - polling with `timer(0, 1500)` + `switchMap` + `takeWhile(terminal, true)`, wrapped with `toSignal`, torn down when leaving the page;
  - elapsed timer and an `aria-live` status;
  - a "MOCK OUTPUT" banner when `generationMode = mock`;
  - summary overview and key points;
  - quiz as radio groups with **Check answers**, which reveals correctness, the explanation, and the excerpt with its ✓/⚠ badge;
  - a **Show answer key** toggle;
  - a failed job shows its friendly error message and a **Retry** button that resubmits the same input from client state;
  - a Delete button.
- **Errors:** a ProblemDetails helper maps `errors{field}` to form controls, and anything else to a banner.
- **Rendering:** interpolation only. No `[innerHTML]` and no `bypassSecurityTrust*`.
- **Accessibility and layout:** semantic controls, labels, and a readable mobile layout.
- **Build:** `ng build` outputs into the API's `wwwroot`, in the Dockerfile or with a documented script. **Not wired into MSBuild**, so `dotnet build` and `dotnet test` don't need Node.

### Docker (required)

- **`Dockerfile`**, multi-stage, at the repo root, with a `.dockerignore` covering `bin/`, `obj/`, `node_modules/`, `dist/`, `App_Data/`, `Context/`, `.git/`:
  1. `node:<lts>-alpine`: `npm ci`, then `npx ng build` in `src/LearnSphere.Web`. Copy `package*.json` first to make use of layer caching.
  2. `mcr.microsoft.com/dotnet/sdk:10.0`: `dotnet restore` (csproj files first, again for caching), then `dotnet publish -c Release -o /app`. Copy the Angular `dist/<app>/browser` output into `/app/wwwroot`.
  3. `mcr.microsoft.com/dotnet/aspnet:10.0`: copy `/app`.
     - `USER $APP_UID` (non-root);
     - `ENV ASPNETCORE_HTTP_PORTS=8080`, `EXPOSE 8080`;
     - `App_Data` writable by the app user;
     - `HEALTHCHECK` with `curl`/`wget` against `/health`. The aspnet image has neither, so use a tiny `--health` mode or the compose `healthcheck` with a .NET-free check. **Decision:** use the compose `healthcheck` on `/health`, with the tool chosen at implementation time.
- **`docker-compose.yml`:** one `api` service.
  - Build from the repo root, map ports `8080:8080`.
  - Named volume `learnsphere-data:/app/App_Data`.
  - Environment:
    - `Llm__Provider=${LLM_PROVIDER:-Mock}`
    - `Llm__ApiKey=${GEMINI_API_KEY:-}`
    - `Llm__Model=${LLM_MODEL:-}` (empty means use the appsettings value)
  - `restart: unless-stopped`.
- **`.env.example`** is committed and documents `LLM_PROVIDER`, `GEMINI_API_KEY` and `LLM_MODEL`. **`.env` is git-ignored.**
- **Running it:**
  - `docker compose up --build` runs the Mock provider, with no key needed.
  - Live mode: copy `.env.example` to `.env`, set `LLM_PROVIDER=Gemini` and `GEMINI_API_KEY`, then `docker compose up --build`.
- **Startup validation:** the same `ValidateOnStart` applies in the container. Live mode with no key fails at startup with a clear log line.
- **Verify:**
  - the image builds from a clean clone;
  - the container runs as non-root;
  - the DB persists across `docker compose down` and `up` (the volume);
  - deep links work (SPA fallback);
  - the image size is reasonable (aspnet base, no SDK or Node in the final stage).

### Logging and observability

- `ILogger` with structured fields: `GenerationId`, `Provider`, `Model`, `TranscriptChars`, `TranscriptSha256`, `DurationMs`, `InputTokens`, `OutputTokens`, `RepairAttempted`, `ErrorCode`.
- **Never log** transcripts, generated content, auth headers, or provider response bodies.
- Wrap the client with `UseLogging()` only if it doesn't emit message contents. Verify this, or skip it.
- Health check: `AddDbContextCheck`.

### Testing (xUnit; Shouldly or plain asserts, no FluentAssertions v8)

**Unit tests:**

- **Validator:** one test per rule, plus the valid case.
- **ExcerptVerifier:** exact match, whitespace and case differences, curly vs straight quotes, absent excerpt, empty excerpt.
- **TranscriptNormalizer:** strips timestamps.
- **LlmJson:** handles code fences, leading prose, invalid JSON, truncated output (`finish_reason = length`), unknown extra fields.
- **TrainingContentGenerator** with a scripted fake `IChatClient`:
  - happy path;
  - invalid output then a valid repair, ending `Succeeded` with `repairAttempted = true`;
  - invalid output twice, ending `provider_invalid_output`;
  - 429, 5xx, timeout and truncation map to the right codes;
  - token usage is summed across calls;
  - an unverifiable excerpt is flagged, not failed.

**Integration tests** (WebApplicationFactory, Mock provider, SQLite in-memory, synchronous inline queue double):

- `POST` returns `202` + `Location`; the follow-up `GET` returns `Succeeded` with 5 questions and `generationMode = mock`.
- `questionCount = 7` gives 7 questions.
- Empty, whitespace-only or too-short transcripts, an over-limit transcript, `questionCount` 2 or 11, and malformed JSON all return `400`.
- An oversized body returns `413`.
- An unknown id returns `404`.
- `DELETE` returns `204`, then `GET` returns `404`.
- The transcript is null in the DB after completion.
- An unknown `/api/v1/nope` returns a `404` ProblemDetails, not `index.html`.
- `/health` returns `200`.
- Startup sweep: jobs seeded as `Processing` become `interrupted`.
- A transcript containing `<script>` comes back intact as JSON text (rendering safety is checked on the Angular side).

**Angular tests:**

- polling stops at a terminal status;
- check-answers logic;
- ProblemDetails mapped to form errors;
- the button is disabled while submitting.

**Manual review:** a live Gemini run on `synthetic-long.txt`. Check summary faithfulness, how well each answer is grounded, distractor quality, and that an injected instruction line is ignored. Save the output to `samples/recorded-gemini-run.json`, and record the duration and tokens in the README.

## Edge cases and risks

| Case | Handling |
|---|---|
| Empty, whitespace, too short, missing `transcript` | `400` field error (`MinTranscriptChars`) |
| Over the character or token limit | `400` with the limit stated; never truncated silently |
| Body over 1 MB | `413` |
| `questionCount` out of range | `400` |
| Transcript too short for N questions | The UI suggests ≤ 5 below 2k characters. If the model can't comply, validation and repair run, then `provider_invalid_output` with a "transcript may be too short" hint |
| Prompt injection in the transcript | Delimiters + system rule, independent validation, excerpts verified, output rendered as text |
| Fenced or prose-wrapped JSON | `LlmJson` strips it |
| Output truncated by the token limit (including thinking tokens) | `provider_truncated`. Mitigate with `MaxOutputTokens` headroom and a low thinking budget |
| Free-tier 429 or daily quota exhausted | `provider_rate_limited` + Retry. The README explains that the Mock and the recorded sample remain available |
| Gemini model renamed or removed from the free tier | Model is configurable; the README says to check AI Studio |
| Gemini endpoint ignores `json_schema` | Validation and repair still protect correctness. Fall back to the native SDK (see D14) |
| Free-tier data used by Google | Synthetic transcripts only; the README privacy note says a real deployment needs a paid tier or Vertex |
| Channel full | `503` ProblemDetails |
| App restart mid-job | Job marked `interrupted`; the user resubmits |
| Duplicate clicks | Button disabled; no idempotency key (future work) |
| Non-English transcript | Output in the transcript's language |
| Unknown API route | ProblemDetails `404`, not the SPA |
| Secrets in AI logs | Never paste keys into an assistant session; scan before publishing |

**Overall risks:**

- **Time.** The estimate is about 8.5 h against the ~8 h guide. Cut in this order:
  1. `DELETE`;
  2. the history list;
  3. Angular tests beyond polling and quiz;
  4. `/scalar`;
  5. downgrade the excerpt verification to display-only.

  **Never cut** validation, repair, the Mock, the integration tests, Docker or the README.
- **API churn:** `Microsoft.Extensions.AI`, and Gemini's OpenAI-compatible support for `json_schema` and thinking controls. Verify both at the start of step 3.

## Time allocation

| Step | Work | Time |
|---|---|---|
| 1 | `git init`, solution, API and test projects, `Directory.Build.props`, `.gitignore`, options with `ValidateOnStart`, SQLite, health check | 0:40 |
| 2 | Domain and contracts, endpoints, request validation, ProblemDetails, channel queue, worker, startup sweep, `MockChatClient`, end-to-end through `curl` | 1:30 |
| 3 | Gemini adapter: **verify `json_schema` first**, prompts v1, `LlmJson`, validator, repair, excerpt verifier, error mapping, metadata | 1:45 |
| 4 | Angular: scaffold, proxy, API service, generate and generation pages, polling, quiz, banners, errors; build into `wwwroot`; SPA fallback | 1:45 |
| 5 | Tests: unit + integration + focused Angular tests | 1:15 |
| 6 | Live Gemini run, recorded sample, README | 1:00 |
| 7 | Docker: Dockerfile, compose, `.env.example`, volume, healthcheck; fresh-clone check through Docker and through `dotnet run` | 0:40 |
| | **Total** | **≈ 8:35** |

**Ordering note:** step 6's live Gemini run waits on the user registering for a Gemini key. Everything else, including the README draft and Docker, runs on the Mock first. The live run and the recorded sample slot in once the key exists.

## Verification checklist

- [ ] `dotnet build LearnSphere.sln -warnaserror` and `dotnet test` pass with no API key.
- [ ] `cd src/LearnSphere.Web && npm ci && npx ng test --watch=false && npx ng build` passes.
- [ ] `dotnet run --project src/LearnSphere.Api` serves the built Angular app at `/`. Deep link `/generations/<id>` loads; `/api/v1/nope` returns a ProblemDetails `404`.
- [ ] Dev mode: `ng serve --proxy-config proxy.conf.json` gives the full flow at `:4200`.
- [ ] `curl` `POST` returns `202` + `Location`; `GET` reaches `Succeeded`; an invalid body returns `400` with field errors.
- [ ] Mock mode shows the MOCK banner. Live mode (Gemini key through user-secrets) produces 5 grounded questions with verified excerpts on the synthetic sample.
- [ ] An instruction-injection line in the sample is ignored; HTML in the transcript or output shows as text.
- [ ] Forcing a timeout or 429 (fake client or a bad key) shows a friendly error with Retry; no provider details leak.
- [ ] After completion, the `Transcript` column is null; the logs contain no transcript text.
- [ ] `docker compose up --build` from a fresh clone gives the full flow on `:8080` in Mock mode. With `.env` set, it gives live Gemini. The container runs as non-root, and the data survives `down` and `up`.
- [ ] Fresh clone into a temp dir, then follow the README verbatim.
- [ ] `node .claude/hooks/save-transcript.js` is run before the final commit, and `Context/README.md` is linked from the README.
- [ ] Secret scan before pushing: `git grep -inE "AIza|sk-|api[_-]?key\s*[:=]"`, and repeat it over `Context/**/raw`.
- [ ] README sections:
  - setup for Mock, Gemini and Docker;
  - configuration and environment variables;
  - API with a sample request and response;
  - architecture and job flow;
  - decisions D1–D14 summarised. The **trade-offs section must include, as the user requested:** "No automatic retries on provider errors. On a free tier, retrying a rate-limit error just burns quota, so the app fails fast and the UI offers a Retry button.";
  - limits;
  - privacy, cost and free-tier notes;
  - testing;
  - future work: map-reduce, SSE, auth and rate limiting, idempotency, editable quizzes, LMS export, transcript ingestion from video;
  - AI tools used and how.

## Open questions

1. **Gemini key and model id:** *Deferred.* The user hasn't registered yet. Build and test everything against the Mock. When the key exists, choose a free Flash-class model on the AI Studio quota page and set it through config. The live run and the recorded sample follow.
2. **Gemini OpenAI-compatible `json_schema` support and thinking control:** still unverified, and it now needs the key. The adapter is written to the documented contract with the D14 fallback ready. It gets verified as soon as the key is available.
3. ~~Docker~~ **Resolved:** required. See D10 and the "Docker" section.
4. ~~Sample content~~ **Resolved:** a fictional "Data Privacy Basics" transcript of about 45k characters, with one embedded injection line.
5. ~~Planning location~~ **Resolved:** `Task/` (`Task/AN`, `Task/OP`), not `GlobalHelperX/`.
