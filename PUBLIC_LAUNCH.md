# Finance Freedom public launch notes

## Local account setup

1. Stop any older copy of the app.
2. Run `dotnet run` using the Development launch profile.
3. Register an account. Signup does not require email verification. New accounts always start with no transactions or budgets; existing unowned legacy rows are never assigned automatically.

Account credentials are stored in `pocketledger-auth.db`. Transactions and budgets remain in `pocketledger.db`, with every query scoped to the signed-in account ID. Keep both files private and back them up together while developing.

## Stripe Plus configuration

Plus is £3.99 per month. Checkout and subscription state are handled by Stripe. Do not put live credentials in source control. Configure these environment variables in the host:

- `Stripe__SecretKey`
- `Stripe__WebhookSecret`
- `Stripe__PublicOrigin` (the canonical HTTPS site origin, with no trailing slash)

Point a Stripe webhook at `POST /billing/webhook` and subscribe it to:

- `customer.subscription.created`
- `customer.subscription.updated`
- `customer.subscription.deleted`

The local checkout button remains in setup-needed mode until the server key, webhook signing secret, and public origin are configured. Use Stripe test credentials and test cards before enabling live mode.

## Publish on Render

The project includes a Dockerfile and a Render Blueprint in `render.yaml`. Render is the recommended starting host because it can build this ASP.NET app from a Git repository and provision the app and managed PostgreSQL database together.

1. Create a **private** GitHub repository and push this project to it. Do not upload `pocketledger.db`, `pocketledger-auth.db`, secrets, or local user data. The `.gitignore` excludes these files; verify the repository contents before pushing.
2. Create a Render account and connect the private repository.
3. In Render, create a new Blueprint from the repository. The included Blueprint is configured for the free web and PostgreSQL plans in Virginia to match the existing service shown in the Render setup. Confirm the selected plans before applying any changes.
4. Apply the Blueprint. Wait for the web service health check at `/healthz` to pass, then open the generated `onrender.com` address and try account creation and sign-in.
5. Before enabling public registration, finish the privacy/legal details and production checks below. Add a custom domain later if you own one; use the generated Render address for the initial deployment.

The Render preview configuration uses its free web and PostgreSQL plans with separate `accounts` and `finance` schemas in one database. Free Render Postgres expires after 30 days and its data is deleted after the following grace period, so treat it as temporary preview storage only. Do not accept real financial data or promise permanent storage on that plan. Local SQLite data files are deliberately excluded from the deployment image and source control; the hosted service starts with an empty database. Existing local accounts and entries are not migrated.

Non-development startup requires a persistent PostgreSQL database and automatically uses Render's standard `DATABASE_URL` when present. The Render Blueprint provisions a PostgreSQL database, sets `Database__Provider=Postgres`, and links its connection URL to `DATABASE_URL`. Local SQLite data files are never used as a production fallback.

If you created a Render Web Service directly instead of applying the Blueprint, create a Render Postgres database in the same region as the web service, then connect it to the web service. Set `Database__Provider` to `Postgres` and `Database__Postgres__Url` to the database's internal URL, or set `DATABASE_URL` to the internal connection string. Save the environment changes and redeploy. The current startup error means neither a PostgreSQL provider nor a PostgreSQL URL reached the service. Do not use a local SQLite file for production because it is not persistent across Render deploys.

## Before opening registration publicly

- Replace the launch placeholder in the Privacy page with the operator's legal name, contact details, hosting region, retention schedule, and applicable privacy rights. Confirm the site's terms and the process for handling access/deletion requests.
- Configure the production host's forwarded-proxy boundary so untrusted clients cannot spoof `X-Forwarded-*` headers. The app uses them to apply HTTPS redirects correctly behind Render's TLS proxy.
- ASP.NET Data Protection keys are stored in the PostgreSQL `finance` schema so sign-in cookies survive restarts and are shared across instances. Protect the database and its backups as sensitive security data.
- Configure database backups, restore procedures, monitoring, and alerting. Password recovery is not implemented yet because no outbound email provider is configured; add and verify a transactional email service before promising account recovery.
- Review the current authentication, account deletion, Stripe webhook, tenant isolation, and rate-limit behavior. This is a launch scaffold, not a completed independent security review.
- Add `Stripe__SecretKey`, `Stripe__WebhookSecret`, and `Stripe__PublicOrigin` in Render's secret environment variables only after testing checkout and webhooks in Stripe test mode. Plus billing is unavailable until configured.
- For company quotes/news, set `MarketData__FinnhubApiKey` only after obtaining a plan with rights appropriate for public display. Verify the provider's data delay and redistribution terms.
- For bank linking, create a TrueLayer application, enable `accounts` and `cards`, set `Banking__ClientId`, `Banking__ClientSecret`, `Banking__RedirectUri`, and `Banking__Environment`; register the exact callback URL `https://YOUR-DOMAIN/banking?handler=Callback`. This app requests only those scopes and does not import transactions.
- For AdSense, first deploy the public site and complete its privacy/contact details. The publisher code is in the shared page `<head>` and the app generates `/ads.txt`. The public `/guides` page contains original budgeting content and the manual free-plan ad placement; private finance pages do not contain manual ad units. In AdSense, add the deployed site, choose Google's Privacy & messaging CMP or another Google-certified TCF CMP for UK/EEA/Swiss visitors, and request review. Set `Advertising__PublisherId` to your account's publisher ID and `Advertising__GuidesSlotId` to the numeric ID for your Guides ad unit. Ads stay off until the site is approved, the consent platform is configured, and you set `Advertising__ConsentManagementPlatformConfigured=true` and `Advertising__Enabled=true`. Plus accounts do not see the manual Guides ad unit. Keep Auto ads off or exclude authenticated finance routes so ads do not appear alongside private financial data.
- The root `robots.txt` leaves public content crawlable and excludes private account/finance routes. AdSense review approval is Google's decision; useful original content, live access for its crawler, clear navigation, and completed site information are still required.
- Finalize the privacy notice with the operator's legal name, contact details, hosting region, retention schedule, and applicable data rights. Review subscription terms, cancellation/refund language, and tax settings.
- Complete a security review of auth, account deletion, Stripe webhooks, tenant isolation, and deployed secrets before accepting real financial data or live payments.
- The initial PostgreSQL schema bootstrap is idempotent but not a substitute for versioned EF Core migrations. Add migrations before changing persisted models.
