# LearnSphere: AI-powered training content generator

## Summary and business context

Build a C# web proof of concept that turns a pasted course transcript into a summary and a multiple-choice quiz. Corporate Learning & Development managers need useful training material without manually reviewing hours of video. Prioritize a testable backend, clear API, reliable LLM interaction, and an explanatory README. Keep the UI functional and the implementation within the brief's approximately eight-hour budget.

Source: `Task/prescreen-brief-ivo-nedev.md`. The brief says delivery is due in seven days but supplies no start date; do not infer a calendar deadline.

## Repository evidence and scope

- This workspace currently contains the brief, root `AGENTS.md`, and Codex/Claude conversation context and tooling. No application, solution, tests, persistence, or reusable application patterns exist yet.
- The workspace was not a Git repository when checked. Initialize Git as part of implementation; publication and email submission remain separate actions.
- Installed SDKs observed: 10.0.201 and 10.0.401. The brief suggests C# 12/.NET 8; verify the chosen SDK can build and run the selected target, or install the required SDK/runtime before implementation. Do not silently assume .NET 8 is installed.
- The first-step skill's Linux GlobalHelperX paths do not exist in this workspace. Use the supplied brief and place this OP plan in `Task/OP/`.

## Proposed structure and design

Use one ASP.NET Core API project and an Angular frontend, as requested by the user on 2026-10-17. Keep Angular source in `src/LearnSphere.Web`. Use standalone components, a typed API service with Angular HttpClient, reactive forms, and local component state. Use a development proxy for `/api`; for the packaged build, serve the Angular build output from ASP.NET Core `wwwroot`, with SPA fallback that does not swallow unknown API routes. This adds a Node/Angular build prerequisite but keeps packaged hosting on one origin. Pin compatible Angular and Node versions during scaffolding. Use dependency injection and a small number of explicit boundaries; avoid additional architecture projects for this prototype.

Proposed files (not existing implementations):

```text
LearnSphere.sln
src/LearnSphere.Api/
  Program.cs
  Contracts/GenerateContentRequest.cs
  Contracts/GenerateContentResponse.cs
  Generation/ITrainingContentGenerator.cs
  Generation/TrainingContentService.cs
  Generation/TrainingContentValidator.cs
  Providers/LlmTrainingContentGenerator.cs
  Providers/MockTrainingContentGenerator.cs
  Configuration/GenerationOptions.cs
  wwwroot/ (generated Angular build output; not hand-edited)
src/LearnSphere.Web/
  angular.json
  package.json
  proxy.conf.json
  src/app/app.config.ts
  src/app/core/training-content-api.service.ts
  src/app/features/generator/
  src/app/features/quiz/
tests/LearnSphere.Api.Tests/
README.md
.gitignore
Context/Codex/
Context/Claude/
```

The service owns input/output validation and orchestration. The provider interface isolates the external AI dependency and permits deterministic tests. Use a typed HTTP client for the chosen real provider and a deterministic mock for local demonstration. Confirm the provider's official API contract during implementation before writing its adapter. Mock mode must be visibly labeled; it demonstrates workflow and is not evidence of AI content quality.

## API contract

- `POST /api/training-content`: JSON `{ "transcript": "..." }`.
- Success: `200 OK` with `summary`, `questions`, and `generationMode` (`live` or `mock`). Each question contains `prompt`, exactly four `options`, a zero-based `correctAnswerIndex`, and `explanation`.
- Prototype defaults: five questions and a summary of roughly 150–250 words, with shorter summaries for short source material. These are proposed choices, not requirements from the brief.
- Return consistent Problem Details: `400` for invalid input, `413` for oversized request bodies, `502` for malformed/unusable provider output or upstream failure, `503` for upstream throttling/unavailability, and `504` for provider timeout. Preserve client cancellation without misreporting it as an upstream error.
- Reject empty/whitespace transcripts. Set an explicit configurable text ceiling, initially 40,000 characters, plus a compatible request-body limit that allows JSON escaping and UTF-8 overhead. Document the supported range in the UI and README. Character count is not token count; select a model/context budget that covers the maximum input and output.
- Do not silently truncate. Oversized transcripts get a helpful error. Chunking is deferred and documented, so avoid claiming support for arbitrarily long transcripts.

