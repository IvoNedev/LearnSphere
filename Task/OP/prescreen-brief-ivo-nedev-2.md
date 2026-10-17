# LearnSphere — combined implementation plan, revision 2

Planning only. No application implementation is authorized by this document.

## Sources and precedence

- Brief: `Task/prescreen-brief-ivo-nedev.md`.
- OP: `Task/OP/prescreen-brief-ivo-nedev.md`.
- AN: `Task/AN/prescreen-brief-ivo-nedev.md`.
- User decisions in this conversation: Angular frontend; a hosted provider with a free API tier; agreement with the proposed scope recommendations, with excerpts/download as stretch features.
- Repository evidence: `AGENTS.md`, `Context/README.md`, `Context/Codex/README.md`, `.claude/hooks/save-transcript.js`, and relevant archived messages in `Context/Claude/2026-10-17-92115279.md`.

The explicit output requested is `OP/prescreen-brief-ivo-nedev-2.md` at workspace root. This overrides the seccond-step skill's default GlobalHelperX/BOTH location. Source plans are preserved.

The archived Claude conversation contains a later user message saying, “Lets not use qwen cuz the reviewer cant see that, is there an AI out there that lets you get a free API key ?” This supersedes AN's local-Qwen recommendation. AN also records agreement on net10.0 and keeping project artifacts; preserve those decisions rather than reopening them unnecessarily. Its historical tooling and timing estimates are not acceptance criteria.

## Recommended approach

Deliver a focused C#/.NET 10 API and Angular application that takes a pasted transcript and produces a grounded summary and five multiple-choice questions. Default to an explicitly labeled deterministic mock so reviewers can run the workflow without credentials. Implement one hosted live adapter, initially Gemini Developer API, with a configurable model available to the user's free-tier project. Keep keys exclusively in backend configuration.

Start with a synchronous API and one structured generation call. Before finalizing transcript limits, run a representative long-input experiment against the chosen model and quota. Add chunking or jobs only if the experiment demonstrates they are necessary; do not silently reduce input, claim arbitrary transcript support, or hide a failed live run behind mock output.

Use one backend project with service/provider boundaries and one backend test project. Keep Angular in a separate source folder, use its dev proxy locally, and serve its compiled assets from the API for the packaged build. No database, history page, authentication, or multi-provider framework is needed for the baseline request/response workflow.

## Agreements between the plans

- The brief prioritizes backend design, API clarity, AI interaction, testing, and README quality over visual polish.
- Angular uses standalone components, HttpClient, local state, and reactive forms; generated content is rendered through ordinary template interpolation.
- The provider is replaceable through dependency injection; mock and live paths are visibly distinguishable.
- Prompts separate trusted instructions from untrusted transcripts, request grounded content, and specify structured output. Server-side validation remains mandatory.
- Errors are user-safe and structured. Keys and transcript contents stay out of frontend bundles and application logs.
- Synthetic short/long sample transcripts, focused automated checks, and a real-provider smoke run provide useful evidence.
- Publishing must include the README and complete available AI logs, with no invented claims of completeness.

## Disagreements and resolutions

| Topic | OP | AN | Resolution and reason |
|---|---|---|---|
| Runtime | Resolve .NET 8 availability first | .NET 10 | Target net10.0, as recorded in AN. The brief's .NET 8 stack is suggested. Explain the change and pin a tested SDK. |
| Live provider | Hosted Gemini free tier | Local Qwen, plus several adapters | Use Gemini first, retaining mock. Local Qwen is explicitly superseded by later user feedback. Groq remains an alternative if account-level Gemini access fails, not a second required adapter. |
| API | Synchronous POST returning output | 202 jobs and polling | Begin synchronous. Add a job API only if representative generation cannot fit the documented end-to-end timeout; async is not intrinsically required by the brief. |
| Long input | Proposed 40k-character cap | 300k cap and map-reduce above 12k estimated tokens | Neither number is established by the brief. Keep a configurable provisional cap, then publish an empirically verified supported range and token budget. Do not assume hour-long content cannot fit one provider context. |
| Chunking | Deferred | Default for longer input | Conditional. Multiple calls can consume more quota and introduce synthesis loss. Adopt only when the selected provider's context/request limits or measured quality justify it. |
| Persistence | None | SQLite, queue recovery, history | None for baseline. SQLite is a suggested tool, not a deliverable. Introduce persistence only with an explicitly selected durable-job/history requirement. |
| Backend layers | One project | Api/Core/Infrastructure | One project with logical folders and clear interfaces. Unit and API integration tests can live in one test project. Extract libraries when a real dependency boundary requires it. |
| AI abstraction | Small provider interface | Microsoft.Extensions.AI IChatClient | Small application-owned interface and typed HttpClient for one provider. Do not add several SDKs/adapters to demonstrate hypothetical flexibility. |
| Repair and retries | Fail visibly; explicit user retry | Repair plus SDK retries | No hidden generation retries initially. One schema-repair attempt is optional only if invalid-output samples justify it, within the same deadline and call budget. Avoid stacked retry policies. |
| Quiz settings | Five questions, four options | 3–20 configurable questions and summary lengths | Fix five/four initially. Input length alone cannot establish how many meaningful questions the source supports. Report insufficient content rather than inventing answers. |
| Grounding excerpts | Stretch, exact source match | Recommended fuzzy matching | Stretch: exact matching against a defined normalized source. Do not use fuzzy matching initially; matching is evidence of provenance, not proof of correctness. |
| Logs | Full exports still missing | Complete archive done | Raw and rendered archives now exist. Verify coverage and refresh before submission; file presence does not prove every assistant session or final turn is captured. |
| Packaging | Docker optional | Docker/Compose required | Document and verify local and packaged Angular/API builds first. Add one multistage Dockerfile if time remains; Compose and volumes are unnecessary without extra services/storage. |

