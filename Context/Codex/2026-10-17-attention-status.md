# Remaining review attention — 2026-10-17

## User request (summary)

The user supplied an eleven-item external review and requested a status report after the recent fixes. It covers archive fidelity, code structure, public Task-folder scope, exposed-key revocation, token-count errors, README trade-offs, polling/provider/health/startup/archive/retry-delay issues, mock filtering, live recordings, model configuration, browser QA, and GitHub publication. The supplied review proposes archive-policy alternatives; no alternative was selected in this request.

## Assistant progress (verbatim)

I’ll check each item against the current code and recent fixes, then give you a clear list of what’s resolved, what remains, and what needs your decision. I won’t revoke keys, change the archive policy, or decide whether `Task/` is public without your instruction.

Several items are fixed, but the archive behavior, code structure, README trade-offs, and mock’s “ignore” filter still need attention. Polling now has a bounded counter and manual recovery; rate-limit delays are shown to the user.

The newer chunked Gemini recording captures UTC timestamps and actual finish reasons. The older single-call recording still has the limitations listed in the review, and key revocation remains unconfirmed.

## Inspection and outcome (summary)

Read project archive instructions, archive index, recent fix notes, README, and relevant code. No application changes or account/publication actions were made in this status-check turn. No tests rerun; previous recorded verification is 96 backend and 16 Angular tests passing.

Still needs attention: archive compliance/policy and malformed-line handling; dense Program.cs structure; Task publication scope (still untracked); exposed-key revocation unconfirmed; README trade-off placement/content; fixed-port health probe if custom internal ports are supported; startup database failure recovery policy; archive unconditional refresh and stale verbatim comments; Mock's ignore filter; updated single-call recording with a replacement key; desktop/mobile browser QA. GitHub remains deferred at the user's request.

Resolved by preceding fixes: explicit token-count/provider error categories, finite non-overlapping polling with manual restart, and visible retry-after information. Polling pauses after request errors rather than automatically retrying. The model is configuration-driven; README uses an account-tested example and notes availability differences. A newer chunked live recording has actual finish reasons and UTC timestamps; the older single-call artifact remains historical.

Archive publication copies remain sanitized, original platform session files remain untouched, and internal reasoning is excluded per AGENTS.md. A revoked credential should still be redacted from published copies. No credentials are included in this note.