## Implementation sequence and time allocation

1. **Scaffold and configuration — 45 minutes.** Initialize Git and the solution/API/test projects. Configure static files, DI, provider options, startup validation, and secret-free sample settings. Use the brief's .NET baseline if the environment supports it; document any deliberate target change. Exclude build output, local secrets, and developer-only settings from Git.
2. **Contract and service — 60 minutes.** Add request/response DTOs, validation, the service boundary, and endpoint. Make mock mode deterministic and explicit. Prove a valid request reaches the UI before adding the external integration.
3. **LLM adapter — 105 minutes.** Select one provider/model through configuration. Send a clear instruction separating trusted instructions from untrusted transcript text. Ask for grounded summary and questions, no outside facts, and the fixed JSON schema. Use schema-constrained output where supported, then deserialize and validate independently. Validate nonempty summary/questions/options/explanations, five questions, four distinct nonempty options per question, and answer indices 0–3. Handle refusals and truncated output as failures. Configure an upstream timeout, bounded output tokens, and cancellation propagation. Avoid automatic retries initially to keep latency and spend predictable; return a useful error and allow explicit user retry. Never fall back silently to mock output.
4. **Frontend workflow — 60 minutes.** Build a labeled transcript textarea, input counter/limit, generate button, loading feedback, clear errors, summary, and quiz. Disable duplicate submissions while processing and restore controls after failure. Let users select answers and reveal correctness/explanations. Use semantic controls and `textContent` for generated text, with readable mobile layout and accessible status announcements.
5. **Targeted automated verification — 75 minutes.** Test the validation and provider boundary cases below, plus endpoint integration with an injected fake provider. Exercise the user flow manually. Use an optional real-provider smoke run only when credentials are available.
6. **README and submission readiness — 75 minutes.** Write setup/run/test commands, configuration, sample API request/response, mock/live behavior, architecture, trade-offs, supported transcript limits, privacy/cost notes, and AI-tool disclosure. Collect full available AI transcript exports and confirm their completeness. Check clean-clone instructions and the working tree. Reserve publication for the user's requested destination and authorization.

The original allocation totals seven hours. Reserve an additional 30 minutes for Angular scaffolding, proxy/build integration, and targeted frontend checks, leaving approximately 30 minutes of contingency within eight hours. The frontend step uses Angular reactive forms and template interpolation, rather than manual DOM updates. Verify loading/error recovery and quiz interactions with focused component tests. Docker is optional if time remains; it must not displace core workflow, testing, or documentation.

## Candidate deviations for step-two review

User feedback on 2026-10-17: the remaining recommendations sound good, and the live integration should support a free API tier. Recommend Gemini Developer API as the initial provider, subject to account/model free-quota availability; keep the model configurable and verify the exact model during implementation. Official pricing and structured-output documentation were checked. Groq is an alternative with a rate-limited free plan. Configure each user's key server-side through user secrets/environment variables; do not bundle a shared key in the public repository or Angular application. Use synthetic demo transcripts because free-service data-use terms must be considered. Retain mock mode for reviewers without credentials. Treat supporting excerpts and JSON download as stretch goals, as previously recommended.

Only the Angular change above is confirmed. Consider these additions before finalizing the implementation plan:

- Prioritize one working live-provider path alongside mock mode, with a small synthetic transcript and documented expected factual points. This offers stronger evidence of the central AI behavior than a mock-only demo.
- Add optional supporting transcript excerpts to quiz answers and validate that each excerpt occurs in the source. This helps managers review grounding; an exact match alone does not prove an answer is correct. Adds schema, prompt, UI, and test work.
- Add a sample-transcript button and JSON download to make the evaluation workflow easy without introducing storage. Keep both as stretch features until the core flow works.
- Revisit the proposed 40,000-character limit against representative long course transcripts. If it is inadequate, explicitly budget token-aware chunking and synthesis rather than claiming arbitrary long-input support. This is the largest potential scope increase.
- Keep synchronous generation for the initial POC. Background jobs and polling become worth exploring only if representative generation exceeds practical request timeouts.

