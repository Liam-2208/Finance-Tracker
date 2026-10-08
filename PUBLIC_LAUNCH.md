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
3. In Render, create a new Blueprint from the repository and review the services and their current prices. Applying it provisions paid compute and database resources and starts billing under Render's current terms.
4. Apply the Blueprint. Wait for the web service health check at `/healthz` to pass, then open the generated `onrender.com` address and try account creation and sign-in.
5. Before enabling public registration, finish the privacy/legal details and production checks below. Add a custom domain later if you own one; use the generated Render address for the initial deployment.

The production configuration uses PostgreSQL with separate `accounts` and `finance` schemas in the same managed database. Local SQLite data files are deliberately excluded from the deployment image and source control; the hosted service starts with an empty database. Existing local accounts and entries are not migrated.

## Before opening registration publicly

- Replace the launch placeholder in the Privacy page with the operator's legal name, contact details, hosting region, retention schedule, and applicable privacy rights. Confirm the site's terms and the process for handling access/deletion requests.
- Configure the production host's forwarded-proxy boundary so untrusted clients cannot spoof `X-Forwarded-*` headers. The app uses them to apply HTTPS redirects correctly behind Render's TLS proxy.
- ASP.NET Data Protection keys are stored in the PostgreSQL `finance` schema so sign-in cookies survive restarts and are shared across instances. Protect the database and its backups as sensitive security data.
- Configure database backups, restore procedures, monitoring, and alerting. Password recovery is not implemented yet because no outbound email provider is configured; add and verify a transactional email service before promising account recovery.
- Review the current authentication, account deletion, Stripe webhook, tenant isolation, and rate-limit behavior. This is a launch scaffold, not a completed independent security review.
- Add `Stripe__SecretKey`, `Stripe__WebhookSecret`, and `Stripe__PublicOrigin` in Render's secret environment variables only after testing checkout and webhooks in Stripe test mode. Plus billing is unavailable until configured.
- For company quotes/news, set `MarketData__FinnhubApiKey` only after obtaining a plan with rights appropriate for public display. Verify the provider's data delay and redistribution terms.
- For bank linking, create a TrueLayer application, enable `accounts` and `cards`, set `Banking__ClientId`, `Banking__ClientSecret`, `Banking__RedirectUri`, and `Banking__Environment`; register the exact callback URL `https://YOUR-DOMAIN/banking?handler=Callback`. This app requests only those scopes and does not import transactions.
- For ad revenue, obtain AdSense publisher/site approval, install and configure a Google-certified consent management platform for UK/EEA/Swiss traffic, then set `Advertising__Enabled`, `Advertising__PublisherId`, `Advertising__SavingsSlotId`, and `Advertising__ConsentManagementPlatformConfigured`. The free savings page has the ad placement; Plus accounts do not.
- Finalize the privacy notice with the operator's legal name, contact details, hosting region, retention schedule, and applicable data rights. Review subscription terms, cancellation/refund language, and tax settings.
- Complete a security review of auth, account deletion, Stripe webhooks, tenant isolation, and deployed secrets before accepting real financial data or live payments.
- The initial PostgreSQL schema bootstrap is idempotent but not a substitute for versioned EF Core migrations. Add migrations before changing persisted models.
