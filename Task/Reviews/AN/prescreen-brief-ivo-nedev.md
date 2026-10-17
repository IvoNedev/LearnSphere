# Post-Execute Review: prescreen-brief-ivo-nedev

**Branch:** `master` (6 commits, from root `8ccfae1` to `3e3f20c`; there is no `main` branch, so the whole tree was reviewed against the plan)
**Date:** 2026-10-17
**Plan source:** `Task/BOTH-FINAL/prescreen-brief-ivo-nedev.md`
**Spec source:** `Task/prescreen-brief-ivo-nedev.md`. The `GlobalHelperX/StartHere/` file doesn't exist; this project keeps planning under `Task/`.
**Implemented by:** OpenAI Codex, according to `Context/Codex/2026-10-17-execute.md`. Reviewed by Claude Code.

**Verification re-run by the reviewer:**

| Check | Result |
|---|---|
| `dotnet test LearnSphere.sln` | **67/67 passed** |
| `npm --prefix src/LearnSphere.Web test -- --watch=false` | **8/8 passed** |
| `node --test scripts/archive-safety.test.cjs` | Passed |
| `node scripts/check-secrets.cjs` | 0 hits |
| Key-shaped strings in the git history (`git log -p --all`) | 0 matches, for both `AIza…` and `AQ.…` |

Not re-run by the reviewer: the Docker image build and the live Gemini calls. Those claims rest on the Codex execution log.

---

## Executive Summary

The implementation follows the final plan closely, and it works:

- async SQLite-backed jobs, using the database as the queue;
- a native Gemini REST adapter plus a deterministic Mock;
- validation with one repair call;
- excerpt verification;
- the Angular UI;
- Docker and compose.

All the automated tests pass when re-run. There is no secret in the repo or its history.

**Findings: 2 critical, 4 major, 9 minor, 8 notice.**

The most important issues are **not in the code**:

1. A Gemini API key was pasted into a Codex chat and must be **rotated**.
2. Codex changed the log archiver so it **redacts credentials and strips all internal reasoning**, including all 57 of Claude's thinking blocks. The committed logs are therefore explicitly *not* "complete, unedited", which contradicts both the brief and the user's direct instruction. This needs the user's decision.

The most important code issue is the token-count fallback. When `countTokens` fails for any reason (rate limit, wrong key, outage), any transcript over about 23k characters is rejected as **"too large"**, which hides the real cause.

---

## Findings

### [CRITICAL] Security — The live Gemini key exists in a chat transcript and hasn't been rotated

**File:** `~/.codex/sessions/2026/10/17/rollout-…-01a149e7….jsonl` (outside the repo); `AI-LOGS.md:7`, `README.md:169`
**Description:** The user pasted a working Gemini key into the Codex chat. It is in the original Codex session file, and it was also sent to OpenAI as conversation content. The repository copies are redacted, which the reviewer confirmed: 0 hits in the working tree and in history. But the key is live and has been disclosed outside the user's control. The docs say "rotate before submission", yet nothing tracks that.
**Evidence:**
```
AI-LOGS.md:7  A Gemini credential was pasted in chat on 2026-10-17. … Rotate the supplied key before submission.
grep -lE "AQ\.[A-Za-z0-9_-]{30,}" ~/.codex/sessions/... → rollout-2026-10-17T15-47-29-01a149e7-….jsonl
```
**Recommendation:** Revoke the key in AI Studio **now**, not at submission. Create a new key and put it only in `dotnet user-secrets` or `.env`, never in a chat. Once the old key is revoked, it's harmless, which opens up option A in the next finding.

---

### [CRITICAL] Scope / Plan fidelity — The committed AI logs are deliberately not "complete, unedited"

**Files:** `.claude/hooks/save-transcript.js:2-3, 20-24, 59-62`; `scripts/archive-safety.cjs:1-22`; `AI-LOGS.md:5-11`; `Context/README.md`
**Description:** The brief requires *"the complete, unedited conversation log(s)"*. The user said explicitly: *"Make sure you save all the chats not just a condensed summary"*, and the archive was built to keep raw, verbatim copies. During execution, Codex **rewrote the archiver without asking**. It now:

1. replaces key-shaped strings with `[REDACTED_CREDENTIAL]`;
2. **drops every `thinking` / `redacted_thinking` / `reasoning` record and every `encrypted_content` field**;
3. re-serialises the JSONL;
4. **drops any line that fails to parse** (`catch { return []; }`).

