using System;
using System.Collections.Generic;
using DSDaemon.Messages;
using Xunit;

namespace DSDaemon.Tests.Scenarios {
    /// <summary>
    /// Dispatcher permission level transitions.
    /// Granted = full control (green)
    /// Observer = read-only view (yellow)
    /// Rescinded / NoChange = no access (red)
    /// </summary>
    public class PermissionTests {

        [Fact]
        public void GrantedPermission_LogsGreen() {
            var logs = Capture(out var cb);
            cb.PermissionUpdate(new DispatcherPermissionMessage {
                Permission   = EDispatcherPermission.Granted,
                AIPermission = true,
            });

            Assert.Single(logs);
            Assert.Equal(ConsoleColor.Green, logs[0].Color);
            Assert.Contains("Granted", logs[0].Text);
        }

        [Fact]
        public void ObserverPermission_LogsYellow() {
            var logs = Capture(out var cb);
            cb.PermissionUpdate(new DispatcherPermissionMessage {
                Permission   = EDispatcherPermission.Observer,
                AIPermission = false,
            });

            Assert.Equal(ConsoleColor.Yellow, logs[0].Color);
            Assert.Contains("Observer", logs[0].Text);
        }

        [Fact]
        public void RescindedPermission_LogsRed() {
            var logs = Capture(out var cb);
            cb.PermissionUpdate(new DispatcherPermissionMessage {
                Permission   = EDispatcherPermission.Rescinded,
                AIPermission = false,
            });

            Assert.Equal(ConsoleColor.Red, logs[0].Color);
            Assert.Contains("Rescinded", logs[0].Text);
        }

        [Fact]
        public void NoChangePermission_LogsRed() {
            // NoChange is documented as "appears to be unused" in iecc8; treat as no-access.
            var logs = Capture(out var cb);
            cb.PermissionUpdate(new DispatcherPermissionMessage {
                Permission   = EDispatcherPermission.NoChange,
                AIPermission = false,
            });

            Assert.Equal(ConsoleColor.Red, logs[0].Color);
        }

        [Fact]
        public void AIPermission_IsIncludedInOutput() {
            var logs = Capture(out var cb);
            cb.PermissionUpdate(new DispatcherPermissionMessage {
                Permission   = EDispatcherPermission.Granted,
                AIPermission = true,
            });

            Assert.Contains("AI=True", logs[0].Text);
        }

        [Fact]
        public void PermissionTransition_GrantedToRescinded_SecondLogIsRed() {
            // Simulate Run8 revoking access mid-session.
            var logs = Capture(out var cb);

            cb.PermissionUpdate(new DispatcherPermissionMessage {
                Permission = EDispatcherPermission.Granted, AIPermission = true });
            cb.PermissionUpdate(new DispatcherPermissionMessage {
                Permission = EDispatcherPermission.Rescinded, AIPermission = false });

            Assert.Equal(2, logs.Count);
            Assert.Equal(ConsoleColor.Green, logs[0].Color);
            Assert.Equal(ConsoleColor.Red,   logs[1].Color);
        }

        [Fact]
        public void PermissionTransition_GrantedToObserver_SecondLogIsYellow() {
            // Downgrade to observer (read-only, e.g. not the active dispatcher session).
            var logs = Capture(out var cb);

            cb.PermissionUpdate(new DispatcherPermissionMessage {
                Permission = EDispatcherPermission.Granted, AIPermission = true });
            cb.PermissionUpdate(new DispatcherPermissionMessage {
                Permission = EDispatcherPermission.Observer, AIPermission = false });

            Assert.Equal(ConsoleColor.Yellow, logs[1].Color);
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private static List<(string Text, ConsoleColor Color)> Capture(out DispatcherCallback cb) {
            var logs = new List<(string, ConsoleColor)>();
            cb = new DispatcherCallback((msg, color) => logs.Add((msg, color)));
            return logs;
        }
    }
}
