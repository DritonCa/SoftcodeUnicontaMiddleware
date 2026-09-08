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

        /// <summary>Return the user when username + password match and the account is
        /// active, else null.</summary>
        Task<AdminUser?> ValidateAsync(string username, string password);

        Task<AdminUser?> FindAsync(string username);

        Task<IReadOnlyList<AdminUser>> ListAsync();

        /// <summary>Create a further login. Returns an error message, or null on success.</summary>
        Task<(AdminUser? User, string? Error)> CreateAsync(
            string username, string password, string? email, string role, IEnumerable<string>? companies);

        /// <summary>Change everything except the password. Returns an error message, or null on success.</summary>
        Task<string?> UpdateAsync(
            int id, string? email, string role, IEnumerable<string>? companies, bool isActive, string actingUsername);

        Task<string?> SetPasswordAsync(int id, string password);

        Task<string?> DeleteAsync(int id, string actingUsername);
    }
}