## Verified repository state

- No application source, solution, or tests currently exist. No application pattern can be inferred from planning files.
- `git status` still reports that the workspace is not a Git repository. Do not create a remote or publish as part of this planning task.
- SDKs 10.0.201 and 10.0.401 were previously observed. Current runtime inspection shows both ASP.NET Core/.NET 8.0.31 and 10.0 runtimes. The earlier “.NET 8 availability gap” was overstated: the runtime exists, while no .NET 8 SDK was observed. Use the chosen .NET 10 baseline regardless.
- Raw Codex and Claude JSONL archives and full Markdown renderings are present. The existing hook copies and renders available local session files. Do not modify raw files or execute the archiver without considering its wider archive updates; refresh it as part of final delivery verification.

## Contract and boundaries

### API baseline

`POST /api/v1/training-content`

Request: `{ "transcript": "..." }`.

Success: `200 OK` with:

```json
{
  "summary": "...",
  "questions": [
    {
      "prompt": "...",
      "options": ["...", "...", "...", "..."],
      "correctAnswerIndex": 0,
      "explanation": "..."
    }
  ],
  "generationMode": "live",
  "metadata": {
    "provider": "Gemini",
    "model": "configured-model",
    "promptVersion": "v1",
    "durationMs": 1234,
    "inputTokens": null,
    "outputTokens": null
  }
}
```

The example shows one question for brevity; valid output contains exactly five. Missing provider usage is null, not fabricated zero. The answer key is appropriate for the manager authoring/reviewing the quiz; it is not a secure learner assessment endpoint.

Errors use Problem Details with stable application error codes and a trace ID:

- `400`: malformed JSON, missing/blank transcript, configured text-length violation, or content too limited to generate a meaningful quiz.
- `413`: request body exceeds the transport limit.
- `502`: malformed/incomplete provider output or other upstream generation failure.
- `503`: provider throttled/unavailable; include a safe retry hint when available.
- `504`: configured generation deadline exceeded.

Do not report a client-disconnected request as a provider timeout. Handle wrong content type consistently, and ensure malformed requests receive the documented error format rather than assuming framework defaults match it.

Provisional defaults: five questions, four options each, roughly 150–250 words for the summary where the source supports it, 40,000 transcript characters, and a 90-second generation deadline. These are design starting points requiring the experiment below. Set a separate transport ceiling that accounts for JSON escaping and UTF-8 expansion; keep UI, endpoint, host, and packaged configuration consistent.

### Proposed files

```text
LearnSphere.sln
global.json
src/LearnSphere.Api/
  Program.cs
  Contracts/
  Endpoints/TrainingContentEndpoints.cs
  Generation/TrainingContentService.cs
  Generation/ITrainingContentGenerator.cs
  Generation/TrainingContentValidator.cs
  Generation/Prompts.cs
  Providers/GeminiTrainingContentGenerator.cs
  Providers/MockTrainingContentGenerator.cs
  Configuration/GenerationOptions.cs
  wwwroot/                         # packaged Angular output
src/LearnSphere.Web/
  angular.json
  package.json
  package-lock.json
  proxy.conf.json
  src/app/core/api/
  src/app/features/generator/
  src/app/features/quiz/
tests/LearnSphere.Api.Tests/
samples/transcript-short.txt
samples/transcript-long.txt
README.md
AI-LOGS.md
Context/
```

Provider adapters handle HTTP/authentication and map responses/errors. The service owns orchestration, validation, and deadline/cancellation. Frontend DTO interfaces match API contracts; use a contract fixture/integration check to catch drift. OpenAPI generation is useful; automatic Angular client generation is deferred.

## Implementation plan and checkpoints

