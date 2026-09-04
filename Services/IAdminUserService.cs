using SoftcodeUnicontaMiddleware.Data.Entities;

namespace SoftcodeUnicontaMiddleware.Services
{
    public interface IAdminUserService
    {
        /// <summary>True once at least one admin login exists (setup done).</summary>
        Task<bool> AnyExistsAsync();

        /// <summary>Create the first admin login. Returns null if one already exists
        /// or the input is invalid.</summary>
        Task<AdminUser?> CreateFirstAsync(string username, string password);

        /// <summary>Return the user when username + password match, else null.</summary>
        Task<AdminUser?> ValidateAsync(string username, string password);
    }
}
