using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace tik4net.Api
{  
    internal sealed class ApiConnection : ITikConnection, ITikConnectionCapabilities, ITikTlsConnection,
        ITikRawSentenceConnection, ITikSafeModeConnection, ITikTaggedConnection
    {
        ///// <summary>
        ///// Version of the login process. See https://wiki.mikrotik.com/wiki/Manual:API#Initial_login
        ///// </summary>
        //internal enum LoginProcessVersion
        //{
        //    /// <summary>
        //    /// Prior RouterOS version 6.43
        //    /// </summary>
        //    Version1,
        //    /// <summary>
        //    /// RouterOS version 6.43 and newer
        //    /// </summary>
        //    Version2,
        //}


        //Inspiration:
        // http://ayufan.eu/projects/rosapi/repository/entry/trunk/routeros.class.php
        // http://forum.mikrotik.com/viewtopic.php?f=9&t=31555&start=0

        private const int API_DEFAULT_PORT = 8728;
        private const int APISSL_DEFAULT_PORT = 8729;

        // Serializes writes: sentences must not interleave on the wire. A SemaphoreSlim rather than a lock
        // because the async path holds it across an await, which C# does not allow for a monitor — and two
        // different mutexes for the two paths would serialize neither against the other.
        private readonly System.Threading.SemaphoreSlim _writeLock = new System.Threading.SemaphoreSlim(1, 1);
        private volatile bool _isOpened = false;
        private bool _safeModeHeld = false;
        private bool _isSsl = false;
        private Encoding _encoding = Encoding.UTF8;
        // On since 4.0: the router echoes the tag back, and that is the only thing tying a reply to the
        // caller that asked for it. Off, two threads on one connection cross-deliver rows rather than fail.
        private bool _sendTagWithSyncCommand = true;
        private int _sendTimeout;
        private int _receiveTimeout = 30000;
        private TcpClient _tcpConnection = null!; // assigned by Open()/OpenAsync(), called right after construction
        private /*NetworkStream*/System.IO.Stream _tcpConnectionStream = null!; // assigned by Open()/OpenAsync()

        // One reader owns the socket for the connection's whole life; callers wait on their tag (P2.3).
        private readonly ApiSentenceDispatcher _dispatcher = new ApiSentenceDispatcher();
        private System.Threading.Tasks.Task? _readerTask;
        private volatile bool _readerStopRequested;

        public event EventHandler<TikConnectionCommCallbackEventArgs>? OnReadRow;
        public event EventHandler<TikConnectionCommCallbackEventArgs>? OnWriteRow;

        public bool DebugEnabled { get; set; }

        /// <summary>
        /// The binary API is the reference transport and natively supports every capability:
        /// CRUD, native <c>/listen</c>, streaming monitor windows (<c>.tag</c> + duration),
        /// raw <c>!re</c>/<c>!done</c>/<c>!trap</c> sentence access, per-command <c>.tag</c> multiplexing
        /// and connection-bound Safe Mode. It declares the full set explicitly (a positive declaration)
        /// rather than relying on the "no interface = supports everything" fallback.
        /// </summary>
        /// <remarks>
        /// <see cref="TikConnectionCapability.CancelInFlight"/> is real here rather than best-effort: the
        /// protocol has <c>/cancel tag=N</c>, the router answers the cancelled command with
        /// <c>!trap interrupted</c> + <c>!done</c>, and the sentence stream stays framed — so a cancelled
        /// command leaves the connection usable instead of merely un-desynchronized-by-luck. It is the
        /// per-command <c>.tag</c> (<see cref="TikConnectionCapability.Tagging"/>) that makes this possible.
        /// </remarks>
        public TikConnectionCapability Capabilities =>
            TikConnectionCapability.Crud | TikConnectionCapability.Listen
            | TikConnectionCapability.Streaming | TikConnectionCapability.RawSentences
            | TikConnectionCapability.Tagging | TikConnectionCapability.SafeMode
            | TikConnectionCapability.RawCommand
            | TikConnectionCapability.AsyncCommands | TikConnectionCapability.CancelInFlight;

        public bool IsOpened
        {
            get { return _isOpened; }
        }

        /// <summary>
        /// Wire text encoding for words. Defaults to UTF-8, matching RouterOS 7's own encoding
        /// (and the CLI-family transports). Set to <see cref="Encoding.ASCII"/> for legacy RouterOS 6.x
        /// routers if non-ASCII names/comments come back mangled.
        /// </summary>
        public Encoding Encoding
        {
            get { return _encoding; }
            set { _encoding = value; }
        }

        public bool SendTagWithSyncCommand
        {
            get { return _sendTagWithSyncCommand; }
            set { _sendTagWithSyncCommand = value; }
        }

        public int SendTimeout
        {
            get { return _sendTimeout; }
            set { _sendTimeout = value; }
        }

        public int ReceiveTimeout
        {
            get { return _receiveTimeout; }
            set { _receiveTimeout = value; }
        }

        /// <inheritdoc/>
        /// <remarks>Bounds the initial TCP handshake (and, on API-SSL, the TLS handshake).</remarks>
        public int ConnectTimeout { get; set; } = 15000;

        public bool IsSsl
        {
            get { return _isSsl; }
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Applies to API-SSL only. The default is <c>false</c> since 4.0: standard chain/hostname
        /// validation against the OS trust store.
        /// </remarks>
        public bool AllowInvalidCertificate { get; set; }

        /// <inheritdoc/>
        public RemoteCertificateValidationCallback? CertificateValidationCallback { get; set; }

        public ApiConnection(bool isSsl)
        {
            _isSsl = isSsl;
            DebugEnabled = System.Diagnostics.Debugger.IsAttached;
        }

        private void EnsureOpened()
        {
            if (!_isOpened)
                throw new TikConnectionNotOpenException("Connection has not been opened.");
        }

        public void Close()
        {
            try
            {
                if (IsOpened)
                {
                    if (!_isSsl)
                    {
                        //NOTE: returns !fatal => can not use standard ExecuteNonQuery call (should not throw exception)
                        var responseSentences = CallCommandSync(new string[] { "/quit" });
                        //TODO should return single response of ApiFatalSentence with message "session terminated on request" - test and warning if not?
                    }
                    else
                    {
                        //NOTE: No result returned when SSL & /quit => do not read response (possible bug in SSL-API?)
                        WriteCommand(new string[] { "/quit" });
                    }
                }
            }
            catch(IOException)
            {
                // catch exception if connection is closed
            }

            DisposeConnectionResources();
        }

        // Disposes the TCP/SSL resources without throwing — safe to call from Close(), Dispose(),
        // and from a failed Open()/OpenAsync() to avoid leaking a half-opened socket.
        private void DisposeConnectionResources()
        {
            // Tell the reader this is our doing before pulling the socket out from under it, so a caller
            // waiting on a tag is told "closed by the client" rather than being handed the socket error that
            // closing produced. The blocked read ends when the stream goes.
            _readerStopRequested = true;
            try { _tcpConnectionStream?.Dispose(); } catch { /* Close/Dispose must not throw */ }
            try { _tcpConnection?.Dispose(); } catch { /* Close/Dispose must not throw */ }
            _isOpened = false;

            // Bounded: the reader is blocked in a read on a stream that has just been disposed, so it is
            // about to throw. Waiting keeps "closed" meaning the reader is actually gone — a Dispose that
            // leaves a live reader behind is how a test suite accumulates threads on a pooled connection.
            var reader = _readerTask;
            _readerTask = null;
            try { reader?.Wait(2000); } catch { /* the loop's own exception is its business */ }

            // Whatever the reader did or did not manage to publish, nobody may still be waiting on a
            // connection that no longer exists.
            _dispatcher.TerminateAll(new ApiFatalSentence(new[] { "connection closed by the client" }));
        }

        /// <inheritdoc/>
        public void SafeModeTake()
        {
            EnsureOpened();
            // RouterOS 7.18+ scriptable safe-mode. Bound to this API session: an unexpected
            // disconnect (without a SafeModeRelease) rolls back everything changed since.
            CreateCommand("/safe-mode/take").ExecuteNonQuery();
            _safeModeHeld = true;
        }

        /// <inheritdoc/>
        public void SafeModeRelease()
        {
            EnsureOpened();
            CreateCommand("/safe-mode/release").ExecuteNonQuery();
            _safeModeHeld = false;
        }

        /// <inheritdoc/>
        public void SafeModeUnroll()
        {
            EnsureOpened();
            CreateCommand("/safe-mode/unroll").ExecuteNonQuery();
            _safeModeHeld = false;
        }

        /// <inheritdoc/>
        public bool SafeModeGet() => _safeModeHeld;

        public void Open(string host, string user, string password)
        {
            Open(host, _isSsl ? APISSL_DEFAULT_PORT : API_DEFAULT_PORT, user, password);
        }

        // Open and OpenAsync are ONE implementation, the async one, with the synchronous entry point
        // blocking on it (P2.5; the same D5 inversion the CLI, REST and WinBox-native CRUD paths use).
        // They used to be two near-identical copies, and they had already drifted where it mattered: the
        // synchronous copy negotiated TLS with `AuthenticateAsClientAsync(host, null, SslProtocols.None,
        // false)` and translated a handshake failure into TikConnectionSSLErrorException, while the async
        // copy called the one-argument overload and let a raw AuthenticationException out. So the same
        // ApiSsl connection reported a certificate problem differently depending on which method opened it.
        // Every await below carries ConfigureAwait(false), which is what keeps the blocking entry point
        // safe under a UI / ASP.NET-classic SynchronizationContext.
        public void Open(string host, int port, string user, string password)
            => OpenAsync(host, port, user, password).GetAwaiter().GetResult();

        public System.Threading.Tasks.Task OpenAsync(string host, string user, string password)
            => OpenAsync(host, _isSsl ? APISSL_DEFAULT_PORT : API_DEFAULT_PORT, user, password);

        public async System.Threading.Tasks.Task OpenAsync(string host, int port, string user, string password)
        {
            try
            {
                //open connection
                _tcpConnection = new TcpClient();
                if (_sendTimeout > 0)
                    _tcpConnection.SendTimeout = _sendTimeout;
                if (_receiveTimeout > 0)
                    _tcpConnection.ReceiveTimeout = _receiveTimeout;

                // Task.WhenAny + Task.Delay so we work on netstandard2.0 (no ConnectAsync(CancellationToken) overload there).
                var connectTask = _tcpConnection.ConnectAsync(host, port);
                var timeoutTask = System.Threading.Tasks.Task.Delay(ConnectTimeout);
                if (await System.Threading.Tasks.Task.WhenAny(connectTask, timeoutTask).ConfigureAwait(false) == timeoutTask)
                {
                    // Observe the abandoned connect so a later "connection refused" cannot surface as an
                    // unobserved task exception in an unrelated part of the process.
                    _ = connectTask.ContinueWith(t => { _ = t.Exception; },
                        System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted);
                    throw new SocketException((int)SocketError.TimedOut);
                }
                await connectTask.ConfigureAwait(false); // observe/rethrow any connect exception

                var tcpStream = _tcpConnection.GetStream();
                if (_receiveTimeout > 0)
                    tcpStream.ReadTimeout = _receiveTimeout;
                if (_sendTimeout > 0)
                    tcpStream.WriteTimeout = _sendTimeout;
                if (!_isSsl)
                {
                    _tcpConnectionStream = tcpStream;
                }
                else
                {
                    var sslStream = new SslStream(tcpStream, false,
                        new RemoteCertificateValidationCallback(ValidateServerCertificate), null);

                    try
                    {
                        // SslProtocols.None lets the OS negotiate the best available version (TLS 1.2/1.3).
                        // TLS 1.0 (the former explicit value) is disabled on modern systems and RouterOS 7+.
                        await sslStream.AuthenticateAsClientAsync(host, null, SslProtocols.None, false)
                            .ConfigureAwait(false);
                    }
                    catch (AuthenticationException ex)
                    {
                        throw new TikConnectionSSLErrorException(ex);
                    }
                    _tcpConnectionStream = sslStream;
                }

                _isOpened = true;
                StartReaderLoop();        // login is an ordinary exchange — it goes through the reader too
                await Login_v3Async(user, password).ConfigureAwait(false);
            }
            catch
            {
                // Do not leak a half-opened socket when Open fails at any stage (connect, SSL auth, login) —
                // the caller never gets a connection object back to Dispose.
                DisposeConnectionResources();
                throw;
            }
        }

        /// <summary>
        /// The RouterOS login exchange — one sentence on 6.43+, two on the pre-6.43 challenge/response
        /// protocol. Awaited rather than blocking: the handshake is the part that costs the round trips, so
        /// running it synchronously on the caller's thread would leave <c>OpenAsync</c> asynchronous in name
        /// only.
        /// </summary>
        private async System.Threading.Tasks.Task Login_v3Async(string user, string password)
        {
            try
            {
                ApiCommand loginCommand = new ApiCommand(this, "/login", TikCommandParameterFormat.NameValue,
                    new ApiCommandParameter("name", user), new ApiCommandParameter("password", password)); //parameters will be ignored with old login protocol

                var responseHashOrNull = await loginCommand.LoginScalarOrDefaultAsync().ConfigureAwait(false);

                //old login protocol
                if (!string.IsNullOrEmpty(responseHashOrNull))
                {
                    //login connection
                    // non-null inside the IsNullOrEmpty guard; netstandard2.0's BCL cannot tell the compiler so
                    string hashedPass = ApiConnectionHelper.EncodePassword(password, responseHashOrNull!);
                    ApiCommand loginCommand2 = new ApiCommand(this, "/login", TikCommandParameterFormat.NameValue,
                        new ApiCommandParameter("name", user), new ApiCommandParameter("response", hashedPass));
                    await loginCommand2.LoginNonQueryAsync().ConfigureAwait(false);
                }
            }
            catch(TikCommandTrapException ex)
            {
                if (ex.Message == "cannot log in")
                    throw new TikConnectionLoginException(ex);
                else if (ex.Message.StartsWith("invalid user name or password"))
                    throw new TikConnectionLoginException(ex);
                else
                    throw;
            }
        }

        internal bool ValidateServerCertificate(object sender, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors sslPolicyErrors)
        {
            if (CertificateValidationCallback != null)
                return CertificateValidationCallback(sender, certificate, chain, sslPolicyErrors);
            if (AllowInvalidCertificate)
                return true;
            return sslPolicyErrors == SslPolicyErrors.None;
        }

        public void Dispose()
        {
            if (_isOpened)
            {
                try { Close(); } catch { /* Dispose must not throw */ }
            }
        }

        // Thrown only when ReadByte() returns -1 (peer closed the TCP connection).
        // Distinct from general IOException (e.g. timeout) so GetAll() can treat it as !fatal.
        private sealed class TikEofException : IOException
        {
            public TikEofException() : base("Connection closed by remote host.") { }
        }

        private byte ReadByteChecked()
        {
            int b = _tcpConnectionStream.ReadByte();
            if (b < 0)
                throw new TikEofException();
            return (byte)b;
        }

        private long ReadWordLength()
        {
            byte readByte = ReadByteChecked();
            int length;

            if ((readByte & 0x80) == 0x00)
            {
                length = readByte;
            }
            else if ((readByte & 0xC0) == 0x80)
            {
                length = ((readByte & 0x3F) << 8) + ReadByteChecked();
            }
            else if ((readByte & 0xE0) == 0xC0)
            {
                length = ((readByte & 0x1F) << 8) + ReadByteChecked();
                length = (length << 8) + ReadByteChecked();
            }
            else if ((readByte & 0xF0) == 0xE0)
            {
                length = ((readByte & 0x0F) << 8) + ReadByteChecked();
                length = (length << 8) + ReadByteChecked();
                length = (length << 8) + ReadByteChecked();
            }
            else if (readByte == 0xF0)
            {
                // 5-byte encoding: 0xF0 + four bytes (network order)
                length =                  ReadByteChecked();
                length = (length << 8) + ReadByteChecked();
                length = (length << 8) + ReadByteChecked();
                length = (length << 8) + ReadByteChecked();
            }
            else
            {
                // Control bytes 0xF1–0xFF are reserved by the protocol
                throw new IOException($"Unexpected control byte 0x{readByte:X2} in word length.");
            }

            return length;
        }

        private string ReadWord(bool skipEmptyRow)
        {
            string result;

            do
            {
                long wordLength = ReadWordLength();

                if (wordLength == 0)
                {
                    result = "";
                }
                else
                {
                    // Rented, not allocated: every word of every row of every read used to leave a byte[]
                    // behind, and a 1000-row load is tens of thousands of them. The buffer never escapes
                    // this block - the string is materialized before it goes back to the pool.
                    int length = (int)wordLength;
                    byte[] buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(length);
                    try
                    {
                        int totalRead = 0;
                        while (totalRead < length)
                        {
                            int n = _tcpConnectionStream.Read(buffer, totalRead, length - totalRead);
                            if (n == 0)
                                throw new IOException("Connection closed while reading word body.");
                            totalRead += n;
                        }
                        result = Encoding.GetString(buffer, 0, length);

                        if (Diagnostics.TikWireTrace.Enabled)
                            Diagnostics.TikWireTrace.Emit("api.word", Diagnostics.TikWireDir.Recv,
                                buffer, 0, length, "len=" + wordLength);
                    }
                    finally
                    {
                        // A rented buffer is only as long as ITS OWN length, which is >= what was asked for -
                        // hence `length` above rather than buffer.Length at every use.
                        System.Buffers.ArrayPool<byte>.Shared.Return(buffer);
                    }
                }
            } while (skipEmptyRow && string.IsNullOrWhiteSpace(result));

            if (OnReadRow != null)
                OnReadRow(this, new TikConnectionCommCallbackEventArgs(result));
            if (DebugEnabled)
                System.Diagnostics.Debug.WriteLine("< " + result);
            return result;
        }

        private ITikSentence ReadSentence()
        {
            try
            {
                string sentenceName = ReadWord(true);

                List<string> sentenceWords = new List<string>();
                string sentenceWord;
                do
                {
                    sentenceWord = ReadWord(false);
                    if (!string.IsNullOrWhiteSpace(sentenceWord)) //read ending empty row, but skip it from result
                        sentenceWords.Add(sentenceWord);
                } while (!string.IsNullOrWhiteSpace(sentenceWord));

                switch (sentenceName)
                {
                    case "!done":  return new ApiDoneSentence(sentenceWords);
                    case "!trap":  return new ApiTrapSentence(sentenceWords);
                    case "!re":    return new ApiReSentence(sentenceWords);
                    case "!fatal": return new ApiFatalSentence(sentenceWords);
                    case "!empty": return ReadSentence(); // RouterOS 7.18+: data sentence meaning "no rows", always followed by !done — skip it and return the real final sentence
                    case "": throw new IOException("Can not read sentence from connection"); // With SSL possibly not logged in
                    default: throw new NotImplementedException(string.Format("Response type '{0}' not supported", sentenceName));
                }
            }
            catch(IOException ex)
            {
                _isOpened = _tcpConnection.Connected;
                if (IsTimeout(ex))
                    throw new TikConnectionReceiveTimeoutException(_receiveTimeout, ex);
                throw;
            }
        }

        // True when the IOException wraps a socket read/write timeout (NetworkStream.ReadTimeout/WriteTimeout
        // elapsed), as opposed to e.g. the peer resetting the connection.
        private static bool IsTimeout(IOException ex)
            => ex.InnerException is SocketException se && se.SocketErrorCode == SocketError.TimedOut;

        private void WriteCommand(IEnumerable<string> commandRows)
        {
            try
            {
                foreach (string row in commandRows)
                {
                    byte[] bytes = _encoding.GetBytes(row.ToCharArray());
                    byte[] length = ApiConnectionHelper.EncodeLength(bytes.Length);

                    _tcpConnectionStream.Write(length, 0, length.Length); //write length of comming sentence
                    _tcpConnectionStream.Write(bytes, 0, bytes.Length);   //write sentence body

                    if (Diagnostics.TikWireTrace.Enabled)
                        Diagnostics.TikWireTrace.Emit("api.word", Diagnostics.TikWireDir.Send,
                            bytes, 0, bytes.Length, "len=" + bytes.Length);

                    if (OnWriteRow != null)
                        OnWriteRow(this, new TikConnectionCommCallbackEventArgs(row));
                    if (DebugEnabled)
                        System.Diagnostics.Debug.WriteLine("> " + row);
                }

                _tcpConnectionStream.WriteByte(0); //final zero byte (sentence terminator)
                _tcpConnectionStream.Flush();
            }
            catch(IOException)
            {
                _isOpened = _tcpConnection.Connected;
                throw;
            }
        }

        // ── Reader loop (P2.3) ────────────────────────────────────────────────

        /// <summary>
        /// Starts the one thread that reads this connection's socket. Runs from just after the stream is
        /// ready — the login exchange goes through it like any other command — until the connection ends.
        /// </summary>
        private void StartReaderLoop()
        {
            _readerStopRequested = false;

            // The socket read must NOT carry ReceiveTimeout any more. That value bounds a caller waiting for
            // its answer, and a reader that sits on an idle socket between commands would otherwise read its
            // own idleness as a failure and kill a perfectly healthy connection every ReceiveTimeout. The
            // deadline now lives where the waiting happens: ApiSentenceDispatcher.Wait.
            try { _tcpConnectionStream.ReadTimeout = System.Threading.Timeout.Infinite; }
            catch (InvalidOperationException) { /* stream does not support timeouts — nothing to relax */ }

            _readerTask = System.Threading.Tasks.Task.Factory.StartNew(
                ReaderLoop, System.Threading.Tasks.TaskCreationOptions.LongRunning);
        }

        private void ReaderLoop()
        {
            try
            {
                while (!_readerStopRequested)
                    _dispatcher.Push(ReadSentence());
            }
            catch (Exception ex)
            {
                // Every caller learns of this, not just whoever happened to own the read. Carry the reason:
                // an empty !fatal makes a router reboot, a socket error and a bug in our own reader
                // indistinguishable, and this is the only place the exception exists (P2.14).
                _isOpened = false;
                _dispatcher.TerminateAll(_readerStopRequested
                    ? new ApiFatalSentence(new[] { "connection closed by the client" })
                    : new ApiFatalSentence(new[] { "connection lost: " + ex.GetType().Name + ": " + ex.Message }));
                return;
            }

            _isOpened = false;
            _dispatcher.TerminateAll(new ApiFatalSentence(new[] { "connection closed by the client" }));
        }

        // Async sibling of WriteCommand. Same framing, same trace hooks; the difference is that it awaits the
        // socket instead of blocking a thread on it, and holds the write lock across that await — which is
        // why the lock is a SemaphoreSlim.
        private async System.Threading.Tasks.Task WriteCommandAsync(
            IEnumerable<string> commandRows, System.Threading.CancellationToken cancellationToken)
        {
            await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                foreach (string row in commandRows)
                {
                    byte[] bytes = _encoding.GetBytes(row.ToCharArray());
                    byte[] length = ApiConnectionHelper.EncodeLength(bytes.Length);

                    await _tcpConnectionStream.WriteAsync(length, 0, length.Length).ConfigureAwait(false);
                    await _tcpConnectionStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);

                    if (Diagnostics.TikWireTrace.Enabled)
                        Diagnostics.TikWireTrace.Emit("api.word", Diagnostics.TikWireDir.Send,
                            bytes, 0, bytes.Length, "len=" + bytes.Length);

                    OnWriteRow?.Invoke(this, new TikConnectionCommCallbackEventArgs(row));
                    if (DebugEnabled)
                        System.Diagnostics.Debug.WriteLine("> " + row);
                }

                await _tcpConnectionStream.WriteAsync(new byte[] { 0 }, 0, 1).ConfigureAwait(false); //sentence terminator
                await _tcpConnectionStream.FlushAsync().ConfigureAwait(false);
            }
            catch (IOException)
            {
                _isOpened = _tcpConnection.Connected;
                throw;
            }
            finally
            {
                _writeLock.Release();
            }
        }

        private ITikSentence GetOne(string tag)
        {
            // A sentence already delivered for this tag is returned even after the connection dropped: it is
            // this caller's answer, and the router had sent it before anything went wrong.
            return _dispatcher.Wait(tag, _receiveTimeout);
        }
        
        private IEnumerable<ITikSentence> GetAll(string tag)
        {
            // NOTE: !trap is always followed by !done (keep reading). !fatal closes the connection immediately — no !done follows.
            // NOTE: yield return/break cannot be inside catch blocks in C# iterators — use a flag instead.
            ITikSentence sentence = null!; // always reassigned by GetOne() before being read below; the catch path below yield-breaks instead
            do
            {
                TikEofException? eofException = null;
                try
                {
                    sentence = GetOne(tag);
                }
                catch (TikEofException ex)
                {
                    eofException = ex;
                }

                if (eofException != null)
                {
                    // Remote peer closed the TCP connection (e.g. router rebooted/shutdown after accepting the command).
                    // Yield a synthetic !fatal so callers handle this uniformly.
                    yield return new ApiFatalSentence(Array.Empty<string>());
                    yield break;
                }

                yield return sentence;
            } while (!(sentence is ApiDoneSentence || sentence is ApiFatalSentence));
        }

        private static readonly Regex tagRegex = new Regex($"^\\{TikSpecialProperties.Tag}=(?<TAG>.+)$"); // .tag=1234

        /// <summary>
        /// Finds the <c>.tag</c> a caller put in the command, or <c>null</c> when there is none.
        /// </summary>
        /// <remarks>
        /// Two spellings reach here, differing by one character and by route: the connection writes its own
        /// tag as the bare row <c>.tag=N</c>, while a caller supplying it as a command parameter produces
        /// <c>=.tag=N</c>. Both have to be recognised, and missing one is invisible rather than loud: the
        /// command goes out correctly tagged, the router answers it correctly, and the client waits on the
        /// untagged queue until <see cref="ReceiveTimeout"/> elapses for an answer that has already arrived —
        /// a full timeout per such command (<c>ApiCallerSuppliedTagTests</c>).
        /// </remarks>
        private static string? FindTag(IEnumerable<string> commandRows)
        {
            foreach (var row in commandRows)
            {
                if (row == null) continue;
                var match = tagRegex.Match(row.StartsWith("=", StringComparison.Ordinal) ? row.Substring(1) : row);
                if (match.Success)
                    return match.Groups["TAG"].Value;
            }
            return null;
        }
        public IEnumerable<ITikSentence> CallCommandSync(params string[] commandRows)
        {
            EnsureOpened();

            //read .tag from commandRows - if present
            var tagOrEmptyString = FindTag(commandRows) ?? string.Empty;

            if (_sendTagWithSyncCommand && string.IsNullOrEmpty(tagOrEmptyString))
            {
                tagOrEmptyString = TagSequence.Next().ToString();
                commandRows = commandRows.Concat(new string[] { string.Format("{0}={1}", TikSpecialProperties.Tag, tagOrEmptyString) }).ToArray();
            }

            _writeLock.Wait();
            try { WriteCommand(commandRows); }
            finally { _writeLock.Release(); }
            return GetAll(tagOrEmptyString).ToList();
        }

        public IEnumerable<ITikSentence> CallCommandSync(IEnumerable<string> commandRows)
        {
            return CallCommandSync(commandRows.ToArray());
        }

        /// <summary>
        /// Task-based sibling of <see cref="CallCommandSync(string[])"/>: writes the command and awaits its
        /// sentences without holding a thread.
        /// </summary>
        /// <remarks>
        /// The <c>Sync</c> in <see cref="CallCommandSync(string[])"/> is historical — it distinguished the
        /// blocking call from the callback pattern, not from anything awaitable — which is why this one is
        /// not called <c>CallCommandSyncAsync</c>. It is internal, so the name costs nothing to keep
        /// straight; the public low-level surface is <see cref="ITikRawSentenceConnection"/> and remains
        /// synchronous only.
        /// </remarks>
        /// <remarks>
        /// <para>
        /// The command is <b>always</b> tagged, whatever <see cref="SendTagWithSyncCommand"/> says. A tag is
        /// what makes the answer addressable, and it is also what makes cancelling possible at all — the
        /// router's <c>/cancel</c> takes a tag, so an untagged command could only be "cancelled" by giving up
        /// on the connection. The single exception is <paramref name="forceTag"/>.
        /// </para>
        /// <para>
        /// Cancellation is level 2 of the contract: the token triggers a real <c>/cancel tag=N</c>, the
        /// router answers <c>!trap interrupted</c> + <c>!done</c>, both are consumed, and the connection is
        /// left fully usable — which is the property worth testing, far more than the exception type.
        /// </para>
        /// </remarks>
        /// <param name="commandRows">The sentence to send, one word per row.</param>
        /// <param name="cancellationToken">Cancels the command; see the remarks for what that means here.</param>
        /// <param name="forceTag">
        /// <c>false</c> makes this follow <see cref="SendTagWithSyncCommand"/> exactly as
        /// <see cref="CallCommandSync(string[])"/> does, instead of tagging unconditionally. Used by the login
        /// exchange and nothing else: login is not cancellable, and it is the one command whose bytes must
        /// stay identical between the synchronous and asynchronous open — a tag we started adding here would
        /// be a wire change on the most sensitive exchange there is, including against pre-6.43 routers that
        /// no test here can reach.
        /// </param>
        internal async System.Threading.Tasks.Task<IList<ITikSentence>> CallCommandAsync(
            string[] commandRows, System.Threading.CancellationToken cancellationToken, bool forceTag = true)
        {
            EnsureOpened();
            cancellationToken.ThrowIfCancellationRequested();   // level 0: nothing is written

            string tag = FindTag(commandRows) ?? string.Empty;
            if (string.IsNullOrEmpty(tag) && (forceTag || _sendTagWithSyncCommand))
            {
                tag = TagSequence.Next().ToString();
                commandRows = commandRows.Concat(new[] { $"{TikSpecialProperties.Tag}={tag}" }).ToArray();
            }

            await WriteCommandAsync(commandRows, cancellationToken).ConfigureAwait(false);

            var result = new List<ITikSentence>();

            // The cancel is driven by the TOKEN, not by the next sentence arriving. Sending it from inside
            // the read loop looks equivalent and is not: a command that has gone quiet — precisely the case
            // a caller cancels — leaves the loop parked in the wait, so the router is never asked to stop
            // and the cancel only takes effect at the receive timeout, if at all.
            System.Threading.Tasks.Task? cancelTask = null;
            using (cancellationToken.Register(() => cancelTask = SendCancelAsync(tag)))
            {
                while (true)
                {
                    ITikSentence sentence;
                    try
                    {
                        // The wait itself is NOT given the caller's token: abandoning it would leave the
                        // router still answering a tag nobody is reading. We ask the router to stop and keep
                        // reading until it says it has — that is what keeps the connection usable afterwards.
                        sentence = await _dispatcher.WaitAsync(tag, _receiveTimeout,
                            System.Threading.CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (TikConnectionReceiveTimeoutException)
                    {
                        // Cancelled and silent: the caller asked to stop and the router said nothing at all.
                        // Report the cancellation — the timeout is a symptom of it, not the news.
                        cancellationToken.ThrowIfCancellationRequested();
                        throw;
                    }

                    result.Add(sentence);

                    if (sentence is ApiDoneSentence || sentence is ApiFatalSentence)
                        break;
                }
            }

            if (cancelTask != null)
                await cancelTask.ConfigureAwait(false);   // observe it; it swallows its own failures

            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }

        // Asks the router to abandon a running command. Sent on its own tag so its own !done cannot be
        // mistaken for the cancelled command's; failures are swallowed because the caller is already
        // cancelling and a cancel that could not be delivered still ends with the command's own !done.
        //
        // The target tag goes as `=tag=N`, NOT `=.tag=N`: it is an argument of /cancel, not this sentence's
        // own tag. That is the spelling ApiCommand.CancelInternal has used since 3.x. Both were measured on
        // 7.23.2 (a cancelled 5-count ping returns in 684 ms either way, against ~5 s uncancelled), so the
        // router accepts both — this one is kept because it is the one with years of evidence behind it and
        // because two cancel paths spelling the same thing differently is how a difference becomes a bug.
        private async System.Threading.Tasks.Task SendCancelAsync(string tag)
        {
            try
            {
                await WriteCommandAsync(
                    new[] { "/cancel", $"=tag={tag}",
                            $"{TikSpecialProperties.Tag}={TagSequence.Next()}" },
                    System.Threading.CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Nothing to do: we are cancelling, and the read loop below still ends on the command's
                // own terminal sentence or on the receive timeout.
            }
        }

        // Internal since 4.0: it hands back a Thread nobody can await, cancel or observe a failure on, so it
        // is no longer offered to callers (ITikCommand.ExecuteAsync and the Task-based Execute*Async
        // extensions are). ApiCommand still drives the callback form through it — the Thread it returns is
        // what ApiCommand.Cancel joins.
        internal Thread CallCommandAsync(IEnumerable<string> commandRows, string tag,
            Action<ITikSentence> oneResponseCallback)
        {
            Guard.ArgumentNotNullOrEmptyString(tag, "tag");
            EnsureOpened();

            commandRows = commandRows.Concat(new string[] { string.Format("{0}={1}", TikSpecialProperties.Tag, tag) }); // .tag=1234
            _writeLock.Wait();
            try { WriteCommand(commandRows); }
            finally { _writeLock.Release(); }

            // The thread pumps this tag's sentences into the callback — it does NOT touch the socket. Before
            // P2.3 it was one of many readers, so a slow callback ran with nobody servicing the connection
            // and every command's deadline depended on what else was in flight (F8). The signature stays a
            // Thread because it is public API; what it does underneath is now dispatch, not I/O.
            Thread result = new Thread(() =>
            {
                try
                {
                    ITikSentence sentence;
                    do
                    {
                        sentence = GetOne(tag);
                        try
                        {
                            oneResponseCallback(sentence);
                        }
                        catch
                        {
                            //Do not crash reading thread because of implementation error in called code
                        }
                    } while (!(sentence is ApiDoneSentence /*|| sentence is ApiTrapSentence*/ || sentence is ApiFatalSentence)); // read sentences via TryGetOne(wait) for TAG until !done or !fatal is returned
                    //NOTE: Should be ended via !done or !trap+!done (called via Cancel() command for specific tag)
                    // The loop no longer tests _isOpened: a closed connection reaches the callback as the
                    // reader's synthetic !fatal, which ends it. Testing the flag instead ended the pump
                    // silently, leaving ExecuteAsync's caller with a monitor that simply stopped.
                }
                catch (Exception ex)
                {
                    // Everything that can still land here is local (a receive timeout on this tag). Connection
                    // loss arrives as the reader's !fatal, above. Either way the caller (ExecuteAsync) must be
                    // told, or it never clears its running state — carry the reason (P2.14).
                    try
                    {
                        oneResponseCallback(new ApiFatalSentence(
                            new[] { "connection lost while reading tag " + tag + ": "
                                    + ex.GetType().Name + ": " + ex.Message }));
                    }
                    catch { /* callback is caller code — never let it mask the fatal */ }
                }
            });
            result.IsBackground = true;
            result.Start();

            return result;
        }

        public ITikCommand CreateCommand()
        {
            return new ApiCommand(this);
        }

        public ITikCommand CreateCommand(TikCommandParameterFormat defaultParameterFormat)
        {
            var result = CreateCommand();
            result.DefaultParameterFormat = defaultParameterFormat;

            return result;
        }


        public ITikCommand CreateCommand(string commandText, params ITikCommandParameter[] parameters)
        {
            return new ApiCommand(this, commandText, parameters);
        }

        public ITikCommand CreateCommand(string commandText, TikCommandParameterFormat defaultParameterFormat, params ITikCommandParameter[] parameters)
        {
            var result = CreateCommand(commandText, parameters);
            result.DefaultParameterFormat = defaultParameterFormat;

            return result;
        }


        public ITikCommand CreateCommandAndParameters(string commandText, params string[] parameterNamesAndValues)
        {
            var result = new ApiCommand(this, commandText);
            result.AddParameterAndValues(parameterNamesAndValues);

            return result;
        }

        public ITikCommand CreateCommandAndParameters(string commandText, TikCommandParameterFormat defaultParameterFormat, params string[] parameterNamesAndValues)
        {
            var result = CreateCommandAndParameters(commandText, parameterNamesAndValues);
            result.DefaultParameterFormat = defaultParameterFormat;

            return result;
        }

        public ITikCommandParameter CreateParameter(string name, string value)
        {
            return new ApiCommandParameter(name, value);
        }

        public ITikCommandParameter CreateParameter(string name, string value, TikCommandParameterFormat parameterFormat)
        {
            var result = CreateParameter(name, value);
            result.ParameterFormat = parameterFormat;

            return result;
        }
    }
}
