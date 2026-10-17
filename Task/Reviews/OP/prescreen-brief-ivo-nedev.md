# Post-execute review — LearnSphere

## Remediation update — 2026-10-17

Following the owner's fix request, R2–R7 have been addressed with regression coverage. The original findings below remain as the historical review, not the current defect list.

- Typed token-count/provider errors are preserved; unknown counts no longer produce estimated size errors. Added safe categories for bad requests, missing models, malformed envelopes, key rejection, timeouts and unavailable counts, with Retry-After surfaced when reported.
- Provider/model intent is persisted with submissions and enforced before processing. A queued job never silently changes provider after restart.
- Polling waits for in-flight responses, counts attempts, has request timeouts and a finite API-configured maximum. Manual status refresh never resubmits generation.
- Route parameters drive job state reactively; quiz state resets, and actions use the current ID.
- Final result checkpoints allow same-ID save-stage retry after transient storage failure or restart, without repeated inference. Tests cover failed terminal saves, a read-only database, an unwritable checkpoint path, and reopening the checkpoint. If neither the database nor the checkpoint filesystem can write, only the process-local copy can survive; a restart before any durable checkpoint succeeds remains an explicit limitation.
- Angular obtains limits and polling settings from `/api/v1/capabilities`; tests deliberately change server limits to prove there are no mirrored hardcoded defaults in the form.
- The owner also requested optional section processing while keeping the 200k-character cap. This is implemented, bounded, and live-verified on the long synthetic fixture: four section calls plus synthesis, approximately 74 seconds, all five final excerpts verified. Single-call remains the default. Section-level durable resume is future work; save-stage resume is implemented.

Verification: 96 backend tests and 16 Angular tests passed; warning-free API build, production Angular build, recognized-credential scan, and Docker image/health/volume-upgrade checks passed. The owner is Ivo Nedev; GitHub work is deferred. R1 remains a disclosed submission-format exception, not a reason to publish secrets or internal reasoning. No statement of evaluator acceptance or completed credential rotation is made.

Reviewed: 2026-10-17 (Europe/Kiev). Review only: no application fixes, deployments, provider requests, or commits performed.

## Verdict

The core application matches most of the final plan, but is not ready to describe as fully reviewed or submission-complete. There are **seven findings: one P1 delivery/compliance gap, five P2 correctness issues, and one P3 configuration inconsistency**. The P1 needs an explicit submission decision; the code issues have concrete fixes and should not require new product decisions.

The existing successful test results are useful, but do not exercise the important failure and navigation paths identified here. No evidence of a committed credential was found by the current scanner; its coverage is limited to recognized credential formats.

## Scope, sources, and comparison base

- Branch: `master`, HEAD `3e3f20c` at review start. No upstream or remote configured, and no pre-application base branch exists.
- Reviewed the initial implementation from Git's empty tree (`4b825dc642cb6eb9a060e54bf8d69288fbee4904`) to HEAD, plus relevant untracked planning files. This is a greenfield review, not a comparison against an existing production branch.
- Raw brief: `Task/prescreen-brief-ivo-nedev.md`.
- Final authority: `Task/BOTH-FINAL/prescreen-brief-ivo-nedev.md`.
- Evolution: `Task/OP/prescreen-brief-ivo-nedev.md`, `Task/OP/prescreen-brief-ivo-nedev-2.md`, `Task/AN/prescreen-brief-ivo-nedev.md`, `Task/AN/prescreen-brief-ivo-nedev-2.md`; inspected relevant decisions and disagreements. Earlier rejected designs are not requirements.
- Instructions and evidence: `AGENTS.md`, archive indexes/execution notes, README, AI-LOGS, application source, tests, Docker/Compose, scripts, and recorded live outputs.
- The skill's Linux GlobalHelperX paths do not apply here. `Task/` is the project planning location. There is no separate `Task/BOTH/` comparison artifact; the revision-two plans and final guide provide that context. Neither the brief nor the final plan is missing.
- Output adapted to `Task/Reviews/OP/prescreen-brief-ivo-nedev.md`, outside application code folders.

## Findings ordered by severity

### R1 — P1: Required unedited-log deliverable is not satisfied literally

