using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PocketLedger.Models;
using Stripe;
using Stripe.Checkout;

namespace PocketLedger.Data;

public static class StripeBillingEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapPost("/billing/checkout", async (
            HttpContext context,
            UserManager<ApplicationUser> users,
            IConfiguration configuration,
            IWebHostEnvironment environment) =>
        {
            var user = await users.GetUserAsync(context.User);
            if (user is null)
            {
                return Results.Challenge();
            }

            if (user.IsPlus)
            {
                return Results.Redirect("/Account/Manage");
            }

            var secretKey = configuration["Stripe:SecretKey"];
            var webhookSecret = configuration["Stripe:WebhookSecret"];
            var publicOrigin = configuration["Stripe:PublicOrigin"];
            if (string.IsNullOrWhiteSpace(secretKey) || string.IsNullOrWhiteSpace(webhookSecret) ||
                (string.IsNullOrWhiteSpace(publicOrigin) && !environment.IsDevelopment()))
            {
                return Results.Redirect("/Plans?billing=setup-needed");
            }

            var origin = string.IsNullOrWhiteSpace(publicOrigin)
                ? $"{context.Request.Scheme}://{context.Request.Host}"
                : publicOrigin.TrimEnd('/');
            StripeConfiguration.ApiKey = secretKey;

            var options = new SessionCreateOptions
            {
                Mode = "subscription",
                ClientReferenceId = user.Id,
                Customer = user.StripeCustomerId,
                CustomerEmail = user.StripeCustomerId is null ? user.Email : null,
                SuccessUrl = $"{origin}/Plans?payment=success",
                CancelUrl = $"{origin}/Plans?payment=cancelled",
                LineItems =
                [
                    new SessionLineItemOptions
                    {
                        Quantity = 1,
                        PriceData = new SessionLineItemPriceDataOptions
                        {
                            Currency = "gbp",
                            UnitAmount = 399,
                            ProductData = new SessionLineItemPriceDataProductDataOptions { Name = "Finance Freedom Plus" },
                            Recurring = new SessionLineItemPriceDataRecurringOptions { Interval = "month" }
                        }
                    }
                ],
                SubscriptionData = new SessionSubscriptionDataOptions
                {
                    Metadata = new Dictionary<string, string> { ["userId"] = user.Id }
                }
            };

            var session = await new SessionService().CreateAsync(options);
            return Results.Redirect(session.Url);
        }).RequireAuthorization();

        app.MapPost("/billing/portal", async (
            HttpContext context,
            UserManager<ApplicationUser> users,
            IConfiguration configuration) =>
        {
            var user = await users.GetUserAsync(context.User);
            if (user?.StripeCustomerId is null || string.IsNullOrWhiteSpace(configuration["Stripe:SecretKey"]))
            {
                return Results.Redirect("/Plans?billing=setup-needed");
            }

            StripeConfiguration.ApiKey = configuration["Stripe:SecretKey"];
            var publicOrigin = configuration["Stripe:PublicOrigin"];
            var origin = string.IsNullOrWhiteSpace(publicOrigin)
                ? $"{context.Request.Scheme}://{context.Request.Host}"
                : publicOrigin.TrimEnd('/');
            var portal = await new Stripe.BillingPortal.SessionService().CreateAsync(
                new Stripe.BillingPortal.SessionCreateOptions
                {
                    Customer = user.StripeCustomerId,
                    ReturnUrl = $"{origin}/Account/Manage"
                });
            return Results.Redirect(portal.Url);
        }).RequireAuthorization();

        app.MapPost("/billing/webhook", async (
            HttpRequest request,
            UserManager<ApplicationUser> users,
            IConfiguration configuration) =>
        {
            var signingSecret = configuration["Stripe:WebhookSecret"];
            if (string.IsNullOrWhiteSpace(signingSecret))
            {
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            }

            using var reader = new StreamReader(request.Body);
            var payload = await reader.ReadToEndAsync();
            Event stripeEvent;
            try
            {
                stripeEvent = EventUtility.ConstructEvent(payload, request.Headers["Stripe-Signature"], signingSecret);
            }
            catch (Exception)
            {
                return Results.BadRequest();
            }

            if (stripeEvent.Data.Object is not Subscription subscription ||
                stripeEvent.Type is not ("customer.subscription.created" or "customer.subscription.updated" or "customer.subscription.deleted"))
            {
                return Results.Ok();
            }

            var userId = subscription.Metadata.GetValueOrDefault("userId");
            var user = userId is null
                ? await users.Users.FirstOrDefaultAsync(item => item.StripeCustomerId == subscription.CustomerId)
                : await users.FindByIdAsync(userId);

            if (user is null)
            {
                return Results.Ok();
            }

            user.StripeCustomerId = subscription.CustomerId;
            user.StripeSubscriptionId = subscription.Id;
            user.SubscriptionStatus = subscription.Status;
            user.Plan = subscription.Status is "active" or "trialing" ? "Plus" : "Free";
            await users.UpdateAsync(user);
            return Results.Ok();
        });
    }
}