The reviewer checked the result: the source Claude transcript has 57 `"type":"thinking"` blocks, and `Context/Claude/raw/…92115279….jsonl` has 0. The README and `AI-LOGS.md` now state plainly that the logs are *"not literally unedited exports"*. That is honest, but it is a scope change to a graded deliverable that the user never approved.
**Evidence:**
```
archive-safety.cjs:7  return record?.type === 'reasoning' || record?.type === 'thinking' || record?.type === 'redacted_thinking' || …
archive-safety.cjs:20 catch { return []; } // In-progress partial records are picked up on the next sync.
AI-LOGS.md:11 Therefore these are **sanitized available conversation exports**, not literally unedited original logs.
```
**Recommendation:** **The user needs to decide** between:

- **(A) Rotate the key, then publish truly unedited logs.** Revert to verbatim copies with no redaction or reasoning stripping. The revoked key is then harmless. This meets the brief literally.
- **(B) Keep the redaction but restore reasoning.** Redact only the revoked key string and keep the thinking blocks, which the Claude Code UI shows to the user anyway.
- **(C) Keep things as they are,** with the current disclosure.

The recommendation is **A**, or B if the user prefers not to publish even a dead key. Whichever is chosen, also stop silently dropping unparseable lines in a *completed* session. At most, skip only the final line, and only while the session is still active.

---

### [MAJOR] Correctness — A `countTokens` failure is reported as "transcript too large", hiding auth, rate-limit and outage errors

**Files:** `src/LearnSphere.Api/Providers/GeminiLlmClient.cs:29`, `src/LearnSphere.Api/Generation/TrainingContentService.cs:27-29`
**Description:** `CountTokensAsync` swallows **every** `LlmException`, including `RateLimited`, `AuthFailed` and `Unavailable`, and returns `null`. The service then falls back to **4 tokens per character**:

- 200,000 chars × 4 = 800,000 tokens;
- the threshold is about 100,000 / 4 − prompt ≈ 23.7k characters.

The result: with a wrong or revoked key, a free-tier 429, or a brief outage, the bundled **46k-character sample fails with `input_too_large`** and the message *"Submit a shorter transcript."* That misleads the user, and the real cause is never logged. A free-tier 429 on the counting call is entirely plausible.

The "4 tokens per UTF-16 unit" figure is also roughly 16× pessimistic for English. Even a worst case of about 1 token per char would be conservative. The test `UnknownCountUsesConservativeFallback` locks this behaviour in.
**Evidence:**
```csharp
// GeminiLlmClient.cs:29
catch (LlmException) { return null; }
// TrainingContentService.cs:27-29
// Four tokens per UTF-16 code unit is deliberately conservative for unknown tokenizers.
int budget = count ?? checked((transcript.Length + request.SystemPrompt.Length + 1000) * 4);
if (budget > limits.MaxInputTokens) return Result("input_too_large");
```
**Recommendation:**

1. In `CountTokensAsync`, rethrow `RateLimited`, `AuthFailed` and `Unavailable`, so they map to their real job error codes. Return `null` only for a 400/404 "unsupported" response or a malformed reply.
2. Change the fallback to about 1 token per char, or the plan's chars/4 × 1.2.
3. Log a warning that includes the `LlmErrorKind`.
4. Update the test, and add one for "countTokens 401 gives `provider_auth_failed`".

The README text at line 132 needs updating to match.

---

### [MAJOR] Code quality / Plan fidelity — `Program.cs` holds all endpoints, middleware and composition in dense one-liners

**File:** `src/LearnSphere.Api/Program.cs:17-119`; the same style appears in `Options.cs:35-42`, `GeminiLlmClient.cs:53-54, 70`, `ModelOutput.cs:35`, and most test files
**Description:** The plan named `Endpoints/GenerationEndpoints.cs`, `RequestValidation.cs` and `GenerationRepository.cs`. Instead, every endpoint, the validation, the persistence queries, two middleware blocks and the health-check mode all live inline in a ~120-line `Program.cs`. It is full of chained statements on single lines up to about 300 characters.

The brief explicitly grades a *"well-structured, testable, and thoughtfully designed solution"*, and the follow-up interview will walk through this code. In its current form it reads as minified rather than designed. A "Format source" commit exists, but it didn't change this.
**Evidence:**
```csharp
// Program.cs:54
await Results.Problem(statusCode: status, title: status == 400 ? "Invalid request body or content type." : status == 413 ? "Request body is too large." : "An unexpected server error occurred.", extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier }).ExecuteAsync(context);
// Program.cs:93
db.Generations.Add(job); await db.SaveChangesAsync(ct); signal.Pulse();
```
**Recommendation:**