1. **Confirm provider and long-input behavior — 45 minutes.** Select a free-tier-supported model in the actual account, test a synthetic representative long transcript, and record input size, provider token count, total latency, finish reason, schema validity, and basic factual quality. Inspect selected model context/output and free-tier quotas; do not infer them from a different model. Test one-call generation first. Define the supported transcript range from this evidence. If credentials are unavailable, mark live verification pending and continue the mock path without claiming success.
2. **Scaffold — 45 minutes.** Initialize Git, create the API/test solution and Angular workspace, pin tested SDK/Node/Angular versions, and add ignores for credentials, build output, and local artifacts. Configure DI/options, default mock mode, static assets, development proxy, and consistent Problem Details. Nonmock configuration without a key fails clearly at startup. Use a sample nonsecret configuration file.
3. **Backend workflow — 90 minutes.** Implement DTOs, input limits, cancellation/deadline propagation, provider abstraction, deterministic mock, validator, and synchronous endpoint. Add basic OpenAPI documentation. Mock responses are fixed synthetic examples and visibly labeled; never pretend they are grounded analysis of arbitrary input. No database or migration is needed.
4. **Live integration — 75 minutes.** Implement the Gemini REST adapter against official documentation, with schema-constrained output where supported. Keep full instructions separate from transcript data. Handle refusal, token-limit truncation, null content, invalid JSON, timeout, 429, and 5xx. Bound output length and concurrent generation calls; a local demonstration can begin with one active call and reject excess demand with an explicit busy response. Do not queue unlimited work or silently substitute mock results.
5. **Angular workflow — 90 minutes.** Build a single-page reactive form, character limit/counter, sample loader, generate button, loading/elapsed feedback, friendly errors, summary, and interactive radio-button quiz. Use local state/signals and HttpClient. Prevent duplicate submissions; abort/unsubscribe when leaving the workflow. Keep source text on failure and prevent stale results from being mistaken for the latest request. Render model text via interpolation; no innerHTML or trust bypass. Accessible labels/status announcements and readable mobile layout are enough.
6. **Meaningful verification — 60 minutes.** Run backend unit/API integration tests and focused Angular API/error/quiz tests. Manually review sample generation against the source. Check the production Angular build served from ASP.NET Core, including unknown API routes returning API errors instead of the SPA document.
7. **README, archives, and clean setup — 60 minutes.** Document actual commands and configuration, mock/live behavior, API examples, supported sizes/timeouts, architecture, privacy, trade-offs, measured live evidence, and AI tool use. Verify raw-log coverage and refresh archives before the final commit. Exercise setup in a fresh checkout once Git exists. Publication is a later explicit action.

Total: 465 minutes (7 hours 45 minutes), with 15 minutes contingency. These are estimates, not evidence that integration will fit. If the first experiment requires chunking/jobs, re-estimate before treating that expanded scope as an eight-hour commitment. Cut optional excerpts/download, packaging, and polish first; preserve tests and documentation.

## Long-input and asynchronous expansion criteria

- Character limits protect the application; provider token counting protects the selected model's context budget. Reserve space for system instructions, schema, and the requested output. `chars / 4` is only a rough English estimate and is insufficient to guarantee safety for Unicode or other languages.
- If a representative transcript fits context and quota and meets the measured deadline/quality target, retain one call. Context size alone does not establish latency or factual quality.
- If input exceeds the chosen model's request/context budget, either select a suitable free-tier model or explicitly add bounded chunking. Split on paragraphs/sentences, enforce forward progress on long unpunctuated input, preserve source offsets, and cap chunks and aggregate calls. Include source excerpts/fact provenance in intermediate output when required; a quote appearing only in an intermediate summary is not source evidence.
- Budget the reduce prompt as well as map prompts. If accumulated notes exceed that budget, use bounded hierarchical reduction or reject explicitly; do not silently truncate. Start chunk concurrency at one for quota predictability.
- Chunking is not an assumed fix for slow calls or free-tier throttling: additional calls can make both worse. Re-measure latency and total tokens before committing to it.
- If generation cannot meet the agreed request deadline, adopt `POST /api/v1/generations` returning `202` and `Location`, plus `GET /api/v1/generations/{id}`. Replace the synchronous contract explicitly; do not maintain two flows without need.
- A selected job design must specify bounded admission, job timeout, lifecycle states, expiry/retention, polling teardown, and shutdown semantics. In-memory jobs are lost on restart and must be described that way. Durable jobs require SQLite plus explicit recovery; persisting before enqueueing alone does not close the commit/enqueue failure window. Use a database-backed pending-work scan or equivalent recovery, and acknowledge at-least-once provider execution/cost risk. Do not promise exactly-once generation.

## AI quality and operational notes

