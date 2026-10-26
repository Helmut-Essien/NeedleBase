# WhatsApp setup (Meta WhatsApp Business Cloud API)

NeedleBase uses WhatsApp in two ways:

1. **Tap-to-send.** Works with no setup, on the phone app and the web. On an order, staff tap **Message on WhatsApp** (phone) or **Open in WhatsApp** (web). WhatsApp opens with a ready message they can change, and nothing is sent until they press Send. Numbers are turned into `+233…` form. The phone app builds the message on the device, so this works offline. Activity records it as "opened", not "sent".
2. **Automatic updates.** These need the setup below. When an order reaches a status the owner picked, the server sends an approved **template** message. It only sends to customers who agreed, waits 2 minutes so a mistaken status change sends nothing, and retries if sending fails. A customer who replies **STOP** stops getting them. Without this setup the feature is off, the WhatsApp settings page says so, and tap-to-send still works.

One WhatsApp number (the NeedleBase platform number) sends for every shop. The shop's name goes into each message.

## 1. Meta business, app and number

1. Go to <https://business.facebook.com> and create (or open) the **Meta Business portfolio** for the business that will own the number.
2. Business verification: **Business settings → Security centre → Start verification**. Upload the company registration (for Ghana, the Registrar-General certificate), a utility bill or bank letter showing the same name and address, and the website (helmutcode.com). Verification usually takes 1–5 working days. Until it's done you can only message 5 test numbers, with a limit of 250 conversations a day.
3. Go to <https://developers.facebook.com/apps>, choose **Create app → Other → Business**, and link it to the portfolio. Add the **WhatsApp** product.
4. In **WhatsApp → API Setup**, add a phone number. It must be a number that **is not** registered in the WhatsApp or WhatsApp Business app (delete that account first, or use a new SIM). Verify it by SMS or voice. Set the **display name**: Meta checks that it matches the business name.
5. Copy two values from API Setup: the **Phone number ID** (a long number, not the phone number) and the **WhatsApp Business Account ID**.
6. Add a payment method in **WhatsApp Manager → Payment settings**. Meta charges per template message (utility category). Only templates are sent, never free text.

## 2. Permanent access token

The temporary token on API Setup expires after 24 hours. Use a System User token instead:

1. **Business settings → Users → System users → Add**. Give it a name such as `needlebase-api` with the Admin role.
2. **Add assets**: add the app (full control) and the WhatsApp account (full control).
3. **Generate new token**: choose the app, set the expiry to **Never**, and tick `whatsapp_business_messaging` and `whatsapp_business_management`.
4. Copy the token once and keep it secret. It is the `WhatsApp:AccessToken` value.
5. Copy the **App secret** from **App settings → Basic → App secret → Show**. It is the `WhatsApp:AppSecret` value, used to check that webhook calls really come from Meta.

## 3. Message templates

Create these in **WhatsApp Manager → Message templates → Create template**. For each one:

- Category: **Utility**.
- Language: **English**. The language code must match `WhatsApp:TemplateLanguage`. It defaults to `en`; use `en_GB` or `en_US` if that's what you pick.
- Body variables, in this order: `{{1}}` customer first name, `{{2}}` shop name, `{{3}}` order reference. Meta asks for sample values, for example `Efua`, `Ama Stitches` and `NB-0042`.
- Footer (optional, recommended): `Reply STOP to stop these updates.`

| Status | Template name | Body |
| --- | --- | --- |
| Fitting | `order_ready_for_fitting` | Hello {{1}}, this is {{2}}. Your order {{3}} is ready for fitting. Please come by when you are free. |
| Ready | `order_ready_for_pickup` | Hello {{1}}, this is {{2}}. Your order {{3}} is ready for pickup. You are welcome any time during opening hours. Thank you! |
| Collected | `order_collected_thanks` | Hello {{1}}, this is {{2}}. Thank you for collecting your order {{3}}. We hope you enjoy it! |
| Cutting (optional) | `order_cutting_started` | Hello {{1}}, this is {{2}}. Good news: we have started cutting your order {{3}}. |
| Sewing (optional) | `order_sewing_started` | Hello {{1}}, this is {{2}}. Your order {{3}} is now being sewn. |
| Cancelled (optional) | `order_cancelled_notice` | Hello {{1}}, this is {{2}}. Your order {{3}} has been cancelled. Please call us if you have any questions. |

Approval usually takes minutes, sometimes up to a day. Only statuses with an **approved** template configured on the server can be chosen in Settings → WhatsApp. "New" is never sent automatically.

## 4. Webhook (delivery ticks and STOP replies)

