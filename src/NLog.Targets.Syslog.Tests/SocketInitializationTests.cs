using System.Net.Sockets;
using NLog.Targets.Syslog.MessageSend;
using NLog.Targets.Syslog.Settings;
using ProtocolType = System.Net.Sockets.ProtocolType;
using Xunit;

namespace NLog.Targets.Syslog.Tests;

public class SocketInitializationTests
{
    [Fact]
    public void ForOs_Android_UsesLinuxConstants()
    {
        // What .NET reports on an Android device: not Linux, and no "linux" in the description.
        var result = SocketInitialization.ForOs(isWindows: false, isLinux: false, isAndroid: true,
                                                osDescription: "Android (API level 36)");

        Assert.IsType<SocketInitializationForLinux>(result);
    }

    [Theory]
    [InlineData("Linux 6.8.0-1017-azure #20-Ubuntu SMP")]
    [InlineData("Ubuntu 22.04 linux")]
    public void ForOs_Linux_UsesLinuxConstants(string osDescription)
    {
        var result = SocketInitialization.ForOs(isWindows: false, isLinux: true, isAndroid: false, osDescription);

        Assert.IsType<SocketInitializationForLinux>(result);
    }

    [Fact]
    public void ForOs_LinuxOnlyInDescription_UsesLinuxConstants()
    {
        var result = SocketInitialization.ForOs(isWindows: false, isLinux: false, isAndroid: false,
                                                osDescription: "Linux 5.10.0");

        Assert.IsType<SocketInitializationForLinux>(result);
    }

    [Theory]
    [InlineData("Darwin 24.0.0 Darwin Kernel Version 24.0.0")]
    [InlineData("iOS 26.0")]
    [InlineData(null)]
    public void ForOs_Darwin_UsesOsxConstants(string? osDescription)
    {
        var result = SocketInitialization.ForOs(isWindows: false, isLinux: false, isAndroid: false, osDescription!);

        Assert.IsType<SocketInitializationForOsx>(result);
    }

    [Fact]
    public void ForOs_Windows_UsesWindowsConstants()
    {
        var result = SocketInitialization.ForOs(isWindows: true, isLinux: false, isAndroid: false,
                                                osDescription: "Microsoft Windows 10.0.26100");

        Assert.IsType<SocketInitializationForWindows>(result);
    }

    [Fact]
    public void SendPendingDataOnClose_DisablesZeroTimeoutLinger()
    {
        // A zero-timeout linger makes close discard unsent data; a crashing app's last log goes with it.
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        socket.LingerState = new LingerOption(true, 0);

        SocketInitialization.ForCurrentOs().SendPendingDataOnClose(socket);

        Assert.False(socket.LingerState!.Enabled);
    }

    [Fact]
    public void SetKeepAlive_RejectedOptionValues_DoesNotThrowAndKeepsKeepAliveOn()
    {
        // The Android failure mode: the OS rejects the raw option values. That must not stop the
        // connection from being made.
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        var sut = new ThrowingSocketInitialization();

        sut.SetKeepAlive(socket, new KeepAliveConfig { Enabled = true });

        Assert.True(sut.ApplyCalled);
        Assert.NotEqual(0, (int)socket.GetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive)!);
    }

    [Fact]
    public void ForCurrentOs_CanApplyKeepAliveOnThisHost()
    {
        // Exercises the real constants for whatever OS runs the tests (CI covers Windows, Linux, macOS).
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        var config = new KeepAliveConfig { Enabled = true };
        var sut = SocketInitialization.ForCurrentOs();

        var ex = Record.Exception(() => ApplyDirectly(sut, socket, config));

        Assert.Null(ex);
    }

    private static void ApplyDirectly(SocketInitialization sut, Socket socket, KeepAliveConfig config)
    {
        // Bypass SetKeepAlive's catch so a wrong constant actually fails this test.
        typeof(SocketInitialization)
            .GetMethod("ApplyKeepAliveValues", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(sut, new object[] { socket, config });
    }

    private sealed class ThrowingSocketInitialization : SocketInitialization
    {
        public bool ApplyCalled { get; private set; }

        public override void DisableAddressSharing(Socket socket) { }

        protected override void ApplyKeepAliveValues(Socket socket, KeepAliveConfig keepAliveConfig)
        {
            ApplyCalled = true;
            throw new ApplicationException("Socket option syscall error value: '-1'");
        }
    }
}
