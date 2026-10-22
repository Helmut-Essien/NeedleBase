# Priority journeys (design-time + acceptance)

Five journeys gate the slices in [phases.md](../needlebase-phases/phases.md). Use them for prototypes and as end-to-end acceptance checks. Sample data: **Ama Owusu** (Owner, *Ama Stitches*, Accra) · **Kofi Asante** (Tailor) · **Efua Mensah**, +233 24 555 0123.

| # | Journey | Steps | Must pass | Slice | Status |
|---|---------|-------|-----------|-------|--------|
| J1 | Register → deposit → status | Sign up + business → add Efua → dress measurements → New Order → deposit GH₵200 of GH₵800 → order detail | Business name on Home; customer + measurement saved; order ref with snapshot; balance GH₵600; status shown | S1–S3, S5 | Steps 1–7 built; deposit recorded from Order detail (Slice 5); device run pending |
| J2 | Offline order → reopen → sync | Offline → New Order → kill + reopen → online → sync | Toast "Saved on this device"; pending badge survives restart; **exactly one** order after sync; Sync details empty | S4 | Code + tests built; device run pending |
| J3 | Production + staff + web | Mobile status → Sewing; assign Kofi; web board + dashboard | Chip updates; assignee visible on web; Tailor edits only assigned items | S3, S6, S7 | Planned |
| J4 | Extra payment → receipt | Record GH₵300 MoMo → balance → receipt → share | Outstanding GH₵300; receipt totals + method + business header; offline = "recorded on device" | S5 | Planned |
| J5 | Invite → role → limited UI | Owner invites Kofi as Tailor → accept → Kofi's nav | Plain role summary; Kofi sees no Staff/Subscription, Payments hidden, assigned work only; web shows Accepted | S7 | Planned |

**Every journey:** SyncChip accurate (from S4) · empty/loading/error states on touched screens · status = colour + text + icon · GH₵/date/phone per [content-and-states.md](content-and-states.md) · no invented backend features · solo path never forces staff setup.