> **On hold (26 Oct 2026):** the public Api host isn't set up yet, so the URL below is a placeholder. Until it is, messages still send, but delivery ticks and STOP replies don't reach NeedleBase; record opt-outs by hand on the customer (web or phone).

1. Choose a long random **verify token** (for example `openssl rand -hex 32`). It is `WhatsApp:VerifyToken`.
2. In the app, open **WhatsApp → Configuration → Webhook → Edit**:
   - Callback URL: `https://<your Api host>/api/whatsapp/webhook`, for example `https://api.helmutcode.com/api/whatsapp/webhook`. For MonsterASP hosting, use the site's https address (`https://<your-site>.runasp.net/api/whatsapp/webhook`), or the custom domain mapped to it. It must be **https** with a valid certificate.
   - Verify token: the same value as `WhatsApp:VerifyToken`.
   - Press **Verify and save**. The Api must already be running with VerifyToken and AppSecret set, or Meta's check fails (the Api answers 404 when the webhook isn't configured and 403 for a wrong token).
3. Under **Webhook fields**, subscribe to **messages**. This one field carries both delivery statuses and customer replies.
4. Calls with a bad `X-Hub-Signature-256` are refused (401). Bodies over 1 MB are refused.

## 5. Configuration keys

| Key | Example / default | Secret? |
| --- | --- | --- |
| `WhatsApp:AccessToken` | System User token | **yes** |
| `WhatsApp:AppSecret` | app secret | **yes** |
| `WhatsApp:VerifyToken` | random string | **yes** |
| `WhatsApp:PhoneNumberId` | `1234567890…` | no |
| `WhatsApp:BusinessAccountId` | `1234567890…` | no |
| `WhatsApp:TemplateLanguage` | `en` | no |
| `WhatsApp:Templates:Ready` (one per status) | `order_ready_for_pickup` | no |
| `WhatsApp:ApiVersion` | `v21.0` | no |
| `WhatsApp:DelaySeconds` | `120` | no |
| `WhatsApp:MaxPerBusinessPerHour` | `100` | no |

Development (user-secrets, from the repo root):

```bash
cd src/NeedleBase.Api
dotnet user-secrets set "WhatsApp:AccessToken" "<token>"
dotnet user-secrets set "WhatsApp:AppSecret" "<app secret>"
dotnet user-secrets set "WhatsApp:VerifyToken" "<random string>"
dotnet user-secrets set "WhatsApp:PhoneNumberId" "<phone number id>"
dotnet user-secrets set "WhatsApp:BusinessAccountId" "<waba id>"
dotnet user-secrets set "WhatsApp:Templates:Fitting" "order_ready_for_fitting"
dotnet user-secrets set "WhatsApp:Templates:Ready" "order_ready_for_pickup"
dotnet user-secrets set "WhatsApp:Templates:Collected" "order_collected_thanks"
```

Production (MonsterASP control panel → site → **Environment variables**, or `web.config` `<environmentVariables>` written by the panel; never in git). Use a double underscore:

```
WhatsApp__AccessToken, WhatsApp__AppSecret, WhatsApp__VerifyToken,
WhatsApp__PhoneNumberId, WhatsApp__BusinessAccountId,
WhatsApp__Templates__Ready=order_ready_for_pickup, WhatsApp__Templates__Fitting=..., WhatsApp__TemplateLanguage=en
```

Restart the Api after any change. If the token leaks, delete it under System users and generate a new one.

## 6. Turning it on in NeedleBase

1. The Owner or a Manager opens **WhatsApp** (web sidebar), switches on **Send automatic updates**, ticks the statuses and saves.
2. On each order (web), under **WhatsApp**, press **Customer agreed: send automatic updates** for customers who said yes. The default is off. WhatsApp's rules require the customer's agreement before you send them business messages.
3. Check the **Messages** list on the WhatsApp page for each message's state: Pending, Sent, Delivered, Read, Failed or Skipped (with the reason).

## Limits and notes

- The same number on two shops: a STOP reply switches it off at both, because one platform number sends for all shops.
- A phone that changes status offline triggers the message when it syncs. Only one message goes out per order and status.
- Meta's per-number limit starts at 250 business-initiated conversations a day before verification, and 1,000 after. NeedleBase also caps each shop at 100 messages an hour.

## Phone app

Owners and managers can turn automatic updates on/off, choose statuses and see the message log under **More → WhatsApp** (needs a connection). Each customer's consent is on **Customer detail**; it can be changed offline and is sent with the next sync. If the customer replied STOP after the change was made on the phone, the STOP is kept.

This setup uses one shared platform WhatsApp number for every shop (agreed for now): a customer who replies STOP stops messages from every shop that uses NeedleBase.