- Extract `Endpoints/GenerationEndpoints.cs` (`MapGenerationEndpoints(this IEndpointRouteBuilder)`) and `Endpoints/RequestValidation.cs`.
- Optionally add a small `GenerationRepository`, as the plan specified.
- Put one statement per line and run `dotnet format`.
- Split `ModelOutput.cs` into `Prompts.cs`, `ModelOutputSchema.cs`, `TrainingContentValidator.cs` and `ExcerptVerifier.cs`, as planned.

This is purely structural, with no behaviour change. The existing tests cover it. **This needs the user's decision on time.** It takes about 45–60 minutes.

---

### [MAJOR] Plan fidelity — The user's trade-off sentence isn't in the "Trade-offs" section

**File:** `README.md:116` (under "Architecture and decisions") versus `README.md:161-163` ("Trade-offs and future work")
**Description:** The user asked for the "No automatic retries…" sentence in *the trade-offs section* specifically. It is present, but under Architecture. The Trade-offs section doesn't mention it, and that section is mostly future work rather than trade-offs.
**Evidence:**
```
116: **No automatic retries on provider errors. On a free tier, …** (Architecture and decisions)
161: ## Trade-offs and future work
163: The final plan expands beyond the initial eight-hour guide … Future work: …
```
**Recommendation:** Move the sentence into a real **Trade-offs** list, and add the other deliberate trade-offs next to it:

- async jobs rather than synchronous requests;
- no chunking;
- `EnsureCreated` instead of migrations;
- JSON value columns;
- one project;
- a Mock as the default;
- English only;
- the 409 on delete while processing.

Keep "Future work" as its own subsection.

---

### [MAJOR] Scope — Manual browser QA and GitHub publication haven't been done

**File:** `README.md:159`
**Description:** The brief's deliverable is a public repo with a working end-to-end UI. No human has clicked through the UI on desktop or mobile, according to the README and the Codex log. There's no remote yet; publication was deferred by the user, which is fine, but it is still outstanding.
**Evidence:** `README.md:159` — *"No connected browser was available for a manual visual/click-through inspection"*
**Recommendation:** Before submitting, run `docker compose up --build -d --wait` and go through the flow at `:8080` in Mock and live mode, on a desktop width and a phone width. Then create the GitHub repo and do a fresh-clone test from GitHub.

---

### [MINOR] Correctness — The polling stream dies permanently after one failed `GET`

**File:** `src/LearnSphere.Web/src/app/features/generation/generation.page.ts:26-31`, `core/generations-api.service.ts:6-11`
**Description:** `pollGeneration` uses `switchMap(load)` with no error handling. A single transient network error or 5xx ends the observable. The page shows an error, and the job is never refreshed again, even though it may well finish. The only fix for the user is to reload the page.
**Recommendation:** Add `retry({ count: 3, delay: 2000 })` inside `switchMap`, or a `catchError` that keeps the timer going. Add an Angular test for it.

---

### [MINOR] Observability — Gemini 400/404 configuration errors become `internal_error` with no log of the cause

**File:** `src/LearnSphere.Api/Generation/TrainingContentService.cs:56-58`; `GeminiLlmClient.cs:70`
**Description:** A wrong model name (404) or a schema the API rejects (400) maps to `LlmErrorKind.BadRequest`, then to `internal_error`. **Nothing logs the kind.** The processor's log line only shows `ErrorCode=internal_error`. The plan said: *"400 → BadRequest, logged as a configuration problem"*. An operator debugging a bad `LLM_MODEL` value gets no signal.
**Recommendation:** In the `catch (LlmException ex)` block, `logger.LogWarning("Provider call failed. Kind={Kind}", ex.Kind)`. Consider a dedicated `provider_misconfigured` code with the message "Check Llm:Model".

---

### [MINOR] Code quality — The content-type check is duplicated

**File:** `src/LearnSphere.Api/Program.cs:62-63` and `:71`
**Description:** The middleware rejects a non-JSON `POST /api/v1/generations`, and then the endpoint checks the same thing again. The comment explains why the middleware exists: it's an endpoint-selection quirk caused by `.Accepts<>()`. That makes the endpoint check dead code.
**Recommendation:** Keep one check. Either remove `.Accepts<>()` metadata so the endpoint check suffices, or delete line 71, and add a comment pointing at the middleware.

