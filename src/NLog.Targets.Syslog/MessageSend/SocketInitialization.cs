// Licensed under the BSD license
// See the LICENSE file in the project root for more information

using System;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using NLog.Common;
using NLog.Targets.Syslog.Settings;

using Debug = System.Diagnostics.Debug;

namespace NLog.Targets.Syslog.MessageSend
{
    internal abstract class SocketInitialization
    {
        // netstandard2.0 has no OSPlatform.Android; this is what OperatingSystem.IsAndroid() checks
        private static readonly OSPlatform Android = OSPlatform.Create("ANDROID");

        public static SocketInitialization ForCurrentOs()
        {
            var osDescription = RuntimeInformation.OSDescription;
            var socketInitialization = ForOs(
                RuntimeInformation.IsOSPlatform(OSPlatform.Windows),
                RuntimeInformation.IsOSPlatform(OSPlatform.Linux),
                RuntimeInformation.IsOSPlatform(Android),
                osDescription);

            Debug.WriteLine($"===== NLog.Targets.Syslog detected OS: '{osDescription}', using {socketInitialization.GetType().Name}");

            return socketInitialization;
        }

        /// <summary>
        /// Picks the socket option constants for the running OS.
        /// <para>
        /// Android runs a Linux kernel, but .NET reports it as its own platform:
        /// <c>IsOSPlatform(OSPlatform.Linux)</c> is false and <c>OSDescription</c> is
        /// "Android (API level N)", with no "linux" in it. Without the explicit Android check it fell
        /// through to the macOS constants, which the Linux kernel rejects, so every TCP connection
        /// failed before it was opened and nothing was ever sent.
        /// </para>
        /// </summary>
        internal static SocketInitialization ForOs(bool isWindows, bool isLinux, bool isAndroid, string osDescription)
        {
            if (isWindows)
                return new SocketInitializationForWindows();

            if (isLinux || isAndroid || (osDescription ?? string.Empty).ToLowerInvariant().Contains("linux"))
                return new SocketInitializationForLinux();

            // macOS, iOS, Mac Catalyst, tvOS: all Darwin, all use the same constants
            return new SocketInitializationForOsx();
        }

        public abstract void DisableAddressSharing(Socket socket);

        /// <summary>
        /// Lets the OS finish sending queued data after the socket is closed.
        /// <para>
        /// A zero-timeout linger (the previous setting) makes close reset the connection and drop
        /// whatever is still in the send buffer. The OS closes the socket that way when the process
        /// dies, so the last messages before a crash - the ones that explain it - were lost even
        /// after a flush had reported them sent.
        /// </para>
        /// </summary>
        public void SendPendingDataOnClose(Socket socket)
        {
            socket.LingerState = new LingerOption(false, 0);
        }

        public void SetKeepAlive(Socket socket, KeepAliveConfig keepAliveConfig)
        {
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, keepAliveConfig.Enabled);
            if (!keepAliveConfig.Enabled)
                return;

            // Keepalive tuning is an optimisation, not a prerequisite for sending. If the OS rejects
            // the raw option values, keep SO_KEEPALIVE on with the OS defaults and carry on, rather
            // than failing the connection and with it every message.
            try
            {
                ApplyKeepAliveValues(socket, keepAliveConfig);
            }
            catch (Exception ex)
            {
                InternalLogger.Warn(ex, "[Syslog] Unable to apply keepalive values with {0}, using OS defaults", GetType().Name);
            }
        }

        protected abstract void ApplyKeepAliveValues(Socket socket, KeepAliveConfig keepAliveConfig);
    }
}