Recommended scope remains no persistence, authentication, state-management library, or streaming for the initial workflow. These are deferred scope choices, not missing requirements from the brief.

## AI quality, privacy, and operational behavior

- Treat transcript instructions as data, including attempts to override the task or request secret disclosure. Do not provide tools or secrets to the generation context.
- Ground both outputs in the transcript. Ask the model to avoid ambiguous questions and unsupported claims. Schema validation verifies shape, not factual truth; manually review a representative sample against its source.
- Detect clearly insufficient source text with simple input validation and document limits. Do not add an unbounded regeneration loop for poor material.
- Keep provider keys server-side in environment variables or development user secrets. Never place keys in JavaScript, committed configuration, or logs.
- Log request identifiers, elapsed time, provider status, and validation failure categories. Do not log transcripts, full generated output, authorization headers, or provider response bodies that may contain sensitive text.
- Inform users that live mode sends transcripts to the configured provider. Do not claim retention guarantees without checking the provider's configuration and terms.
- No database or migration is necessary for a single request/response workflow. Do not retain transcripts or generations in application storage. Persistence, user accounts, history, and permissions are outside the requested prototype.
- A prototype without authentication or rate limiting should run locally by default. Public hosting of the paid generation endpoint requires an explicit access/spend-control decision. Publishing source code is a separate concern from hosting the service.

## Testing and verification checklist

- [ ] Solution builds and tests run using the documented SDK and commands.
- [ ] Valid transcript returns one summary and five structurally valid questions.
- [ ] Empty, whitespace, missing transcript, malformed JSON, and maximum/over-limit input behave consistently.
- [ ] Invalid provider JSON, missing fields, duplicate options, wrong counts, and out-of-range indices are rejected.
- [ ] Timeout, upstream 429/5xx, refusal, and cancellation follow the documented error behavior without leaking provider details.
- [ ] Tests use a fake HTTP handler/provider and need no API key; mock mode works on a fresh checkout.
- [ ] Browser flow displays loading, output, answer feedback, and recoverable errors; duplicate clicks do not send duplicate requests.
- [ ] HTML/script-like transcript or generated content displays as text and does not execute.
- [ ] A manually reviewed sample covers summary faithfulness, answer grounding, distractor quality, and instruction-injection text.
- [ ] README explains setup, environment variables, test commands, API, limits, decisions, deferred features, and AI assistant usage.
- [ ] No secrets or private training transcripts are tracked; no migration is required.
- [ ] Full unedited AI conversation logs are committed before submission. Existing Codex Markdown logs summarize tool actions and cannot alone establish compliance. Obtain native exports where available; preserve originals and explicitly disclose any unavailable history or necessary secret redaction. Do not call reconstructed summaries complete exports.
- [ ] Once publishing is authorized, the final public repository URL is accessible and contains the complete solution and required logs. Do not send submission email without explicit instruction.

## Assumptions, open questions, and trade-offs

- **Provider credentials:** not supplied. Default to a clearly labeled mock locally; select and document the live provider when implementing its adapter.
- **Runtime:** .NET 8 is suggested, not mandated. Resolve runtime availability and supported target before scaffolding.
- **Transcript size:** 40,000 characters is a proposed bounded POC limit. Confirm this meets evaluation expectations; larger documents require token-aware chunking and cross-chunk synthesis, which materially expands scope.
- **Content settings:** five questions, four options, and summary length are defaults chosen for a predictable demo; avoid adding customization until the core workflow is complete.
- **Data handling:** no persistence/authentication is assumed. Corporate deployment would require tenant isolation, access controls, rate limiting, and a retention policy beyond this exercise.
- **AI archive:** workspace instructions help maintain future logs, but do not automatically produce complete platform exports. Full export availability remains a delivery dependency.
- **Submission:** public source is required by the brief, but the repository owner/name and publication authorization have not been provided. Prepare locally without inventing a remote.

This is a planning deliverable only. No application code has been implemented.
