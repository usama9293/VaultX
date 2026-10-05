using PasswordManager.Domain.Entities;

namespace PasswordManager.UnitTests.Domain;

public class UserLockoutTests
{
    [Fact]
    public void NewUser_StartsWithNoFailedAttemptsOrLock()
    {
        var user = CreateUser();

        Assert.Equal(0, user.FailedLoginAttempts);
        Assert.Null(user.LockedUntil);
        Assert.False(user.IsLocked(DateTime.UtcNow));
    }

    [Fact]
    public void RecordFailedLogin_IncrementsConsecutiveFailureCount()
    {
        var user = CreateUser();
        var now = DateTime.UtcNow;

        user.RecordFailedLogin(now);

        Assert.Equal(1, user.FailedLoginAttempts);
        Assert.Null(user.LockedUntil);
    }

    [Fact]
    public void FifthFailure_ActivatesFifteenMinuteLock()
    {
        var user = CreateUser();
        var now = DateTime.UtcNow;

        for (var attempt = 0; attempt < User.LoginFailureThreshold; attempt++)
        {
            user.RecordFailedLogin(now);
        }

        Assert.Equal(User.LoginFailureThreshold, user.FailedLoginAttempts);
        Assert.Equal(now.Add(User.LoginLockoutDuration), user.LockedUntil);
        Assert.True(user.IsLocked(now));
    }

    [Fact]
    public void RecordFailedLogin_DuringActiveLockDoesNotIncreaseCount()
    {
        var user = CreateUser();
        var now = DateTime.UtcNow;
        LockUser(user, now);

        user.RecordFailedLogin(now.AddMinutes(1));

        Assert.Equal(User.LoginFailureThreshold, user.FailedLoginAttempts);
        Assert.Equal(now.Add(User.LoginLockoutDuration), user.LockedUntil);
    }

    [Fact]
    public void ClearExpiredLockout_ResetsStateLazily()
    {
        var user = CreateUser();
        var now = DateTime.UtcNow;
        LockUser(user, now);

        user.ClearExpiredLockout(now.Add(User.LoginLockoutDuration));

        Assert.Equal(0, user.FailedLoginAttempts);
        Assert.Null(user.LockedUntil);
    }

    [Fact]
    public void FailureAfterExpiredLockStartsNewConsecutiveSequence()
    {
        var user = CreateUser();
        var now = DateTime.UtcNow;
        LockUser(user, now);

        user.RecordFailedLogin(now.Add(User.LoginLockoutDuration));

        Assert.Equal(1, user.FailedLoginAttempts);
        Assert.Null(user.LockedUntil);
    }

    [Fact]
    public void ResetLoginFailures_ClearsCountAndLock()
    {
        var user = CreateUser();
        LockUser(user, DateTime.UtcNow);

        user.ResetLoginFailures();

        Assert.Equal(0, user.FailedLoginAttempts);
        Assert.Null(user.LockedUntil);
    }

    private static User CreateUser()
    {
        return new User("lockout@example.test", new byte[] { 1, 2, 3 });
    }

    private static void LockUser(User user, DateTime utcNow)
    {
        for (var attempt = 0; attempt < User.LoginFailureThreshold; attempt++)
        {
            user.RecordFailedLogin(utcNow);
        }
    }
}
