# Offline, Sync, Conflict & Subscription UX

Highest priority: reliable offline operation with honest messaging.

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

## Subscription & entitlement screens

NeedleBase accounts are independent (no central platform admin required).

### Screens / surfaces

| Surface | Content |
|---------|---------|
| Trial / activation | What’s included, end date, CTA continue |
| Plan & status | Current plan, status, renewal date, usage limits if any |
| Expired / suspended | What still works vs locked; Upgrade / Renew |
| Connectivity during check | “Couldn’t verify plan — using last known access” (temporary) |
| Upgrade / renew | Clear actions; return to work after success |

### Behaviour rules

1. Subscription UI must **not** dominate everyday Home/Dashboard.  
2. Must **not** prevent access to **login**.  
3. When expired: clearly list **still available** (e.g. view local data, record offline drafts per policy) vs **restricted** (e.g. invite staff, sync — follow product rules; do not invent).  
4. Soft banner or Settings badge > full-screen block for daily work when possible.

### Suggested expired banner copy

> Your trial ended on 12 Oct 2026. You can still view records on this device. Renew to sync and add staff.

(Adjust capabilities to real product rules when implemented.)

---

## Sample sync chip copy (short)

- Synced  
- Offline  
- 3 to sync  
- Syncing…  
- Couldn’t sync  
- Needs review  
- Plan unknown  