**Evidence:** `AI-LOGS.md:5–11`, `scripts/archive-safety.cjs:17–21`, raw brief Deliverables and On AI assistants sections.

The brief explicitly requires complete, unedited conversation logs. The implementation intentionally removes credentials and internal reasoning and reserializes JSONL. The documentation correctly discloses this, but disclosure alone does not establish that the evaluator accepts an exception. A note saying the archive exists is not evidence that the literal deliverable has been fulfilled.

**Action/decision:** rotate the exposed credential, retain the safe publication copies, and confirm that the disclosed exceptions are acceptable for submission. Never restore a credential to public logs. Preserve the available user-facing exchange as fully as possible; refresh the final completed turn before publication. Treat this as a delivery acceptance issue, not a request to reveal internal reasoning.

### R2 — P2: Token-count failures can become misleading size failures

**Evidence:** `src/LearnSphere.Api/Providers/GeminiLlmClient.cs:20–30`; `src/LearnSphere.Api/Generation/TrainingContentService.cs:25–29`; `src/LearnSphere.Api/Domain/Models.cs:18`.

`CountTokensAsync` converts every LlmException—including authentication, throttling, and provider unavailability—to null. The service then estimates more than four tokens per character. For the included roughly 46k-character sample this exceeds the configured 100k-token budget, so an invalid key, unavailable model, or a throttled token-count endpoint yields `input_too_large`. No generation call occurs, the real error category is lost, and the job is nonretryable even when the provider issue was transient. The successful live run used only 8,185 reported input tokens for that sample.

The final plan itself makes count errors nonfatal, so this is partly a design defect carried through implementation and amplified by the changed fallback. The README acknowledges conservative rejection, but does not make a false size diagnosis correct.

**Fix:** retain typed failure information. Propagate authentication/configuration/rate-limit failures or return a distinct token-count-unavailable outcome; reserve fallback for a deliberate, documented class of failures. Add a combined adapter/service test using a long input and 401/429/503 count response. Do not add provider transport retries.

### R3 — P2: Queued jobs can change provider across restart while retaining the old mode label

**Evidence:** `src/LearnSphere.Api/Program.cs:40,85–93`; `src/LearnSphere.Api/Jobs/GenerationWorker.cs:62–69`; `src/LearnSphere.Api/Domain/Models.cs:34,46–49`; `src/LearnSphere.Web/src/app/features/generation/generation.page.html:10–25`.

Submission stores `GenerationMode` from current configuration, but does not persist the intended provider/model. Queued rows survive restart. Processing uses the newly configured ILlmClient, and Succeed never updates GenerationMode. Queue under Mock, restart under Gemini: the transcript can be sent to Google and the result still carries the MOCK OUTPUT banner. The reverse transition can present mock output as live generation. Metadata may contradict the banner, but does not correct it.

**Fix:** persist and enforce provider intent for queued work. If configuration no longer matches, fail with an explicit safe configuration-change outcome and require resubmission. At minimum derive final mode from the actual provider, but that alone does not address unexpected live transmission of a formerly mock job. Test both transition directions.

### R4 — P2: Slow polling responses are continually cancelled

**Evidence:** `src/LearnSphere.Web/src/app/core/generations-api.service.ts:6–10`; `src/LearnSphere.Web/src/app/core/api.spec.ts:9–24`.

The interval emits every 1,500 ms and `switchMap` cancels the in-flight HttpClient request each time. If status requests consistently need more than 1,500 ms, no response reaches the UI, including terminal results. The existing tests use immediately emitting `of(...)`, so they cannot expose this.

**Verification:** a read-only in-memory experiment with the installed RxJS used a 10 ms polling interval and 30 ms response latency. Six requests started and finalized, with zero responses observed. The same cancellation relationship applies at the production timings.

**Fix:** use `exhaustMap` for interval-driven polling or schedule the next poll after the previous request completes. Add a delayed-response test, terminal-stop test, and teardown test. This corrects a pattern also present in the final guide.

### R5 — P2: Same-component route changes keep using the previous generation ID

**Evidence:** `src/LearnSphere.Web/src/app/features/generation/generation.page.ts:24–30,48–85`; `src/LearnSphere.Web/src/app/app.routes.ts:5–9`. Installed Angular router `_router-chunk.mjs:4114–4115` reuses a route when its routeConfig is unchanged.