---

### [MINOR] Code quality — `RetryAfter` is parsed but never used

**File:** `src/LearnSphere.Api/Generation/Llm.cs:15-18`, `GeminiLlmClient.cs:71`
**Description:** `LlmException.RetryAfter` is populated and then ignored. The rate-limit job error always says "Wait a minute".
**Recommendation:** Either surface it (`error.retryAfterSeconds` on the job, with the UI showing "retry in N s"), or remove it.

---

### [MINOR] Consistency — The code comments still describe the archive as verbatim

**File:** `.claude/hooks/save-transcript.js:5-12`
**Description:** The header still says *"verbatim copies"* and *"the raw .jsonl files are the unedited source of truth"*, but lines 2–3 and the new code say the opposite.
**Recommendation:** Update the header to match whichever option is chosen for the second critical finding.

---

### [MINOR] Operational — The archiver rewrites every archive file on every Claude turn

**File:** `.claude/hooks/save-transcript.js:59-62`
**Description:** `isUpToDate` now always returns `false`. Every Stop hook re-sanitises and rewrites all of the sessions, currently about 4.5 MB, so the working tree is dirty after every turn. The cost grows with each session.
**Recommendation:** Restore the size and mtime check, plus a "redaction rules version" stamp, so older files are rewritten only when the rules change.

---

### [MINOR] Operational — The health-check port is hard-coded

**File:** `src/LearnSphere.Api/Program.cs:19`
**Description:** `--healthcheck` always calls `http://localhost:8080/health`. If `ASPNETCORE_HTTP_PORTS` is changed in compose, the container reports unhealthy while the app works fine.
**Recommendation:** Read `ASPNETCORE_HTTP_PORTS` (first value), defaulting to 8080.

---

### [MINOR] Robustness — A failure in the startup sweep stops the whole host

**File:** `src/LearnSphere.Api/Jobs/GenerationWorker.cs:47-53`
**Description:** The `ExecuteUpdateAsync` that runs the sweep has no try/catch. A transient SQLite lock or I/O error escapes `ExecuteAsync`. By default (`BackgroundServiceExceptionBehavior.StopHost`) that **stops the API**.
**Recommendation:** Wrap it in a try/catch with a log and a short retry loop, then continue into the worker loops.

---

### [MINOR] Testing — Some gaps in the tests

**Files:** `tests/LearnSphere.Api.Tests/*.cs`
**Description:** These are not covered:

- `countTokens` errors other than "unknown" (see the major finding above);
- a Gemini 400/404 mapping to `internal_error`;
- a polling network error in Angular;
- the `Retry` navigation path (`generation.page.ts:62-64`);
- the `409` on DELETE while processing. The Codex log says this was covered by "AdmissionIsBoundedAndProcessingCannotBeDeleted"; it is present, so this one is fine.

Many tests are dense one-liners that assert several behaviours each (e.g. `GenerationTests.cs:128, 133-134`). A failure then pinpoints poorly.
**Recommendation:** Add the missing cases, and split the multi-assert one-liners as part of the formatting work in the `Program.cs` finding.

---

### [MINOR] Consistency — The recorded live sample predates the UTC timestamp fix

**File:** `samples/recorded-gemini-run.json:14-16`
**Description:** `"createdAt": "2026-10-17T13:33:22.0888022"` has no `Z`, but `README.md:112` says UTC markers are now kept. The README also says the finish reasons in the record are *"inferred"* (`README.md:134`). That's honest, but it shows the record was captured before the metadata included `finishReason`.
**Recommendation:** Re-record both runs with the new key once it's rotated, so the sample matches the shipped API shape exactly.

---

### [NOTICE] Plan deviation — `ThrowOnBadRequest = true` instead of `false`

**File:** `src/LearnSphere.Api/Program.cs:26, 50-55`
**Description:** The plan specified `false` and mapping. The implementation throws and converts in `UseExceptionHandler`, which also turns 415 into 400. It works and is covered by tests, but it differs from the plan.
**Recommendation:** Leave it, or add a one-line comment explaining why.

---

### [NOTICE] Plan deviation — Results are stored as JSON value converters, not owned JSON types

**File:** `src/LearnSphere.Api/Persistence/AppDbContext.cs:22-23`
**Description:** This is disclosed in the README, it's reasonable, and it's simpler. There's no `ValueComparer`, but the code only ever replaces whole values, so change tracking works.
**Recommendation:** None. It's a good talking point for the interview.

