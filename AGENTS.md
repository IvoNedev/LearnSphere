# Project conversation history

- Store user-facing conversations for this project in `Context/Codex/`.
- At the start of each session, read `Context/Codex/README.md` and relevant recent conversation logs for continuity.
- Create a Markdown log for each conversation, named `YYYY-MM-DD-topic.md`; add a numeric suffix if needed to avoid overwriting another conversation.
- Record user messages and assistant replies available in the session, in order, and update the log before ending each turn. Include meaningful progress updates and summarize tool actions and results when useful.
- Preserve existing history. Clearly distinguish verbatim messages from summaries; never invent unavailable messages or claim an incomplete archive is complete.
- Do not record hidden reasoning, credentials, tokens, or other secrets. Redact secrets if they appear in user-facing messages.
- Older conversations outside the available session must be supplied or exported by the user before they can be archived.
