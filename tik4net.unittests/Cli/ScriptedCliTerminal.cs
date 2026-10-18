#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using tik4net.Cli;

namespace tik4net.unittests.Cli
{
    /// <summary>
    /// A console the test scripts: each member answers through a delegate. The settle (Tab, F1, resync) and streaming
    /// drivers are optional, and <see cref="SupportsQuietRead"/> / <see cref="SupportsLineStreaming"/> say whether
    /// each was given, so a fake without them takes the paths a terminal without them would.
    /// </summary>
    internal sealed class ScriptedCliTerminal : ICliTerminal
    {
        private readonly Func<string, CancellationToken, Task<string>> _send;
        private readonly Func<byte[], CancellationToken, Task<string>> _sendRaw;
        private readonly Action _close;
        private readonly Func<byte[], int, CancellationToken, Task<string>>? _settle;
        private readonly Func<string, Action<string>, CancellationToken, Task<string>>? _streaming;

        /// <param name="send">Answers a command.</param>
        /// <param name="sendRaw">Answers keystrokes read to the next prompt.</param>
        /// <param name="close">Runs when the console is ended or disposed.</param>
        /// <param name="settle">Answers keystrokes read until quiet; none = no Tab completion, F1 or resync.</param>
        /// <param name="streaming">Answers a command line by line; none = no streaming read.</param>
        internal ScriptedCliTerminal(Func<string, CancellationToken, Task<string>> send,
            Func<byte[], CancellationToken, Task<string>> sendRaw, Action close,
            Func<byte[], int, CancellationToken, Task<string>>? settle = null,
            Func<string, Action<string>, CancellationToken, Task<string>>? streaming = null)
        {
            _send = send;
            _sendRaw = sendRaw;
            _close = close;
            _settle = settle;
            _streaming = streaming;
        }

        public Task<string> SendCommandAndReadAsync(string command, Action<string>? onLine, CancellationToken ct)
            => onLine == null ? _send(command, ct)
             : (_streaming ?? throw new NotSupportedException("no streaming driver"))(command, onLine, ct);

        public Task<string> SendRawAndReadAsync(byte[] raw, CancellationToken ct) => _sendRaw(raw, ct);

        public Task<string> SendRawAndReadUntilQuietAsync(byte[] raw, int quietMs, CancellationToken ct)
            => (_settle ?? throw new NotSupportedException("no settle driver"))(raw, quietMs, ct);

        public bool SupportsQuietRead => _settle != null;

        public bool SupportsLineStreaming => _streaming != null;

        public void End(bool answerSafeModeQuestion) => _close();

        public void Dispose() => _close();
    }

    /// <summary>The base of the unit tests' fake CLI connections: they open over a <see cref="ScriptedCliTerminal"/>.</summary>
    internal abstract class ScriptedCliConnectionBase : CliConnectionBase
    {
        /// <summary>Opens over a scripted console, through the open every CLI transport takes.</summary>
        private protected void OpenScripted(ScriptedCliTerminal terminal, Func<CancellationToken, Task>? login = null)
            => OpenTerminal(() => new CliTerminalOpen(terminal, login ?? (_ => Task.FromResult(0))), retryRefusedLogin: false);

        /// <summary>As <see cref="OpenScripted(ScriptedCliTerminal, Func{CancellationToken, Task})"/>, the console given
        /// by its delegates.</summary>
        private protected void OpenScripted(Func<CancellationToken, Task> login,
            Func<string, CancellationToken, Task<string>> send,
            Func<byte[], CancellationToken, Task<string>> sendRaw, Action close,
            Func<byte[], int, CancellationToken, Task<string>>? settle = null,
            Func<string, Action<string>, CancellationToken, Task<string>>? streaming = null)
            => OpenScripted(new ScriptedCliTerminal(send, sendRaw, close, settle, streaming), login);
    }
}
