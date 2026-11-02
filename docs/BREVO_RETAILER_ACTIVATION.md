# Brevo setup for Retailer activation OTP

## 1. Create and secure the Brevo account

1. Create a Brevo account and keep the **Free** plan. It currently includes 300 email sends per day and transactional email.
2. Complete the account/profile verification prompts. In **Transactional**, confirm that transactional sending is active. If Brevo reports that SMTP/transactional sending is not activated, open a Brevo support ticket before testing the API.
3. Enable MFA for the owner account. Do not share the owner login or API key through chat/email.

Official plan information: https://help.brevo.com/hc/en-us/articles/208589409-About-Brevo-s-pricing-plans

## 2. Authenticate the sending domain and sender

For a production-like demo, use an address on a domain controlled by the team, for example `no-reply@fscm.example.com`.

1. Open **Settings → Senders, Domains, IPs → Domains**.
2. Select **Add a domain**, enter the sending domain, and choose automatic authentication when supported by the DNS provider.
3. Otherwise copy Brevo's DNS records (Brevo verification code, DKIM, and DMARC) into the DNS provider. Wait for DNS propagation, then use **Authenticate** in Brevo until all required records pass.
4. Open **Settings → Senders, Domains, IPs → Senders → Add a sender**.
5. Set the sender name to `FSCM` and use an address on the authenticated domain. If the domain is not authenticated, complete the six-digit sender verification sent by Brevo.

The configured `Brevo:SenderEmail` must exactly match a verified sender or an address on the authenticated domain.

Official guides:

- https://help.brevo.com/hc/en-us/articles/12163873383186-Authenticate-your-domain-with-Brevo-Brevo-code-DKIM-DMARC
- https://help.brevo.com/hc/en-us/articles/208836149-Create-a-new-sender-From-name-and-From-email

## 3. Generate a restricted API key

1. Open the account menu, then **Settings → SMTP & API → API Keys & MCP**.
2. Select **Generate API key** and complete Brevo's verification challenge.
3. Name it clearly, for example `FSCM development transactional email`.
4. Choose **Restricted** access. Give **Transactional email** the required `All` permission (Brevo currently exposes `None`, `Read`, and `All` for this resource); leave unrelated resources at `None`.
5. Choose a reasonable expiry and copy the key immediately. Brevo shows it only once.
6. Store the value in an environment variable or Azure Key Vault. Never commit it.

Official guide: https://help.brevo.com/hc/en-us/articles/40159485019794-Create-and-manage-your-API-keys

## 4. Configure FSCM locally

PowerShell for the current terminal:

```powershell
$env:Brevo__ApiKey = '<API key copied from Brevo>'
$env:Brevo__SenderEmail = 'no-reply@your-domain.example'
$env:Brevo__SenderName = 'FSCM'
$env:Brevo__ApiBaseUrl = 'https://api.brevo.com/v3/'
```

For persistent local development, use .NET User Secrets or the Git-ignored `src/FSCM.API/appsettings.json`. Do not put the real key in `appsettings.example.json`.

Azure App Service settings use the same double-underscore names. With Key Vault references, set `Brevo__ApiKey` to the Key Vault reference and grant the app's managed identity permission to read that secret.

## 5. Prepare a Retailer account

The activation request succeeds only when all of these are true:

- `USER_ACCOUNT.Role` is `Retailer` and `IsActive` is false;
- `USER_ACCOUNT.Phone` matches the phone entered in Mobile;
- a `RETAILER` row is linked through `UserAccountId`;
- for the `Email` channel (or SMS fallback): the account has a syntactically valid email address.

An SMS OTP proves possession of the phone number; an email OTP proves access to the linked email only.

### OTP channels (SMS and email)

`POST /api/v1/auth/otp/request` accepts `{ "phone": "0901234567", "channel": "Sms" | "Email" }`.
`channel` is optional and defaults to `RetailerActivation:DefaultOtpChannel`.

```json
"Brevo": { "SmsSender": "FSCM", "SmsCountryCode": "84" },
"RetailerActivation": { "DefaultOtpChannel": "Email", "FallbackToEmail": true }
```

