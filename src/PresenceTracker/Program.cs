using Azure.Identity;
using Microsoft.Azure.Functions.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Graph;
using PresenceTracker.Data;
using PresenceTracker.Services;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices((context, services) =>
    {
        var config = context.Configuration;

        // Microsoft Graph client (app-only, client credentials)
        services.AddSingleton(_ =>
        {
            var tenantId = config["AzureAd:TenantId"];
            var clientId = config["AzureAd:ClientId"];
            var clientSecret = config["AzureAd:ClientSecret"];

            var credential = new ClientSecretCredential(tenantId, clientId, clientSecret);
            return new GraphServiceClient(credential);
        });

        // EF Core with SQL Server
        services.AddDbContextFactory<PresenceDbContext>(options =>
        {
            options.UseSqlServer(config["SqlConnectionString"]);
        });

        // Application services
        services.AddSingleton<GroupMemberCache>();
        services.AddScoped<GraphPresenceService>();
        services.AddScoped<PresenceStorageService>();
    })
    .Build();

host.Run();
