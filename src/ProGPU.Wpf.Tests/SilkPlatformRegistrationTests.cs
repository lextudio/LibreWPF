using System.Windows.Media.ProGPU;
using Xunit;

namespace ProGPU.Wpf.Tests;

/// <summary>
/// The explicit Silk platform registration only a NativeAOT application performs. The rule these tests pin: an application with dynamic code
/// (every JIT application) never registers, so its platform discovery stays exactly as before.
/// </summary>
public class SilkPlatformRegistrationTests
{
    [Fact]
    public void AnApplicationWithDynamicCode_NeverRegisters()
    {
        var registration = new SilkPlatformRegistration();
        var calls = 0;

        Assert.False(registration.EnsureRegistered(dynamicCodeSupported: true, () => calls++));
        Assert.False(registration.EnsureRegistered(dynamicCodeSupported: true, () => calls++));

        Assert.Equal(0, calls);
        Assert.False(registration.IsRegistered);
    }

    [Fact]
    public void AnApplicationWithoutDynamicCode_RegistersExactlyOnce()
    {
        var registration = new SilkPlatformRegistration();
        var calls = 0;

        Assert.True(registration.EnsureRegistered(dynamicCodeSupported: false, () => calls++));
        Assert.False(registration.EnsureRegistered(dynamicCodeSupported: false, () => calls++));

        Assert.Equal(1, calls);
        Assert.True(registration.IsRegistered);
    }

    [Fact]
    public void ARegistrationThatThrows_CanBeRetried_AndIsNotMarkedRegistered()
    {
        var registration = new SilkPlatformRegistration();
        var attempts = 0;

        Assert.Throws<InvalidOperationException>(() => registration.EnsureRegistered(false, () =>
        {
            attempts++;
            throw new InvalidOperationException("platform missing");
        }));
        Assert.False(registration.IsRegistered);

        Assert.True(registration.EnsureRegistered(false, () => attempts++));

        Assert.Equal(2, attempts);
        Assert.True(registration.IsRegistered);
    }

    [Fact]
    public void ConcurrentFirstWindows_NeverProceedBeforeTheRegistrationHasFinished()
    {
        // The second caller must not return (and go on to create its window) while the first is still registering.
        var registration = new SilkPlatformRegistration();
        using var registering = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var finished = 0;

        var first = Task.Run(() => registration.EnsureRegistered(false, () =>
        {
            registering.Set();
            Assert.True(release.Wait(TimeSpan.FromSeconds(10)));
            Volatile.Write(ref finished, 1);
        }));
        Assert.True(registering.Wait(TimeSpan.FromSeconds(10)));

        var second = Task.Run(() =>
        {
            var performed = registration.EnsureRegistered(false, () => throw new InvalidOperationException("must not register twice"));
            return (performed, finishedWhenItReturned: Volatile.Read(ref finished));
        });
        Assert.False(second.Wait(TimeSpan.FromMilliseconds(300)), "the second caller returned while the first was still registering");

        release.Set();

        Assert.True(first.Result);
        var (performed, finishedWhenItReturned) = second.Result;
        Assert.False(performed);
        Assert.Equal(1, finishedWhenItReturned);
    }
}
