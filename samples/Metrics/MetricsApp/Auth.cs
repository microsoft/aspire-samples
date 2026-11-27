using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace MetricsApp;

public static class Auth
{
    // SAMPLE ONLY: a demo customer for the sign-in page and the traffic simulator. Only created in Development.
    public const string TestUserEmail = "test@contoso.com";
    public const string TestUserPassword = "Password1!";

    public static async Task InitializeTestUserAsync(IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<MyUser>>();
        var userStore = scope.ServiceProvider.GetRequiredService<IUserStore<MyUser>>();

        var user = new MyUser();
        await userStore.SetUserNameAsync(user, TestUserEmail, CancellationToken.None);

        var result = await userManager.CreateAsync(user, TestUserPassword);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException("Error creating test user: " + string.Join(", ", result.Errors.Select(e => e.Description)));
        }
    }
}

public sealed class MyUser : IdentityUser { }

public sealed class AppDbContext : IdentityDbContext<MyUser>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }
}
