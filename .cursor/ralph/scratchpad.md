---
iteration: 3
max_iterations: 12
completion_promise: "ALL_REVIEWS_CLEAN"
---

Run bugbot, security review, and UI/UX review subagents on this slice and then plan and implement all issues that arise in the review and then run bugbot, security review, and UI/UX review again and plan and implement all issues that arise in the review. Do this continuously in a ralph loop until there are no issues.

Review suite every iteration: Bugbot, Security Review, Senior Developer Review, UI/UX Review. Verify findings, plan, implement, update REVIEW_LOG.md, repeat until all four are clean. Completion when ALL_REVIEWS_CLEAN.

STATUS: ALL_REVIEWS_CLEAN after iteration 3.
