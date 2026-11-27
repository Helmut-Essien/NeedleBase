# Priority journeys (design-time + acceptance)

Five end-to-end journeys that gate slice acceptance (J6 gates plan step 7b-2, expenses). Use them for prototypes and as device or browser checks.

Five journeys gate the slices in [phases.md](../needlebase-phases/phases.md). Use them for prototypes and as end-to-end acceptance checks. Sample data: **Ama Owusu** (Owner, *Ama Stitches*, Accra) · **Kofi Asante** (Tailor) · **Efua Mensah**, +233 24 555 0123.

| # | Journey | Steps | Must pass | Slice | Status |
|---|---------|-------|-----------|-------|--------|
| J1 | Register → deposit → status | Sign up + business → add Efua → dress measurements → New Order → deposit GH₵200 of GH₵800 → order detail | Business name on Home; customer + measurement saved; order ref with snapshot; balance GH₵600; status shown | S1–S3, S5 | Steps 1–7 built; deposit recorded from Order detail (Slice 5); device run pending |
| J2 | Offline order → reopen → sync | Offline → New Order → kill + reopen → online → sync | Toast "Saved on this device"; pending badge survives restart; **exactly one** order after sync; Sync details empty | S4 | Pass on one device 26 Nov 2026; two-device run pending (code-gaps G15h). Open receipt/order detail now reload when a sync pass applies (G15f) |
| J3 | Production + staff + web | Mobile status → Sewing; assign Kofi; web board + dashboard | Chip updates; assignee visible on web; Tailor edits only assigned items | S3, S6, S7 | PASS 26 Nov 2026 (after code-gaps G14: assign on phone + web, no DB edits) |
| J4 | Extra payment → receipt | Record GH₵300 MoMo → balance → receipt → share | Outstanding GH₵300; receipt totals + method + business header; offline = "recorded on device" | S5 | Pass 26 Nov 2026 (emulator + web) |
| J5 | Invite → role → limited UI | Owner invites Kofi as Tailor → accept → Kofi's nav | Plain role summary; Kofi sees no Staff or License page, Payments hidden, assigned work only; web shows Accepted | S7 | Pass 26 Nov 2026 (Tailor Home crash fixed, code-gaps G13) |
| J6 | Offline expense → sync → profit | Ama (Workshop) offline → More → Money → Expenses → Record GH₵120 Fabric and materials, linked to Efua's order → reopen app → online → sync → web `/Expenses` + `/Reports`; Kofi (Tailor) checks his nav | "Saved on this device" + "Not synced yet" survive restart; **exactly one** expense after sync; web shows it on the order and in the month; Profit = money in − GH₵120 on phone and web; void on web → phone shows Voided after sync and profit goes back up; Kofi sees no Expenses anywhere; Starter shop sees the upgrade message | Plan 7b-2 | Planned — [expenses-plan.md](../needlebase-plans/expenses-plan.md) |

**Every journey:** SyncChip accurate (from S4) · empty/loading/error states on touched screens · status = colour + text + icon · GH₵/date/phone per [content-and-states.md](content-and-states.md) · no invented backend features · solo path never forces staff setup.