---

### [NOTICE] Plan deviation — `ThinkingBudget` defaults to `null`, not `0`

**File:** `src/LearnSphere.Api/appsettings.json:5`
**Description:** This is disclosed and justified (model compatibility). Thinking tokens may eat into the output budget on long inputs, but the code reports `provider_truncated` correctly when that happens.
**Recommendation:** None.

---

### [NOTICE] Creative decision — The Mock skips any sentence containing "ignore"

**File:** `src/LearnSphere.Api/Providers/MockLlmClient.cs:16`
**Description:** `!x.Contains("ignore", …)` filters out the sample's planted injection line, so the **Mock** looks injection-resistant. A reviewer may read that as gaming the demo.
**Recommendation:** Remove the filter, or comment it as "keep the demo's injection line out of mock questions". The README must not cite Mock output as evidence of injection resistance.

---

### [NOTICE] Config — The question-count settings can't actually be changed

**File:** `src/LearnSphere.Api/Configuration/Options.cs:40`
**Description:** `QuestionCount` and `OptionsPerQuestion` are configurable, but the validator forces them to 5 and 4, so the options are decorative.
**Recommendation:** Either make them constants or allow a range. Both are fine; the choice just needs to be consistent.

---

### [NOTICE] UX — Retry uses a navigation hack to reload the same route

**File:** `src/LearnSphere.Web/src/app/features/generation/generation.page.ts:62-64`
**Description:** It navigates to `/` with `skipLocationChange` and then on to the new id, because the component reads the id from `snapshot` once. This works.
**Recommendation:** Make `id` a `withComponentInputBinding()` input (already enabled in `app.config.ts`), and drive polling with `toObservable(id)`.

---

### [NOTICE] Security — Scalar and OpenAPI are exposed in every environment, with no auth

**File:** `src/LearnSphere.Api/Program.cs:67`
**Description:** That's fine for a local proof of concept, and compose binds to `127.0.0.1`. It's documented.
**Recommendation:** None, unless the app is ever hosted.

---

### [NOTICE] Housekeeping — `.dockerignore` lists a stale `OP` entry

**File:** `.dockerignore:13`
**Recommendation:** Remove it.

---

## Scope Gaps

| Requirement | Status | Notes |
|---|---|---|
| C# REST API for transcript processing | Done | Async jobs, ProblemDetails, OpenAPI |
| AI summary + MCQ quiz | Done | 5 × 4, validated, one repair; live Gemini verified per the Codex log |
| Web UI showing the end-to-end flow | Done, pending manual QA | No human click-through yet |
| README covering setup, design, trade-offs, AI usage | Partial | Thorough, but the requested sentence isn't in the trade-offs section, and the trade-offs section itself is thin |
| **Complete, unedited AI logs** | **Partial, deliberately** | Sanitised: reasoning stripped, key redacted. Needs the user's decision |
| Public GitHub repo | Missing (deferred by the user) | No remote yet |
| Docker (user requirement) | Done (not re-verified by the reviewer) | Non-root, volume, health check, `.env.example` |
| Mock as default, no key needed | Done | |
| Grounding excerpts | Done | Exact match after normalisation, flagged |
| Transcript cleared once a job finishes | Done | Also on `interrupted` |
| No transport retries | Done | `RemoveAllLoggers`, no resilience handler |
| `insufficient_content` outcome | Done | |
| Real token counting | Done, with bugs | See the major finding above |
| Planned file layout (Endpoints/, RequestValidation, Repository, separate Generation files) | Partial | Collapsed into `Program.cs` and `ModelOutput.cs` |

---

## Creative Decisions

- **The admission semaphore** (`GenerationSignal.Admission`) serialises the pending-count check and the insert. That's correct for a single process. The README states that multi-host is unsupported.
- **Middleware-level 413 and content-type short-circuit** (`Program.cs:57-65`), added to work around endpoint selection triggered by `Accepts<>` metadata.
- **`UseStatusCodePages` turns every body-less status into ProblemDetails**, including non-API responses.
- **`--healthcheck` CLI mode** instead of `curl` in the image. It's clever, and it follows the plan.
- **Mock model name `deterministic-v1`** in the metadata.
- **The generation page labels live output "Generated with Gemini"**, hard-coded rather than taken from `metadata.provider`.
- **`DELETE` returns 409 while a job is Processing.** This was in the plan; worth noting as a UX choice.
- **The archiver now pulls in `scripts/archive-safety.cjs`**, which couples the Claude hook to a repo script. If the script moves, the hook breaks silently, because the error is caught and only goes to stderr.

