# Priority User Journeys (Prototype Targets)

Use these five journeys for clickable prototypes and acceptance checks. Realistic Ghanaian sample data. Implementation slices that gate on these journeys: [needlebase-phases](../needlebase-phases/phases.md).

**Persona defaults:** Ama Owusu (Owner, Ama Stitches, Accra). Staff: Kofi Asante (Tailor). Customer: Efua Mensah, +233 24 555 0123.

---

## Journey 1 — Happy path (register → deposit → status)

**Goal:** Register business → Add customer → Record measurements → Create order → Record deposit → View status

| Step | Screen | Success criteria |
|------|--------|------------------|
| 1 | Welcome → Sign up → Business profile | Business name shown on Home |
| 2 | Offline explainer → Trial | User understands local save |
| 3 | Add customer Efua Mensah | Appears in Customers |
| 4 | Measurements: dress template | Values saved; history entry |
| 5 | New Order wizard | Order ref created; measurement snapshot linked |
| 6 | Record deposit GH₵200 of GH₵800 | Balance outstanding GH₵600; receipt available |
| 7 | Order detail | Status New (or next); payment summary correct |

**Prototype notes:** End-to-end mobile primary; web can mirror order detail.

---

## Journey 2 — Offline order → reopen → sync

**Goal:** Create order offline → Close/reopen → Data remains → Reconnect → Sync without duplicates

| Step | Screen | Success criteria |
|------|--------|------------------|
| 1 | Set Offline (chip) | Chip shows Offline |
| 2 | New Order + save | Toast: “Saved on this device” |
| 3 | Kill/reopen app | Order still in list with pending badge |
| 4 | Go online → Sync | Syncing → Synced; **one** order only |
| 5 | Sync details | Pending empty; no duplicate refs |

**Must not:** Claim cloud save while offline; duplicate on sync.

---

## Journey 3 — Production + staff + web oversight

**Goal:** Update production status → Assign staff → See progress on web dashboard

| Step | Screen | Success criteria |
|------|--------|------------------|
| 1 | Mobile order detail → status Sewing | Chip updates |
| 2 | Assign Kofi Asante | Assignee visible |
| 3 | Web Production board | Card in Sewing; assignee shown |
| 4 | Web Dashboard | Workload / activity reflects change |

**Roles:** Owner/Manager assigns; Tailor may only update own assigned items.

---

## Journey 4 — Additional payment → balance → receipt

**Goal:** Record additional payment → Outstanding updates → Generate receipt

| Step | Screen | Success criteria |
|------|--------|------------------|
| 1 | Payments → Record GH₵300 on order | Method e.g. MoMo |
| 2 | Order / customer balance | Outstanding GH₵300 (from prior 600) |
| 3 | Receipt preview | Totals, method, business header |
| 4 | Share/Print | Success feedback |
| 5 | If offline | Receipt notes recorded on device / pending sync |

---

## Journey 5 — Invite employee → role → limited UI

**Goal:** Invite employee → Assign role → Employee sees only permitted functions

| Step | Screen | Success criteria |
|------|--------|------------------|
| 1 | Owner: Staff → Invite Kofi, role Tailor | Plain permission summary shown |
| 2 | Kofi accepts invite | Lands in Ama Stitches workspace |
| 3 | Kofi nav | No Staff, no Subscription admin; Payments hidden (default) |
| 4 | Kofi orders | Sees assigned work; cannot change roles |
| 5 | Owner web Staff | Status Accepted; assignments work |

**Copy:** Role descriptions in non-technical language.

---

## Prototype checklist (all journeys)

- [ ] SyncChip visible and accurate  
- [ ] Empty/loading/error states exist on touched flows  
- [ ] Status = colour + text + icon  
- [ ] GHS formatting consistent  
- [ ] No invented backend features  
- [ ] Solo path never forces staff setup (J1)  
