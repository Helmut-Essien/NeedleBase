# Offline, Sync, Conflict & Subscription UX

Highest priority: reliable offline operation with honest messaging.

**Status:** built in Slice 4 [code]. Home shows the SyncChip (`Mobile/Views/HomePage.xaml`, `ViewModels/SyncStatusViewModel.cs`): Synced · "N to sync"/Waiting to sync · Syncing… · Offline · Couldn't sync · "N need review" · Sign in to sync. Tap opens `SyncDetailsPage` (connection, last sync, Sync now, waiting items, failure reasons, conflicts with **Keep mine / Keep theirs**; "Delete mine" when the server copy was deleted). Sign out warns when changes are unsynced. Also: **No access** (403, "This account can't sync this business · saved on this device"), **Sync stopped** (server rejected the request, "Update the app, then tap Sync now"), **Sign in** button on Sync details (re-sign-in keeps local data for the same account). Row actions: Restore / Delete here, Restore customer / Discard (customer deleted elsewhere), Restore customer (delete refused: "Customer has orders on another device, so it wasn't deleted."), **Discard** on failed items. Unsynced new orders note: "Its order number is confirmed when it syncs." (the pre-sync #ref is provisional). Don't show a chip state the app can't truthfully compute. Canonical local-save wording: **"Saved on this device"** (needlebase-mobile-sync rule 9).

## Sync states (required)

| State | Indicator (chip) | User-facing meaning |
|-------|------------------|---------------------|
| Online & synchronised | Cloud check + “Synced” | Local matches server |
| Offline | Cloud off + “Offline” | Local save available; no network |
| Changes waiting | Cloud upload + “Waiting to sync” / count | Pending queue on device |
| Sync in progress | Spinner + “Syncing…” | Do not navigate away mid-critical if avoidable |
| Sync successful | Brief “Synced” toast/chip | Then return to synchronised |
| Sync failed | Alert + “Couldn’t sync” | Retry available |
| Conflicts need attention | Warning + “Needs review” | Open conflict UI — never silent overwrite |
| Entitlement check unavailable | Info + “Plan status unknown” | Temporary; do not lock out login |

### Placement

- **Mobile:** top of Home (and shell where possible); always on Sync details.
- **Web:** top bar SyncChip.
- Unobtrusive; escalate only on fail/conflict.

### After offline save

Explicit confirmation, e.g.:

> Saved on this device. Will sync when you’re online.

**Never:** “Saved to cloud”, “Payment confirmed with provider” for local-only records.

### Payments honesty

| Record type | UI language |
|-------------|-------------|
| Cash / local payment offline | “Payment recorded on this device” + pending sync badge |
| Synced local payment | “Payment recorded” (method shown) |
| Online provider (if ever) | Separate label; only “confirmed” after provider + sync success |

Unsynchronised payment must **not** look like provider-settled.

---

## Sync details screen

Contents:

1. Current connectivity + last successful sync time  
2. Pending changes list (entity type, name/ref, action: create/update)  
3. **Sync now** / **Retry**  
4. Failed items with error in plain language  
5. Conflicts entry point  
6. Link to diagnostics (Settings)

Mobile: full screen from chip or More → Sync.  
Web: drawer or settings page section.

---

## Conflict resolution

**Plain language.** Never silently overwrite payments or financially important records.

Pattern:

1. Title: “These changes conflict”  
2. Show **Your device** vs **Server** (or Staff A vs Staff B) for the field/record  
3. For **payments / balances**: force explicit choice or “Keep both and flag for review” — no auto-merge that drops money  
4. For non-financial: allow “Keep mine” / “Keep theirs” / field-level where simple  
5. Confirm → resolve → sync  

Copy example:

> Two updates were made to this payment while offline. Choose which to keep. NeedleBase will not discard a payment without your choice.

---

## Platform expectations

| Platform | Requirement |
|----------|-------------|
| Mobile MVP | Full offline: customers, measurements, orders, payments recordable offline |
| Web | Clear local-save + sync states; protect unsynchronised edits; **do not claim full offline parity** unless implemented |

Protect unsaved web edits: beforeunload / “You have unsaved changes”; if local draft exists, show pending badge.

---

## Empty / loading / error / recovery (every major flow)

| Flow | Empty | Loading | Error | Recovery |
|------|-------|---------|-------|----------|
| Lists | CTA to create first item | Skeletons | “Couldn’t load” + Retry | Retry; offline cached list if available |
| Forms | — | Disable submit + progress | Field + form errors | Fix & resubmit; offline save path |
| Sync | “Nothing pending” | Syncing list | Failed items | Retry / open conflict |
| Receipt | — | Preview skeleton | Share failed | Retry share/print |

---

## Subscription & entitlement (planned S8)

Surfaces: trial/activation · plan & status · expired/suspended (what still works vs locked) · "Couldn't verify plan — using last known access" · upgrade/renew → back to work.

Rules: never dominates Home/Dashboard · **never blocks login** · when expired, list *still available* vs *restricted* from real product rules (don't invent) · prefer a soft banner/Settings badge over a full-screen block. Example: "Your trial ended on 12 Oct 2026. You can still view records on this device. Renew to sync and add staff."

## Sample sync chip copy (short)

- Synced  
- Offline  
- 3 to sync  
- Syncing…  
- Couldn’t sync  
- Needs review  
- Plan unknown  
