namespace Wallet.Tests;

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Wallet.Infrastructure.Payments;
using Xunit;

public sealed class StripeSettingsTests
{
    private static IConfiguration Build(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static Dictionary<string, string?> Valid(string environment = "Production") => new()
    {
        ["ASPNETCORE_ENVIRONMENT"] = environment,
        ["Stripe:SecretKey"] = "sk_test_123",
        ["Stripe:WebhookSecret"] = "whsec_123",
        ["Stripe:SuccessUrl"] = "https://shop.example/success",
        ["Stripe:CancelUrl"] = "https://shop.example/cancel"
    };

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("<SET_VIA_ENVIRONMENT>", "<SET_VIA_ENVIRONMENT>")]
    [InlineData("<REPLACE_WITH_STRIPE_KEY>", "<REPLACE_WITH_STRIPE_SECRET>")]
    public void Unconfigured_ReturnsFalseAndDoesNotThrow(string? key, string? webhook)
    {
        var config = Build(new() { ["Stripe:SecretKey"] = key, ["Stripe:WebhookSecret"] = webhook });

        Assert.False(StripeSettings.Validate(config));
        StripeSettings.EnsureValid(config, NullLogger.Instance);
    }

    [Theory]
    [InlineData("sk_live_123")]
    [InlineData("pk_test_123")]
    public void NonTestSecretKey_Throws(string key)
    {
        var values = Valid();
        values["Stripe:SecretKey"] = key;

        Assert.Throws<InvalidOperationException>(() => StripeSettings.Validate(Build(values)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("<SET_VIA_ENVIRONMENT>")]
    public void MissingWebhookSecret_Throws(string? webhook)
    {
        var values = Valid();
        values["Stripe:WebhookSecret"] = webhook;

        Assert.Throws<InvalidOperationException>(() => StripeSettings.Validate(Build(values)));
    }

    [Theory]
    [InlineData("Stripe:SuccessUrl")]
    [InlineData("Stripe:CancelUrl")]
    public void MissingReturnUrl_Throws(string key)
    {
        var values = Valid();
        values[key] = null;

        Assert.Throws<InvalidOperationException>(() => StripeSettings.Validate(Build(values)));
    }

    [Theory]
    [InlineData("Development", true)]
    [InlineData("Production", false)]
    public void LocalhostHttp_IsAllowedOnlyInDevelopment(string environment, bool allowed)
    {
        var values = Valid(environment);
        values["Stripe:SuccessUrl"] = "http://localhost:3000/callback";
        values["Stripe:CancelUrl"] = "http://localhost:3000/cancel";

        if (allowed)
        {
            Assert.True(StripeSettings.Validate(Build(values)));
        }
        else
        {
            Assert.Throws<InvalidOperationException>(() => StripeSettings.Validate(Build(values)));
        }
    }

    [Fact]
    public void DotnetEnvironmentDevelopment_AllowsLocalhostHttp()
    {
        var values = Valid();
        values.Remove("ASPNETCORE_ENVIRONMENT");
        values["DOTNET_ENVIRONMENT"] = "Development";
        values["Stripe:SuccessUrl"] = "http://localhost:3000/callback";
        values["Stripe:CancelUrl"] = "http://localhost:3000/cancel";

        Assert.True(StripeSettings.Validate(Build(values)));
    }

    [Theory]
    [InlineData("http://example.com/callback")]
    [InlineData("http://user:pass@localhost:3000/callback")]
    [InlineData("http://localhost:4000/callback")]
    [InlineData("ftp://localhost:3000/callback")]
    public void UnsafeHttpUrls_AreRejectedEvenInDevelopment(string url)
    {
        var values = Valid("Development");
        values["Stripe:SuccessUrl"] = url;

        Assert.Throws<InvalidOperationException>(() => StripeSettings.Validate(Build(values)));
    }

    [Fact]
    public void ValidHttpsConfiguration_InProduction_ReturnsTrue()
    {
        Assert.True(StripeSettings.Validate(Build(Valid())));
    }
}
