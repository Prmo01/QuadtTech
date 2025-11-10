using MauiApp2.Models;

namespace MauiApp2.Services
{
    public interface IAuthService
    {
        Task<bool> LoginAsync(string username, string password);
        Task LogoutAsync();
        bool IsAuthenticated { get; }
        string CurrentUser { get; }
    }

    public class AuthService : IAuthService
    {
        public bool IsAuthenticated { get; private set; }
        public string CurrentUser { get; private set; } = string.Empty;

        public Task<bool> LoginAsync(string username, string password)
        {
            // Demo credentials
            var demoUsername = "demo@quadtech.com";
            var demoPassword = "QuadTech123!";

            // Check against demo credentials
            bool isDemoUser = (username.Trim().ToLower() == demoUsername.ToLower() &&
                              password == demoPassword);

            if (isDemoUser)
            {
                IsAuthenticated = true;
                CurrentUser = username;
                return Task.FromResult(true);
            }

            return Task.FromResult(false);
        }

        public Task LogoutAsync()
        {
            IsAuthenticated = false;
            CurrentUser = string.Empty;
            return Task.CompletedTask;
        }
    }
}