- SMS goes through Brevo `POST /v3/transactionalSMS/send` with the same API key. The local number
  `0901234567` is sent as `84901234567`. The text is ASCII so it fits one 160-character segment.
- Brevo SMS is prepaid: buy SMS credits under **Plans → SMS credits** first. The sender
  (`SmsSender`) is at most 11 letters/digits; Vietnamese carriers usually require a registered
  brand name for alphanumeric senders, otherwise the message may be rewritten or dropped.
- If SMS fails and `FallbackToEmail` is true and the account has a valid email, the same OTP is sent
  by email. The response `channel` tells Mobile where the code actually went:
  `{ "channel": "Sms", "maskedDestination": "090****567", "maskedEmail": null, ... }`.
- If no channel can deliver, the challenge is consumed (so the user can retry immediately) and the
  API returns `503 sms_delivery_failed` or `503 email_delivery_failed`.
- Environment variables: `Brevo__SmsSender`, `Brevo__SmsCountryCode`,
  `RetailerActivation__DefaultOtpChannel`, `RetailerActivation__FallbackToEmail`.

These preconditions are produced automatically when Admin approves a pending Retailer declaration
(`POST /api/v1/retailers/{retailerId}/review` with `approve=true`):

1. The Retailer becomes `Active`, gets its business code, and an inactive `Retailer` user account is
   created (or an unassigned one is linked) from the declaration contact. Both are saved in one
   transaction, so either both exist or neither does.
2. After the commit, Brevo sends the "Tài khoản FSCM của bạn đã được tạo" email asking the Retailer to
   sign in on Mobile with the phone number and an empty password, then verify by OTP.
3. The result is written to `AUDIT_LOG` as `RetailerAccountCreatedEmailSent` or
   `RetailerAccountCreatedEmailFailed`. A delivery failure does not roll back the approval; the
   Retailer can still sign in with the phone number and request an OTP.

Mobile first login: `POST /api/v1/auth/login` with `{ "identifier": "<phone>", "password": "" }`
(or `password` omitted) returns `409 retailer_activation_required`. Mobile then calls
`otp/request` → `otp/verify` → `password`.

## 6. Run and verify end to end

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet ef database update --project src/FSCM.Persistence/FSCM.Persistence.csproj --startup-project src/FSCM.API/FSCM.API.csproj
dotnet run --project src/FSCM.API/FSCM.API.csproj
```

Then on Mobile select **Kích hoạt tài khoản Retailer lần đầu**:

1. Enter the linked phone number and request OTP.
2. Check **Transactional → Logs** in Brevo and the recipient inbox/spam folder.
3. Enter the six-digit OTP within five minutes.
4. Create a password (at least 12 characters with uppercase, lowercase, number, and special character).
5. Confirm the app receives the normal access/refresh-token authentication result and enters the Retailer shell.
6. Log out and confirm subsequent login uses phone or email plus password without OTP.

The API endpoints are:

- `POST /api/v1/auth/otp/request`
- `POST /api/v1/auth/otp/verify`
- `POST /api/v1/auth/password`
- `POST /api/v1/auth/login` with `identifier` set to an email or phone number

## 7. Troubleshooting

- `email_delivery_failed`: verify transactional sending is active, the sender is verified, and the API key has Transactional email permission.
- `sms_delivery_failed`: check SMS credits, that the API key has Transactional SMS permission, and **Transactional SMS → Logs** in Brevo. HTTP 402 from Brevo means no SMS credits.
- HTTP 401/403 from Brevo: rotate/check the API key and its restricted permissions.
- HTTP 400 from Brevo: confirm sender/recipient formatting and that `SenderEmail` is verified.
- Email is accepted but not received: inspect Brevo Transactional logs, blocked contacts, bounce reason, spam, DKIM, and DMARC.
- HTTP 429 from FSCM: wait for the account/IP rate-limit window; resend itself has a 60-second cooldown.
- `otp_expired`: request a new code; codes expire in five minutes.
- `otp_attempts_exceeded`: request a new code after five failed attempts.
- Free-plan quota: 300 sends reset daily; unused sends do not roll over. Brevo may queue some transactional messages after the daily limit, so do not rely on immediate delivery once the quota is exhausted.

Brevo API reference: https://developers.brevo.com/docs/send-a-transactional-email
