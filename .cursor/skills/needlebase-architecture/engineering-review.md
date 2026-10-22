# Engineering review (before any code change)

Act as a senior software engineer reviewing your own work before making changes.

Before writing or modifying any code:

1. Inspect the existing implementation. Understand the current architecture, event flow, dependencies, and failure points.

2. Check for unnecessary polling. If the system repeatedly checks whether something has happened, determine whether event-driven mechanisms such as webhooks, event listeners, subscriptions, or callbacks are appropriate. Use the option that fits the existing architecture. Do not replace polling blindly when it is necessary.

3. *Fix the root cause.* Do not create a second implementation to work around a broken first implementation. Identify why the existing code fails and correct it.

4. Remove obsolete code. If the fix makes existing functions, duplicate handlers, outdated logic, or workarounds unnecessary, remove them. Do not leave version 1 sitting beside version 2.

5. Check for regressions. Review imports, dependencies, callers, state changes, error handling, and existing tests. Update or add tests where needed.

6. Keep the solution minimal. Prefer a targeted fix over a rewrite. Do not introduce new abstractions or dependencies without a clear reason.

7. Verify the result. Run the relevant tests and checks. If you cannot run them, state what remains unverified.

8. Mobile data/list changes: a new write path bumps `LocalDataVersion`; lists keep the cache rules; nothing heavy on the UI thread; session caches clear in `ClearTokensAsync` ([lists-and-local-data.md](../needlebase-mobile/lists-and-local-data.md)). Errors are typed codes, not strings ([needlebase-api](../needlebase-api/SKILL.md) rule 4).

Before finishing, report:
- The root cause of the problem.
- What you changed and why.
- What obsolete code you removed.
- Any remaining risks or unverified assumptions.

Important: Do not start coding until you have inspected the existing implementation and identified the likely root cause. Never claim a fix works unless you have evidence to support that claim.