- Version the prompt. Request source-grounded, distinct questions with plausible distractors, one correct answer, explanations, and coverage across the transcript. Delimiters reduce instruction confusion but are not a complete prompt-injection defense.
- Validate nonempty summary and question text, exactly five questions, exactly four distinct trimmed options each, unique questions, answer indices 0–3, and nonempty explanations. Do not assert that structural validation proves correctness.
- For insufficient material, support an explicit provider-level insufficient-content outcome mapped to a helpful client error, rather than repeatedly asking the model to invent five questions. Do not use an arbitrary UI-only question cap as a grounding check.
- If excerpts are added, define source normalization once and preserve raw source. Verify exact normalized substring membership and manually assess whether the excerpt supports the answer. Skip aggressive timestamp/whitespace rewriting that destroys provenance.
- Do not blindly strip arbitrary thinking tags or accept partial JSON to salvage responses. Parse the selected provider's documented response fields and reject incomplete/truncated output. No repair is attempted on refusal or insufficient source content.
- Log trace ID, provider/model, duration, status category, prompt version, and available token usage. Never log transcripts, raw prompts, provider bodies, or keys, including debug mode. Avoid raw exception logging that embeds provider content.
- No application persistence means no stored transcript history; live mode still transmits data to a third party. Use synthetic samples and describe applicable provider data-use terms without asserting enterprise privacy guarantees.
- Run locally by default. Public hosting requires an access/rate/spend-control decision; public source publication does not imply an unrestricted generation endpoint.

## Verification checklist

- [ ] Chosen SDK and Angular/Node versions build from documented commands; tests do not require a key.
- [ ] Default mock flow works and is visibly labeled. Live mode never falls back silently.
- [ ] Valid response contains the fixed quiz shape; unknown usage is null.
- [ ] Missing/blank, malformed, wrong-content-type, boundary-size, and oversized-body requests behave as documented.
- [ ] Insufficient source material produces a useful outcome instead of fabricated questions.
- [ ] Invalid provider JSON, missing fields, duplicate options/questions, wrong counts, invalid indices, refusal, and truncation are covered.
- [ ] Provider 429/5xx, deadline, client cancellation, and configuration errors are distinguishable and do not leak payloads.
- [ ] Representative long-input evidence covers latency, model context/quota, and factual review; supported limits reflect actual results.
- [ ] Angular handles loading, errors/retry, double clicks, answer checking, and stale results correctly.
- [ ] Script-like generated text displays as text. SPA fallback does not swallow unknown `/api` routes.
- [ ] Packaged Angular assets load from API hosting; dev proxy works independently.
- [ ] If chunking/jobs are adopted, test budgeting, admission, terminal states, shutdown/recovery, and polling teardown.
- [ ] README explains decisions and limitations, includes sample requests and live/mock setup, and links `Context/README.md` and `AI-LOGS.md`.
- [ ] Archives cover all relevant sessions through the latest completed turn; raw copies are preserved and any necessary redactions disclosed. No secret values are committed.
- [ ] Source plans and project artifacts are preserved; clean-checkout instructions work. Repository creation/publishing and submission messaging are separate from this planning step.

## Remaining questions and risks

1. **Provider access:** a Gemini key/model and actual account quotas have not been supplied or tested. Keep model selection configurable; use Groq only if the selected Gemini option is unavailable or unsuitable.
2. **Representative size:** the brief says “long” but specifies no duration, word count, or character count. AN's 50k–150k-character estimate is an assumption, not a requirement. Select a realistic synthetic long sample and make the supported range explicit.
3. **Time scope:** chunking, durable jobs, SQLite, three routes, multiple providers, Docker, and editable output together exceed the focused baseline. If selected later, revise the budget rather than describing them as free additions.
4. **Output language:** use English for the initial demo and document it; multilingual behavior requires dedicated quality checks and token budgeting, not just one prompt sentence.
5. **Delivery:** no fixed calendar due date or public repository destination is available. The brief says seven days, but its start date is unknown. Do not invent either.

## Official references checked

- [.NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy): .NET 10 is an LTS release; this supports the selected baseline, alongside locally observed tooling.
- [Gemini pricing](https://ai.google.dev/gemini-api/docs/pricing) and [API key setup](https://ai.google.dev/gemini-api/docs/api-key): free access is model/account dependent; each reviewer configures a backend key rather than receiving a shared committed key.
- [Gemini structured output](https://ai.google.dev/gemini-api/docs/structured-output) and [token counting](https://ai.google.dev/gemini-api/docs/tokens): validate output independently and budget actual model input/output.
- [Gemini terms](https://ai.google.dev/gemini-api/terms): assess free-service data use for the applicable account/region; synthetic transcripts keep the demonstration straightforward.

Confirm selected versions, exact model availability, and quotas again during implementation. No application builds, tests, live generation, or deployment have been performed by this planning task.
