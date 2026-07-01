using System;
using System.ServiceModel;
using System.Threading;
using System.Threading.Tasks;
using DSDaemon.Contracts;

namespace DSDaemon {
    /// <summary>
    /// Manages the WCF duplex channel to Run8.
    /// Connect() is idempotent; dispose to close cleanly.
    /// </summary>
    public sealed class Run8Connector : IDisposable {
        private readonly string _host;
        private readonly int    _port;
        private readonly DispatcherCallback _callback;
        private readonly Action<string, ConsoleColor> _log;

        private DuplexChannelFactory<IRun8>? _factory;
        private IRun8?                        _channel;

        /// <summary>
        /// The live IRun8 channel. Non-null after Connect() returns successfully.
        /// </summary>
        public IRun8? Channel => _channel;

        public Run8Connector(string host, int port, DispatcherCallback callback,
                             Action<string, ConsoleColor> log) {
            _host     = host;
            _port     = port;
            _callback = callback;
            _log      = log;
        }

        public void Connect() {
            var uri     = $"net.tcp://{_host}:{_port}/Run8";
            var binding = new NetTcpBinding(SecurityMode.None) {
                MaxReceivedMessageSize = 4_000_000,
                OpenTimeout            = TimeSpan.FromSeconds(30),
                CloseTimeout           = TimeSpan.FromSeconds(10),
                SendTimeout            = TimeSpan.FromSeconds(30),
                ReceiveTimeout         = TimeSpan.MaxValue,
            };
            binding.ReaderQuotas.MaxArrayLength        = 131_072;
            binding.ReaderQuotas.MaxStringContentLength = 131_072;

            _log($"[CONN] Connecting to {uri} ...", ConsoleColor.Gray);

            var context = new InstanceContext(_callback);
            _factory    = new DuplexChannelFactory<IRun8>(context, binding, new EndpointAddress(uri));
            _channel    = _factory.CreateChannel();

            ((IClientChannel)_channel).Open();
            _log($"[CONN] Channel open — registering as dispatcher", ConsoleColor.Green);

            _channel.BeginDispatcherConnected(null, null);
            _log($"[CONN] DispatcherConnected sent — listening for callbacks", ConsoleColor.Green);
        }

        /// <summary>
        /// Blocks until the channel faults or is closed externally.
        /// Returns when the session ends.
        /// </summary>
        public async Task WaitAsync(CancellationToken ct) {
            if (_channel == null) throw new InvalidOperationException("Not connected");

            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var ch  = (IClientChannel)_channel;

            ch.Faulted += (_, _) => {
                _log("[CONN] Channel faulted", ConsoleColor.Red);
                tcs.TrySetResult(false);
            };
            ch.Closed += (_, _) => {
                _log("[CONN] Channel closed", ConsoleColor.Yellow);
                tcs.TrySetResult(true);
            };

            await using (ct.Register(() => tcs.TrySetCanceled(ct)))
                await tcs.Task.ConfigureAwait(false);
        }

        public void Dispose() {
            try {
                if (_channel is IClientChannel ch && ch.State != CommunicationState.Faulted)
                    ch.Close();
            } catch { /* swallow — best-effort close */ }

            try { _factory?.Close(); } catch { }
        }
    }
}
