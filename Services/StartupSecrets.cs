using Microsoft.Extensions.Configuration;

namespace SoftcodeUnicontaMiddleware.Services
{
    /// <summary>
    /// Startup guard for the application's own secrets.
    /// </summary>
    /// <remarks>
    /// A predictable JWT signing key lets anyone forge access tokens, and a
    /// predictable client-secret pepper makes a leaked database brute-forceable.
    /// The shipped appsettings.json carries obvious placeholders, so the app must
    /// refuse to start until real values are supplied (user-secrets, environment
    /// variables or appsettings.Production.json) rather than boot insecurely.
    /// </remarks>
    public static class StartupSecrets
    {
        private const string PlaceholderPrefix = "CHANGE_THIS";

        /// <summary>
        /// Returns the configured secret at <paramref name="path"/>, or throws if it
        /// is missing, still the shipped placeholder, or shorter than
        /// <paramref name="minLength"/> characters.
        /// </summary>
        public static string Require(IConfiguration config, string path, int minLength)
        {
            var value = config[path];

            if (string.IsNullOrWhiteSpace(value)
                || value.StartsWith(PlaceholderPrefix, StringComparison.OrdinalIgnoreCase)
                || value.Length < minLength)
            {
                throw new InvalidOperationException(
                    $"Configuration \"{path}\" is missing, still a placeholder, or shorter than " +
                    $"{minLength} characters. Set a real secret (e.g. " +
                    $"dotnet user-secrets set \"{path}\" <value>) before starting the application.");
            }

            return value;
        }
    }
}
