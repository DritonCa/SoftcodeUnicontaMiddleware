using SoftcodeUnicontaMiddleware.Data.Entities;

namespace SoftcodeUnicontaMiddleware.Services
{
    public interface IClientAuthService
    {
        Task<ApiClient?> ValidateAsync(string clientId, string clientSecret);
    }
}
