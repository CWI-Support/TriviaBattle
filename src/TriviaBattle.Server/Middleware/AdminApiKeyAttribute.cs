using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System.Security.Cryptography;
using System.Text;

namespace TriviaBattle.Server.Middleware
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public class AdminApiKeyAttribute : Attribute, IAsyncActionFilter
    {
        private const string ApiKeyHeaderName = "X-Admin-Api-Key";

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var configuration = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
            var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<AdminApiKeyAttribute>>();

            var configuredApiKey = configuration.GetValue<string>("AdminApiKey");

            // No key configured → admin endpoints are open (handy for dev). Program.cs logs a
            // warning at startup so an open admin API on the venue LAN never goes unnoticed.
            if (string.IsNullOrEmpty(configuredApiKey))
            {
                await next();
                return;
            }

            if (!context.HttpContext.Request.Headers.TryGetValue(ApiKeyHeaderName, out var providedApiKey))
            {
                logger.LogWarning("Admin endpoint accessed without API key from {IP}",
                    context.HttpContext.Connection.RemoteIpAddress);
                context.Result = new UnauthorizedObjectResult(new
                {
                    error = "Admin API key is required. Provide it in the X-Admin-Api-Key header."
                });
                return;
            }

            if (!CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(configuredApiKey),
                    Encoding.UTF8.GetBytes(providedApiKey.ToString())))
            {
                logger.LogWarning("Invalid admin API key provided from {IP}",
                    context.HttpContext.Connection.RemoteIpAddress);
                context.Result = new UnauthorizedObjectResult(new
                {
                    error = "Invalid admin API key."
                });
                return;
            }

            logger.LogInformation("Admin endpoint accessed successfully from {IP}",
                context.HttpContext.Connection.RemoteIpAddress);

            await next();
        }
    }
}
