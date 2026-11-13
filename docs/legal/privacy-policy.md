# NeedleBase Privacy Policy

**Operator:** HelmutCode Solutions ("**HelmutCode**", "**we**"), operator of NeedleBase.

**Registered/business address:** YN13 Flask Street, Ghana

**Contact / data-protection contact:** HelmutCode Solutions, by email [helmutessien@gmail.com](mailto:helmutessien@gmail.com) or WhatsApp [+233 50 078 5153](https://wa.me/233500785153)

**Effective date:** 1 January 2027

**Last updated:** 13 November 2026

NeedleBase is an offline-first app for tailors, seamstresses, dressmakers and fashion workshop owners in Ghana and West Africa. This Policy explains, in plain language, what personal data is involved, why it is used, and the rights people have under Ghana's Data Protection Act, 2012 (Act 843).

## 1. Who decides what happens to the data

- A **workshop** (the Business, and its Owner) decides why customer and staff information is entered into NeedleBase. For that information, the workshop is the **data controller** under Act 843.
- **HelmutCode Solutions / NeedleBase is mainly a data processor / service provider**: we host, store and sync that information on the workshop's instructions (see Act 843, sections 29 and 30). That arrangement is also set out in the Terms and Conditions.
- HelmutCode Solutions is a **data controller** only for a narrower set of data we decide about ourselves: shop account registration, owner and staff sign-in, licence checks, support messages you send us, and keeping the service secure.

## 2. What information NeedleBase handles

This list comes from how the product is built today. We do not invent extra types of data.

- **Account and staff data:** first name, surname, email address, phone number (if provided), role (Owner, Manager or Tailor), shop name and shop phone, and sign-in details. Passwords are stored only as a one-way hash (they are not visible to other users or in readable form). The system also keeps sign-in session tokens (hashed), and, when someone asks to reset a password, a short-lived hashed reset code.
- **Licence data:** the shop's licence key is stored in a protected (encrypted) form, together with a short hint, plan or trial flags, expiry and check times. Licence status may be checked with NeedleBase's licensing service (a separate system the operator configures). A device identifier may be sent as part of that check.
- **Customer records the workshop enters:** name, phone number, and optional notes. Body **measurements** (for a garment, in the unit the shop uses, such as centimetres) are personal data because they belong to a named customer. A workshop should only record measurements needed for an order, and should not put health or other sensitive information in notes or measurement fields.
- **Photo reference:** the customer record includes an optional "photo reference" field in the database, and that field can be synced. The current app screens do **not** include a way to take or upload a customer photo. If that is added later, this Policy will be updated.
- **Orders:** a short order reference, garment type, quantities and prices, due and fitting dates, progress (new, cutting, sewing, fitting, ready, collected), notes, and which staff member is assigned, where linked to a customer.
- **Payment records:** amounts in Ghana cedis (GHS), the method recorded (cash, mobile money / MoMo, bank transfer, or other), dates, notes, and the order they belong to. NeedleBase only records that the workshop says a payment happened. It does not receive, move or guarantee money, and it does not store MoMo PINs or bank passwords. Do not type anyone's PIN or full bank credentials into NeedleBase.
- **Staff invitations:** an invite can store the person's email, name and role, plus a hashed one-time code (valid for seven days). The owner can share the code in person or by WhatsApp. If email sending is configured (Gmail SMTP or Resend), an invite may also be emailed.
- **WhatsApp opt-in:** if the workshop uses WhatsApp notices, it can record whether a customer has opted in or out, who recorded that, and when.
- **Activity log (audit):** the server keeps an append-only log of important actions (for example sign-in or a change to an order). An entry may include the user, role, a short summary, limited field changes with sensitive values masked, a device id, an IP address, and the browser or app "user agent". This log is not synced down to phones. Entries older than **730 days** (two years; this can be configured, but not below 30 days) are deleted automatically.
- **Technical data:** a device identifier used when the phone syncs; and ordinary server logs used to run and secure the service (a sign-in log line may include an email address). We did not find a separate advertising, analytics, crash-reporting or push-notification product in the current app.

## 3. Why the data is used (Act 843)

Act 843 requires that personal data is used lawfully, for a clear purpose, kept reasonably accurate, not excessive, and protected. In short, the workshop and HelmutCode Solutions use the information above to: create and protect accounts; keep customer, measurement, order and payment records; check a shop licence; sync the phone with the shop; send a password-reset or invite email when email is configured (via Gmail SMTP or Resend); send a WhatsApp order notice when the workshop has turned that on and the customer has opted in; answer support requests; and keep the service safe.

The main legal grounds are: performing the workshop's contract with its customer (and our contract with the workshop); a legitimate interest in running and securing a workshop system; a legal duty where one applies; and consent where it has been given (for example a WhatsApp opt-in, or a support message you choose to send us). A person may object to processing under section 20 of the Act. The workshop, as controller, must handle objections that relate to its customers.

## 4. Stored on the phone and synced to the server

NeedleBase is offline-first. Customer, measurement, order, payment and related records may be saved first in a local database on the phone (SQLite, not encrypted at rest by the app). When the phone is online, it syncs to the NeedleBase API and a PostgreSQL database. So a customer's information may exist both on the workshop's device and on the hosted service.

The website, API and database are hosted with **MonsterASP.NET** in **Europe**, and the public site is at [https://needlebase.runasp.net/](https://needlebase.runasp.net/). Personal data held on the hosted service is therefore stored in Europe and is transferred outside Ghana. We will update this Policy if the hosting place changes in a material way.

Please protect phones with a screen lock and keep sign-in details private. A lost or reset phone may still hold unsynced records that were only on that device — see the Terms for the workshop's responsibility for backups.

When a record is deleted in the app, the server keeps a short "tombstone" so other devices can stay in step. Those tombstones are removed after **90 days**.

## 5. Who the data is shared with — we do not sell it

- **We do not sell personal data.** Selling personal data is an offence under Act 843, section 89.
- Data is shared only: with authorised staff in the same workshop, according to their role; with the hosting and database provider (**MonsterASP.NET**, in Europe); with **Gmail SMTP** or **Resend**, which we use to send email (password-reset and optional invite messages); with **Meta** (WhatsApp / graph.facebook.com) when a workshop has turned on WhatsApp notices and a customer has opted in — in that case the customer's number and the notice can be processed by Meta under Meta's own terms; with NeedleBase's licensing service, for licence checks; where the workshop instructs us; and where the law, a court or a regulator requires disclosure.
- MoMo providers and banks are independent. NeedleBase does not send them measurement data.
- The public website may include a WhatsApp "click to chat" link (wa.me) so you can message us. That is a link you choose to open; it is not a transfer of your customer list.

## 6. How long we keep it, and how to ask for deletion

We and workshops should not keep personal data longer than needed for the purpose it was collected, unless a law allows or requires it, or the person consents (Act 843, section 24).

- **If you delete your account in the app or on the website:** after you confirm your password, a staff member's sign-in account is deleted at once (the shop's customer, order and payment records stay). If the Owner deletes their account, the shop is closed at once: everyone is signed out and cannot sign in, and we delete the shop's records that we hold on the hosted service (including customer, measurement, order and payment records, WhatsApp settings and staff sign-in accounts) at once, except where the law requires or allows us to keep something. The activity log is kept for up to 730 days (see below). A copy only on a phone is not deleted by this. The in-app path is More → Delete account. The web page is at [/account/delete](/account/delete).
- **If you cannot sign in** (or prefer not to use the button): an Owner (or a staff member, about their own sign-in account) may ask us by email or WhatsApp, using the contacts below, or start from the public page at [/account/delete](/account/delete). After we have verified the request, we will normally delete the account and the associated data we hold on the hosted service within **30 days**, except where the law requires or allows us to keep something.
- **If a shop simply stops using NeedleBase** without asking us to delete the account, we keep the shop's records that we hold on the hosted service (including customer, order and payment records) for **one year**, and then we delete them, except where the law requires or allows us to keep something.
- **Audit log:** deleted after 730 days by default (see section 2). This is a separate period and may run longer or shorter than the one-year shop-data period, including after an account-deletion request.
- **Sync tombstones:** deleted after 90 days (see section 4).
- **Password-reset codes:** short-lived; old unused codes are cleaned up.
- **Staff invites:** expire after seven days.

On a verified written request from the Owner we will also, within a reasonable period (normally within 30 days), make a reasonable copy of the data we hold available, except where the law does not allow that. We do not promise a particular export format. Data stored only on a workshop's own device remains the workshop's responsibility.

## 7. Security

We use reasonable technical and organisational measures consistent with Act 843, section 28 — including role-based access (Owner, Manager, Tailor), hashed passwords, a protected licence key, signed-in sessions, and HTTPS for the hosted site and sync (as deployed). No system is completely secure. The copy on the phone is not encrypted by the app itself, so a screen lock and care of the device matter.

If there are reasonable grounds to believe personal data has been accessed or acquired by an unauthorised person, Act 843, section 31 requires the controller to notify the Data Protection Commission and affected people as soon as reasonably practicable (subject to any delay directed for a criminal investigation). As processor we will notify the affected workshop and cooperate. The workshop notifies its customers and the Commission where it is the controller.

## 8. Your rights under Act 843

Subject to the Act and proof of identity, a person may:

- **Access** a description of their personal data and a copy in an intelligible form (sections 32 and 35; a controller should normally comply promptly and within 40 days).
- **Ask for correction or deletion** of data that is inaccurate, irrelevant, excessive, out of date, incomplete, misleading or unlawfully obtained, or that the controller is no longer allowed to keep (section 33).
- **Object** under section 20, and by written notice require a controller to stop processing that causes or is likely to cause unwarranted damage or distress (section 39 — the controller must reply in writing within 21 days).
- **Direct marketing:** require a controller not to use their data for direct marketing; prior written consent is needed for that use (section 40). NeedleBase itself is not a marketing tool for advertising to a workshop's customers.
- **Automated decisions:** rights where a solely automated decision significantly affects the person (section 41). NeedleBase does not make such decisions about customers.
- **Complaints and compensation:** a person may complain to the **Data Protection Commission** (see [dataprotection.org.gh](https://dataprotection.org.gh) / [dpc.gov.gh](https://dpc.gov.gh)) and may have a path to compensation through the courts where the Act allows (section 43).

Please contact the workshop that collected your data first — it is usually the controller. If you contact HelmutCode Solutions about customer data, we will pass the request to the relevant workshop and assist it, unless we are the controller for that data (for example your own owner or staff account).

## 9. Children

NeedleBase is a business tool. Shop accounts are for adults (18 or over) running or working in a workshop. A workshop should not enter a child's personal data except where a parent or guardian has arranged a garment, and then only the minimum needed. Act 843, section 37 restricts processing of special personal data, including data relating to a child under parental control, except as the Act allows.

## 10. Cookies and the public website

The signed-in website uses small cookies that are needed for the service to work, not for advertising:

- **NeedleBaseAuth** — keeps you signed in (about eight hours, or 30 days if you tick "Remember me").
- **NeedleBase.AntiForgery** — a security cookie that helps protect forms.

The public landing page and the Privacy and Terms pages do not use advertising or analytics cookies. Fonts on the landing page are stored on our own site (they are not loaded from Google Fonts).

## 11. The Data Protection Commission

A data controller who intends to process personal data must register with the Data Protection Commission (Act 843, sections 27, 46 and 53; renewal every two years, section 50). HelmutCode Solutions is not registered with the Data Protection Commission. Each workshop is responsible for its own registration as controller of its customer data.

A person may complain to the Commission about the handling of their personal data (see [dataprotection.org.gh](https://dataprotection.org.gh) / [dpc.gov.gh](https://dpc.gov.gh)).

## 12. Changes to this Policy

We may update this Policy as the service or the law changes. We will post the new version with a new "Last updated" date and give reasonable notice of material changes through the service or by email.

## 13. How to contact us

HelmutCode Solutions, YN13 Flask Street, Ghana.

Email: [helmutessien@gmail.com](mailto:helmutessien@gmail.com)

WhatsApp: [+233 50 078 5153](https://wa.me/233500785153)

These contacts are also the data-protection contact for HelmutCode Solutions, including requests to delete an account or a copy of the data we hold. You can also delete an account in the app (More → Delete account) or at [https://needlebase.runasp.net/account/delete](https://needlebase.runasp.net/account/delete).

If you are a customer of a tailoring or fashion workshop, please contact that workshop first about its customer records.

The live web copy of this Policy is at [https://needlebase.runasp.net/Privacy](https://needlebase.runasp.net/Privacy). The same text is included in the NeedleBase phone app (More → Privacy Policy, and from the registration and sign-in screens) so it can be read offline.
