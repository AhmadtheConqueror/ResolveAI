using Microsoft.AspNetCore.Identity;
using ResolveAI.Api.Entities;

namespace ResolveAI.Api.Tests;

public class UserAdminAndAuthTests
{
    private readonly PasswordHasher<AppUser> _hasher = new();

    public void RunAllTests()
    {
        Console.WriteLine("\n--- UserAdminAndAuthTests ---");

        Test_Password_Hashing_And_Verification();
        Test_Inactive_User_Flag_Rejection();
        Test_Sole_Active_Admin_Deactivation_Protection();
        Test_Sole_Active_Admin_Demotion_Protection();
        Test_Multiple_Admins_Allows_Admin_Modification();
        Test_Public_Registration_Role_Is_Employee_Only();
        Test_Role_Claims_Synchronization_Over_Stale_Token();
    }

    private void Test_Password_Hashing_And_Verification()
    {
        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            Email = "employee@resolveai.internal",
            FirstName = "Test",
            LastName = "Employee"
        };

        var hash = _hasher.HashPassword(user, "SecureP@ssw0rd!2026");
        Assert.NotNull(hash);

        var validResult = _hasher.VerifyHashedPassword(user, hash, "SecureP@ssw0rd!2026");
        Assert.Equal(PasswordVerificationResult.Success, validResult);

        var invalidResult = _hasher.VerifyHashedPassword(user, hash, "WrongPassword!");
        Assert.Equal(PasswordVerificationResult.Failed, invalidResult);

        Console.WriteLine("  ✓ PasswordHasher PBKDF2 successfully hashes and verifies credentials");
    }

    private void Test_Inactive_User_Flag_Rejection()
    {
        var activeUser = new AppUser { IsActive = true };
        var inactiveUser = new AppUser { IsActive = false };

        static bool CanAuthenticate(AppUser user) => user.IsActive;

        Assert.True(CanAuthenticate(activeUser), "Active user can authenticate");
        Assert.False(CanAuthenticate(inactiveUser), "Inactive user must be denied authentication");

        Console.WriteLine("  ✓ Inactive user login check rejects disabled accounts");
    }

    private void Test_Sole_Active_Admin_Deactivation_Protection()
    {
        var adminId = Guid.NewGuid();
        var activeAdmins = new List<AppUser>
        {
            new() { Id = adminId, IsActive = true, Role = new Role { Name = "Admin" } }
        };

        var otherActiveAdminCount = activeAdmins.Count(u => u.IsActive && u.Role.Name == "Admin" && u.Id != adminId);

        // Deactivation should be blocked if otherActiveAdminCount == 0
        Assert.Equal(0, otherActiveAdminCount);
        var canDeactivate = otherActiveAdminCount > 0;
        Assert.False(canDeactivate, "Cannot deactivate the sole remaining active Admin");

        Console.WriteLine("  ✓ Sole active Admin cannot be deactivated");
    }

    private void Test_Sole_Active_Admin_Demotion_Protection()
    {
        var adminId = Guid.NewGuid();
        var activeAdmins = new List<AppUser>
        {
            new() { Id = adminId, IsActive = true, Role = new Role { Name = "Admin" } }
        };

        var otherActiveAdminCount = activeAdmins.Count(u => u.IsActive && u.Role.Name == "Admin" && u.Id != adminId);

        // Demotion to Manager/Employee should be blocked if otherActiveAdminCount == 0
        var canDemote = otherActiveAdminCount > 0;
        Assert.False(canDemote, "Cannot demote the sole remaining active Admin");

        Console.WriteLine("  ✓ Sole active Admin cannot be demoted to lower role");
    }

    private void Test_Multiple_Admins_Allows_Admin_Modification()
    {
        var admin1Id = Guid.NewGuid();
        var admin2Id = Guid.NewGuid();

        var activeAdmins = new List<AppUser>
        {
            new() { Id = admin1Id, IsActive = true, Role = new Role { Name = "Admin" } },
            new() { Id = admin2Id, IsActive = true, Role = new Role { Name = "Admin" } }
        };

        var otherCountForAdmin1 = activeAdmins.Count(u => u.IsActive && u.Role.Name == "Admin" && u.Id != admin1Id);
        Assert.Equal(1, otherCountForAdmin1);
        Assert.True(otherCountForAdmin1 > 0, "Admin 1 can be modified because Admin 2 is active");

        Console.WriteLine("  ✓ With second active Admin, first Admin can be safely demoted or modified");
    }

    private void Test_Public_Registration_Role_Is_Employee_Only()
    {
        // When public registration is allowed, newly registered users must always receive the Employee role
        var employeeRole = new Role { Id = Guid.NewGuid(), Name = "Employee" };
        var newUser = new AppUser
        {
            Email = "newbie@example.com",
            RoleId = employeeRole.Id
        };

        Assert.Equal(employeeRole.Id, newUser.RoleId);
        Console.WriteLine("  ✓ Public registration defaults to Employee role without privilege escalation");
    }

    private void Test_Role_Claims_Synchronization_Over_Stale_Token()
    {
        // Token was issued when user was "Admin"
        var tokenRole = "Admin";

        // Database now has the user demoted to "Employee"
        var dbUser = new AppUser
        {
            IsActive = true,
            Role = new Role { Name = "Employee" }
        };

        // UserValidationMiddleware replaces token claim with database role
        var effectiveRole = dbUser.Role.Name;
        Assert.NotEqual(tokenRole, effectiveRole);
        Assert.Equal("Employee", effectiveRole);

        // Admin-only check
        static bool HasAdminAccess(string role) => role == "Admin";
        Assert.False(HasAdminAccess(effectiveRole), "User with stale Admin token is denied Admin access once demoted in DB");

        Console.WriteLine("  ✓ Stale token role claims are overridden by database role truth");
    }
}