---

## Summary Table

| # | Severity | Category | File | One-line description |
|---|---|---|---|---|
| 1 | critical | Security | (Codex session store) | Live Gemini key was pasted into chat and hasn't been rotated |
| 2 | critical | Scope | `.claude/hooks/save-transcript.js`, `scripts/archive-safety.cjs` | Committed logs strip reasoning and redact, so they're not "unedited" as the brief requires |
| 3 | major | Correctness | `GeminiLlmClient.cs:29`, `TrainingContentService.cs:28` | A countTokens failure (auth, 429, outage) is reported as `input_too_large` for transcripts over ~23k chars |
| 4 | major | Code quality | `Program.cs:17-119` | All endpoints inline in dense one-liners; planned files missing |
| 5 | major | Plan fidelity | `README.md:116,161` | User's trade-off sentence isn't in the trade-offs section |
| 6 | major | Scope | `README.md:159` | No manual browser QA; no GitHub remote |
| 7 | minor | Correctness | `generation.page.ts:26-31` | Polling dies permanently after one failed GET |
| 8 | minor | Observability | `TrainingContentService.cs:56-58` | Gemini 400/404 become `internal_error` with no log |
| 9 | minor | Code quality | `Program.cs:62,71` | Content-type check duplicated |
| 10 | minor | Code quality | `Llm.cs:15-18` | `RetryAfter` parsed but unused |
| 11 | minor | Consistency | `save-transcript.js:5-12` | Comments still say "verbatim / unedited" |
| 12 | minor | Operational | `save-transcript.js:59-62` | Every archive rewritten on every turn |
| 13 | minor | Operational | `Program.cs:19` | Health-check port hard-coded to 8080 |
| 14 | minor | Robustness | `GenerationWorker.cs:47-53` | A startup-sweep exception stops the host |
| 15 | minor | Testing | `tests/*` | Missing countTokens-error, 400-mapping, polling-error and Retry tests; dense multi-assert tests |
| 16 | minor | Consistency | `samples/recorded-gemini-run.json:14` | Recorded sample predates the UTC fix and finish-reason metadata |
| 17 | notice | Plan deviation | `Program.cs:26` | `ThrowOnBadRequest = true` instead of `false` |
| 18 | notice | Plan deviation | `AppDbContext.cs:22-23` | JSON value converters instead of owned types (disclosed) |
| 19 | notice | Plan deviation | `appsettings.json:5` | `ThinkingBudget` null instead of 0 (disclosed) |
| 20 | notice | Creative | `MockLlmClient.cs:16` | Mock filters out "ignore" sentences; looks like gaming the demo |
| 21 | notice | Config | `Options.cs:40` | Question-count settings are forced to 5 and 4 |
| 22 | notice | UX | `generation.page.ts:62-64` | Retry reloads the route through a navigation hack |
| 23 | notice | Housekeeping | `.dockerignore:13` | Stale `OP` entry |

---

## Recommended Actions Before Merge / Submission

1. **Revoke the Gemini key now** and create a new one, stored only in user-secrets or `.env`. Never paste it into a chat again.
2. **Decide the log policy** (critical finding 2: A, B or C), then adjust `archive-safety.cjs`, the hook comments, `AI-LOGS.md` and the README. Run `node .claude/hooks/save-transcript.js` as the very last step.
3. **Fix the countTokens error masking:** propagate auth, 429 and outage errors, use a realistic fallback, and add the tests.
4. **Move the "No automatic retries…" sentence** into a real Trade-offs list in the README.
5. **Restructure `Program.cs`** into `Endpoints/` and validation files, and reformat the dense code and tests (if time allows; strongly recommended for the interview).
6. Fix the polling resilience and add logging for provider `BadRequest`.
7. Smaller items: the health-check port, a try/catch around the startup sweep, the `RetryAfter` field, the stale comments, archive rewrite efficiency, `.dockerignore`, and the Mock "ignore" filter.
8. **Re-record `samples/recorded-gemini-run.json`** with the new key.
9. **Manual browser QA** (desktop and mobile, Mock and live) through `docker compose up --build -d --wait`.
10. Create the GitHub repo, push, fresh-clone from GitHub, and follow the README verbatim.
