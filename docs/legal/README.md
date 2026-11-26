# Legal documents (internal)

These notes are for the operator and for people maintaining NeedleBase. They are **not** shown on the website or in the app.

## What is published

- Privacy Policy: `privacy-policy.md`, web `/Privacy`, MAUI `PrivacyPage`. Keep all three in sync.
- Terms and Conditions: `terms-and-conditions.md`, web `/Terms`, MAUI `TermsPage`. Keep all three in sync.

User-facing copies should not contain `[TO CONFIRM]` markers or a "Draft for review / not legal advice" banner.

## Account deletion (built, 13 November 2026)

- In the app: **More → Your account → Delete account** (password and a typed confirmation).
- On the web: `/account/delete` (linked from the landing footer, the signed-in footer, the auth layout, the Privacy Policy, and the Change password page).
- A staff delete removes only that sign-in.
- An Owner delete closes the shop, signs everyone out, and purges hosted shop records immediately (the activity log is kept for up to 730 days).
- People who cannot sign in can still request deletion by email or WhatsApp, normally actioned within 30 days.
- The published Privacy Policy and Terms describe this. Keep those copies in sync if the behaviour changes.

## Still outstanding (cannot be fixed by the documents alone)

1. **Lawyer review.** A qualified Ghanaian lawyer should still review both documents before they are treated as final legal advice. The published pages no longer carry that banner.
2. **Data Protection Commission registration.** HelmutCode Solutions is not registered. Act 843 requires a data controller who intends to process personal data to register (sections 27, 46 and 53). The published documents state the fact honestly and do not promise registration. Register at https://app.dpc.gov.gh/ before relying on production processing; then update the factual status line in both documents.
3. **One-year purge is not automatic.** Shop data is kept for one year after a shop simply stops using NeedleBase (if no deletion request). Nothing in the product deletes those shops when the year is up, so that promise still depends on doing it by hand until a purge worker is built.
4. **Company registration number** is omitted because it has not been provided. Add it to the Terms if one is confirmed.

## Terms and landing prices (plan prices 18 November 2026; customer email 26 November 2026)

The published Terms (`terms-and-conditions.md`, web `/Terms`, and MAUI `TermsPage`) and the public landing-page prices were updated on 18 November 2026 to match the decided plans. Keep the three Terms copies in sync. Do not change those public prices or the published Terms again unless asked.

The Terms include the published plan prices (excluding VAT), a 30-day notice before a price change, Free-plan rules (one phone, 10 customers and 20 orders ever created, no NeedleBase sync or backup, "Save a copy"), export for paid and lapsed read-only shops, and one-year hosted retention after a paid shop lapses. Complimentary or specially agreed keys can still be agreed in writing.

**Customer email (26 November 2026):** Privacy Policy and Terms (all three copies each) describe optional customer email, a separate email opt-in, transactional order-status and payment-receipt emails (receipt may include a PDF attachment generated on send, not stored in blob), and that this is not marketing. Keep `docs/legal/`, web `/Privacy` `/Terms`, and MAUI Privacy/Terms pages in sync.

**Still pending:** the Play Store privacy form. Free phone-only mode, "Save a copy", paid export, usage meters, upgrading from Free, paid-plan emails, PDF receipts (phone share) and customer email channel are built. Workshop step 7: photos (Cloudflare R2) and profit/monthly reports **not built**.

## Contacts and facts already confirmed

- Legal operator: HelmutCode Solutions (brand: HelmutCode). Address: YN13 Flask Street, Ghana.
- Email: helmutessien@gmail.com. WhatsApp: +233 50 078 5153 (also the data-protection contact; no named Data Protection Supervisor is claimed).
- Hosting: MonsterASP.NET in Europe (needlebase.runasp.net). Email: Gmail SMTP or Resend. WhatsApp notices: Meta.
- Licence keys are issued off the app after WhatsApp or email contact. The public landing page and the Terms show the published plan prices (excluding VAT). No in-app billing. Effective date of the published documents: 1 January 2027; do not present them as already in force before that date. Terms last updated: 26 November 2026. Privacy Policy last updated: 26 November 2026.
- Shop data on the hosted service is kept one year after a shop stops using NeedleBase (if no deletion request), then deleted. Audit log default: 730 days. Verified deletion or copy requests: normally within 30 days.
- Minimum age for shop accounts: 18.