The ID is captured once from snapshot, and polling is initialized only in the constructor. Navigating client-side from `/generations/A` to `/generations/B` using the same route definition reuses the component, leaving the displayed result, polling, Retry, and Delete bound to A while the URL identifies B. Retry's home-page detour avoids this in that particular path, but does not make the component react correctly to route changes.

**Fix:** derive loading from observable route parameters and switch the whole job stream when the ID changes. Reset job/error/elapsed/busy and quiz answer state. Test A→B navigation through RouterTestingHarness, including which ID Delete targets. No need to add another routing or state-management library.

### R6 — P2: A terminal database-save failure strands the job until process restart

**Evidence:** `src/LearnSphere.Api/Jobs/GenerationWorker.cs:25–37,62–75`.

The worker first commits Processing. GenerationProcessor clears the transcript only on its tracked object, then saves outside its error-handling block. If that final save fails—for example due to a transient SQLite write/IO failure—the worker logs an iteration failure and disposes the scope. Its next scan selects only Queued rows. The database row remains Processing with its original transcript even after the database recovers; there is no current-process recovery or expiry. It remains undeletable and occupies a pending-job slot. Startup recovery fixes it only after a restart.

**Fix:** provide bounded persistence-only recovery that does not repeat inference, or mark the affected row Failed when the database becomes writable again. Document the unrecoverable-storage case and add a SaveChanges fault-injection test that recovers without restarting the host. Do not solve this by automatically rerunning the provider request.

### R7 — P3: Advertised configurable transcript limits are hardcoded in Angular

**Evidence:** `src/LearnSphere.Api/Configuration/Options.cs:18–20`; `src/LearnSphere.Api/Program.cs:77`; `src/LearnSphere.Web/src/app/features/generate/generate.page.ts:20–25`; its template `:32,41–46`; `README.md:120–132`.

The backend accepts configured limits, but the browser always enforces and displays 500–200,000 characters. Lowering the minimum leaves valid backend inputs impossible to submit; increasing the maximum blocks newly supported input; decreasing the maximum keeps advertising a larger range until the server rejects it. Defaults agree, so this does not break the default demo.

**Fix:** expose nonsecret generation capabilities to the frontend, or explicitly freeze these values for the POC and remove the configurability claim. If configuration remains supported, derive validators, counters, and help text from the same values. Add a nondefault-limit case.

## Plan compliance and smaller inconsistencies

| Area | Assessment |
|---|---|
| C# API, Angular, default Mock, Gemini REST | Implemented in the chosen final architecture. |
| SQLite as queue, atomic conditional claim | Implemented. Single-host limitation is documented. R6 concerns persistence failure after claim, not a lost wake signal. |
| Restart rules | Queued work resumes; Processing becomes interrupted. Correct under unchanged configuration; R3 covers configuration changes. |
| Five questions/four options, strict shape, bounded repair | Implemented. Refusal, truncation and insufficient-content paths avoid repair as planned. |
| Grounding | Exact normalized substring flag is implemented. It checks provenance, not semantic entailment; this is disclosed and is not a missing promised guarantee. |
| Docker, non-root, volume, healthcheck | Present; current running container is healthy. Earlier runtime checks are recorded. |
| README | Covers setup, architecture, trade-offs, model configuration, privacy, testing, AI usage and exact no-automatic-retries sentence. |
| Unknown-token fallback | Four tokens per UTF-16 code unit replaces the plan's chars/4 × 1.2. Documented deviation, but its error behavior needs R2. |
| Persistence mapping | JSON TEXT value conversion replaces owned JSON graphs; documented and suitable for replacement-only records. Not a correctness defect by itself. |
| Files/folders | Endpoints are concentrated in Program.cs and repository operations in jobs/endpoints instead of the planned dedicated endpoint/repository files. Boundaries remain logical, but readability/extraction is a maintainability follow-up, not a new architecture requirement. |
| Thinking budget | Omitted by default rather than 0, justified by the successfully selected model. No need to reopen the provider decision. |
| Unicode matching | Invariant uppercase approximates full case folding. Acceptable for documented English scope; do not claim arbitrary multilingual equivalence. |
| Excerpt word limit | Configurable maximum can be less than 40, but prompt/schema guidance still says 40. Nondefault values can cause avoidable repair/failure; align generation guidance if supporting customization. |
| Error copy | provider_invalid_output says “including after one repair attempt” even when an empty/invalid envelope takes a no-repair path. Use metadata or neutral wording. input_too_large does not state the numeric budget requested by the final guide. |
| Logs | Safe publication copies exist, but R1 remains. Current-turn coverage requires a final sync after completion. |
| Git/publication | Local commits exist. No remote/public URL exists. This was deliberately deferred, so it is an outstanding delivery task rather than a broken implementation. `Task/` remains untracked/user-managed. |
| Planning text | Final guide still says no key exists and live verification is pending. These are stale factual notes after the successful live runs. Keep design history, but update status before treating the guide as current completion evidence. |

