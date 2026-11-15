# Legal documents (internal)

These notes are for the operator and for people maintaining NeedleBase. They are **not** shown on the website or in the app.

## What is published

- Privacy Policy: `privacy-policy.md`, web `/Privacy`, MAUI `PrivacyPage`. Keep all three in sync.
- Terms and Conditions: `terms-and-conditions.md`, web `/Terms`, MAUI `TermsPage`. Keep all three in sync.

User-facing copies should not contain `[TO CONFIRM]` markers or a "Draft for review / not legal advice" banner.

Account deletion is **built** (13 November 2026). In-app path: More → Your account → Delete account (password + typed confirmation). Public web page: `/account/delete` (linked from the landing footer, signed-in footer, auth layout, Privacy Policy, and the Change password page). Staff deletion removes only that sign-in; an Owner deletion closes the shop, signs everyone out, and purges hosted shop records immediately (activity log kept up to 730 days). People who cannot sign in can still request deletion by email or WhatsApp, normally actioned within 30 days. The published Privacy Policy and Terms describe this. Keep those copies in sync if the behaviour changes.

## Still outstanding (cannot be fixed by the documents alone)

1. **Lawyer review.** A qualified Ghanaian lawyer should still review both documents before they are treated as final legal advice. The published pages no longer carry that banner.
2. **Data Protection Commission registration.** HelmutCode Solutions is not registered. Act 843 requires a data controller who intends to process personal data to register (sections 27, 46 and 53). The published documents state the fact honestly and do not promise registration. Register at https://app.dpc.gov.gh/ before relying on production processing; then update the factual status line in both documents.
3. **One-year purge is not automatic.** Shop data is kept for one year after a shop simply stops using NeedleBase (if no deletion request). Nothing in the product deletes those shops when the year is up, so that promise still depends on doing it by hand until a purge worker is built.
4. **Company registration number** is omitted because it has not been provided. Add it to the Terms if one is confirmed.

## Pending wording (do not edit the published pages until asked)

Two commercial decisions now clash with the published Terms. Record them here; do not rewrite `privacy-policy.md`, `terms-and-conditions.md`, or the Razor and MAUI copies until Helmut asks:

- The Terms say fees are whatever was agreed when the licence key was issued. Planned pricing says that when Starter's launch price rises, existing shops move to the new price after 30 days' notice. The Terms will need a price-change clause.
- The Terms say no export tool is promised. Export is now planned for the Free "Save a copy" action, for paid plans, and for a shop that has stopped paying and is read-only.

Planned prices themselves are decided (see the README section "Pricing (decided, not built)") but are **not built** and are **not** yet the prices on the public landing page. Until the landing page is updated, treat the figures on that page as samples. The "Website prices are samples until confirmed in writing" line in the Terms still matches what the landing page shows today.

## Contacts and facts already confirmed

- Legal operator: HelmutCode Solutions (brand: HelmutCode). Address: YN13 Flask Street, Ghana.
- Email: helmutessien@gmail.com. WhatsApp: +233 50 078 5153 (also the data-protection contact; no named Data Protection Supervisor is claimed).
- Hosting: MonsterASP.NET in Europe (needlebase.runasp.net). Email: Gmail SMTP or Resend. WhatsApp notices: Meta.
- Licence keys are issued off the app after WhatsApp or email contact. The public landing page still shows sample prices until it is updated. No in-app billing. Effective date of the published documents: 1 January 2027; do not present them as already in force before that date.
- Shop data on the hosted service is kept one year after a shop stops using NeedleBase (if no deletion request), then deleted. Audit log default: 730 days. Verified deletion or copy requests: normally within 30 days.
- Minimum age for shop accounts: 18.