## Items needing your decision or confirmation

1. **Submission logs:** proceed with fully disclosed sanitized available logs and confirm the evaluator accepts the exception. Never publish the credential or internal reasoning. Credential rotation is still unverified.
2. **Supported input size:** retain the provisional 200k-character intake cap with the honestly documented ~46k live test, or invest in representative diverse transcripts at the intended upper bound before claiming support. Recommendation: test larger, nonrepetitive material; do not add chunking automatically.
3. **Public repository destination and timing:** select owner/name and authorize publication when the review fixes and log review are finished. The brief requires one public URL, not necessarily public hosting.
4. **Task artifacts:** decide what you will include from user-managed `Task/` before submission. They currently will not accompany a Git push. This follows the recorded instruction, so it is not an unauthorized omission to fix silently.
5. **Demo mode:** keep Mock as the default as already agreed; each reviewer can configure their own key for live mode. No decision to embed a shared key is necessary or recommended.

The six code findings R2–R7 are implementation fixes, not questions that need you to design the solution. A reasonable fix order is R2/R3, R4/R5, R6, then R7.

## Verification gaps and residual risks

- No connected browser was available during implementation, and no visual or complete UI click-through was performed in this review. Desktop/mobile, keyboard use, retry, deletion, and back/forward navigation still need end-to-end coverage.
- Existing polling tests use immediate responses, and there is no GenerationPage route-transition test. No test covers provider switching with queued records or a failed terminal persistence save.
- Prior implementation evidence reports 67 backend, eight Angular, and two archive-safety tests passing plus clean-checkout builds, live short/long samples and Docker persistence/non-root checks. Those full suites were not rerun here because this review is read-only; their earlier success does not invalidate the new findings.
- The long fixture repeats the same short course. It proves context/transport handling, not a diverse long-course summary's coverage. The short recorded summary adds a mild urgency inference, already disclosed in the recorded factual review.
- No authentication, tenant isolation, automatic retention expiry, secure database erasure, or durable multi-host leasing is implemented. These are documented out-of-scope items for local use; do not silently turn this POC into a publicly exposed generation endpoint.
- Archive redaction recognizes selected credential prefixes only. A zero-match scan is not a general secrets/privacy audit. Exported tool activity also contains local paths and unrelated setup context; review the publication scope.
- `sanitizeJsonl` silently skips any invalid JSONL line, not only a trailing in-progress line. If a stored middle record is corrupt, archive coverage can be lost without a warning. This is a robustness follow-up for the completeness requirement; no corrupt record was established here.
- Model availability/free quotas can change. This review made no additional live calls and did not re-check provider pricing; the recorded success is historical evidence for this account, not a quota guarantee for another reviewer.

## Verification actually performed in this review

- Read-only Git status, branch/remotes/history, empty-tree diff/stat, and `git diff --check`.
- Read source, tests, relevant plan decisions, documentation and recorded outputs; checked installed Angular router reuse implementation.
- `node scripts/check-secrets.cjs`: zero recognized credential-pattern files.
- `node --test scripts/archive-safety.test.cjs`: two passed (pure in-memory tests).
- In-memory installed-RxJS delayed-response experiment: reproduced polling cancellation with no emitted responses.
- `docker compose ps --format json`: existing app healthy. No container or database state was changed.
- Wrote only this review and the required conversation note/index. No code fixes, commits, provider transmissions, dependency changes, application tests with filesystem outputs, or publication performed.
