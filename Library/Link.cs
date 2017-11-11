using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Linq;
using InvertedTomato.TikLink.Records;
using InvertedTomato.TikLink.RecordHandlers;

namespace InvertedTomato.TikLink {
    public class Link : IDisposable {
        /// <summary>
        /// Open a new link to a router.
        /// </summary>
        public static Link Connect(string host, string username, string password, int port = 8728) {
            if (null == host) {
                throw new ArgumentNullException(nameof(host));
            }
            if (null == username) {
                throw new ArgumentNullException(nameof(username));
            }
            if (null == password) {
                throw new ArgumentNullException(nameof(password));
            }
            if (port < 1) {
                throw new ArgumentOutOfRangeException(nameof(port));
            }

            // Connect
            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
            socket.Connect(host, port);
            var socketStream = new NetworkStream(socket, true);

            return new Link(socketStream, true, username, password);
        }

        /// <summary>
        /// Open a new SSL link to a router.
        /// </summary>
        public static Link ConnectSecure(string host, string username, string password, string publicKey = null, int port = 8729) {
            if (null == host) {
                throw new ArgumentNullException(nameof(host));
            }
            if (null == username) {
                throw new ArgumentNullException(nameof(username));
            }
            if (null == password) {
                throw new ArgumentNullException(nameof(password));
            }
            if (port < 1) {
                throw new ArgumentOutOfRangeException(nameof(port));
            }

            // Connect
            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
            socket.Connect(host, port);
            var socketStream = new NetworkStream(socket, true);

            // Wrap stream in SSL
            var sslStream = new SslStream(socketStream, false, new RemoteCertificateValidationCallback((object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors) => {
                var inboundPublicKey = BitConverter.ToString(certificate.GetPublicKey()).Replace("-", "");

                if (null == publicKey) {
                    return sslPolicyErrors == SslPolicyErrors.None;
                } else {
                    return publicKey == inboundPublicKey;
                }
            }), null);
            sslStream.AuthenticateAsClientAsync(host).Wait();

            return new Link(sslStream, true, username, password);
        }

        /// <summary>
        /// Get the router's public key for use with ConnectSecure.
        /// </summary>
        public static string GetSecurePublicKey(string host, int port = 8729) {
            if (null == host) {
                throw new ArgumentNullException(nameof(host));
            }
            if (port < 1) {
                throw new ArgumentOutOfRangeException(nameof(port));
            }

            string inboundPublicKey = null;
            using (var socket = new Socket(SocketType.Stream, ProtocolType.Tcp)) {
                socket.Connect(host, port);
                using (var socketStream = new NetworkStream(socket, true)) {
                    var sslStream = new SslStream(socketStream, false, new RemoteCertificateValidationCallback((object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors) => {
                        inboundPublicKey = BitConverter.ToString(certificate.GetPublicKey()).Replace("-", "");
                        return true;
                    }), null);
                    sslStream.AuthenticateAsClientAsync(host).Wait();
                }
            }

            return inboundPublicKey;
        }

        /// <summary>
        /// Create a new certificate, enable the secure API and set the new certificate for use with the API.
        /// </summary>
        /// <remarks>
        /// This is the easiest way to get setup ready for ConnectSecure. If there is already a certificate setup, it will be replaced (read: the public key will change).
        /// </remarks>
        public static void EnableSecure(string host, string username, string password, int port = 8728, int days = 3650) {
            if (days < 1) {
                throw new ArgumentOutOfRangeException(nameof(days));
            }

            using (var link = Link.Connect(host, username, password, port)) {
                // Create certificate
                var cert = new SystemCertificate() {
                    Name = DateTime.UtcNow.ToString(),
                    CommonName = "self-signed",
                    DaysValid = days,
                    KeyUsage = "key-cert-sign,tls-server"
                    //Trusted = true
                };
                link.System.Certificate.Add(cert);

                // Get certificate
                cert = link.System.Certificate.Query(new Dictionary<string, string>(){
                    {nameof(SystemCertificate.Name), $"={cert.Name}" }
                }, null).Single();

                // Sign certificate
                link.System.Certificate.Sign(cert);

                // Enable API-SSL and set it to use the new certificate
                var sslApi = link.Ip.Service.Query(new Dictionary<string, string>() {
                    {nameof(IpService.Name), "=api-ssl" }
                }, null).Single();
                sslApi.Certificate = cert.Name;
                sslApi.Disabled = false;
                link.Ip.Service.Update(sslApi);
            }
        }

        /// <summary>
        /// If the link is disposed and no longer usable.
        /// </summary>
        public bool IsDisposed { get; private set; }

        /// <summary>
        /// The most recent "fatal" sentence received. NULL if none has been received.
        /// </summary>
        public Sentence LastFatal { get; private set; }

        /// <summary>
        /// Access the router's Bridge functionality.
        /// </summary>
        public readonly LinkBridge Bridge;

        /// <summary>
        /// Access the router's CapsMan functionality.
        /// </summary>
        public readonly LinkCapsMan CapsMan;

        /// <summary>
        /// Access the router's Interface functionality.
        /// </summary>
        public readonly LinkInterface Interface;

        /// <summary>
        /// Access the router's IPv4 functionality.
        /// </summary>
        public readonly LinkIp Ip;

        /// <summary>
        /// Access the router's Queue functionality.
        /// </summary>
        public readonly LinkQueue Queue;

        /// <summary>
        /// Access the router's System functionality.
        /// </summary>
        public readonly LinkSystem System;

        /// <summary>
        /// Access the router's Tools.
        /// </summary>
        public readonly LinkTool Tool;

        private readonly Thread ReadThread;
        private readonly Stream UnderlyingStream;
        private readonly bool OwnsUnderlyingStream;
        private readonly object Sync = new object();
        private readonly ConcurrentDictionary<string, CallResult> PendingResults = new ConcurrentDictionary<string, CallResult>();

        private long NextTag;

        public Link(Stream stream, bool ownsStream, string username, string password) {
            if (null == stream) {
                throw new ArgumentNullException(nameof(stream));
            }
            if (null == username) {
                throw new ArgumentNullException(nameof(username));
            }
            if (null == password) {
                throw new ArgumentNullException(nameof(password));
            }

            // Store options
            UnderlyingStream = stream;
            OwnsUnderlyingStream = ownsStream;

            // Start read thread
            ReadThread = new Thread(ReadThread_Spin);
            ReadThread.Start();

            // Get challenge
            var result1 = Call("/login", new Dictionary<string, string>()).Wait();
            var challenge = result1.GetDoneAttribute("ret");

            // Compute response
            var hash = Password.Hash(password, challenge);

            // Attempt login
            var result2 = Call("/login", new Dictionary<string, string>() { { "name", username }, { "response", hash } }).Wait();
            if (result2.IsError) {
                result2.TryGetTrapAttribute("message", out var message);
                throw new CallException(message);
            }

            // Setup record handler interfaces
            Bridge = new LinkBridge(this);
            CapsMan = new LinkCapsMan(this);
            Interface = new LinkInterface(this);
            Ip = new LinkIp(this);
            Queue = new LinkQueue(this);
            System = new LinkSystem(this);
            Tool = new LinkTool(this);
        }

        /// <summary>
        /// Call a raw command. You shouldn't normally need this, it's for advanced users.
        /// </summary>
        /// <remarks>
        /// Internally attributes are converted to words in the following format "={attrib:key}={attrib:value}".
        /// </remarks>
        public CallResult Call(string command, Dictionary<string, string> attributes) {
            return Call(new Sentence() {
                Command = command,
                Attributes = attributes
            });
        }

        /// <summary>
        /// Call a raw command with a set of queries (filters). You shouldn't normally need this, it's for advanced users.
        /// </summary>
        /// <remarks>
        /// Internally attributes are converted to words in the following format "={attrib:key}={attrib:value}". Queries are converted as "?{query}".
        /// </remarks>
        public CallResult Call(string command, Dictionary<string, string> attributes, List<string> queries) {
            return Call(new Sentence() {
                Command = command,
                Attributes = attributes,
                Queries = queries
            });
        }

        /// <summary>
        /// Call a raw command with a pre-rolled sentence. You shouldn't normally need this, it's for advanced users.
        /// </summary>
        /// <remarks>
        /// Internally attributes are converted to words in the following format "={attrib:key}={attrib:value}". Queries are converted as "?{query}".
        /// </remarks>
        public CallResult Call(Sentence sentence) {
            if (null == sentence) {
                throw new ArgumentNullException(nameof(sentence));
            }
            if (string.IsNullOrWhiteSpace(sentence.Command)) {
                throw new ArgumentException("Command must not be blank or whitespace", nameof(sentence));
            }

            // Get next tag
            var tag = Interlocked.Increment(ref NextTag).ToString();

            // Allocate result
            var result = new CallResult(this, tag);
            PendingResults[tag] = result;

            lock (Sync) {
                // Write words
                WriteWord(sentence.Command);
                foreach (var attribute in sentence.Attributes) {
                    WriteWord("=" + attribute.Key + "=" + attribute.Value);
                }
                foreach (var query in sentence.Queries) {
                    WriteWord("?" + query);
                }
                WriteWord(".tag=" + tag);

                // Write zero-length-word to finish sentence
                WriteWord(string.Empty);
            }

            return result;
        }

        /// <summary>
        /// Disconnect and dispose link.
        /// </summary>
        public void Dispose() {
            Dispose(true);
        }

        protected virtual void Dispose(bool disposing) {
            lock (Sync) {
                if (IsDisposed) {
                    return;
                }
                IsDisposed = true;

                if (disposing) {
                    if (OwnsUnderlyingStream) {
                        UnderlyingStream?.Dispose();
                    }

                    ReadThread?.Join();
                }
            }
        }

        private void ReadThread_Spin(object obj) {
            int pos;
            string key;
            string value;

            try {
                while (!IsDisposed) {
                    // Read sentence
                    var tag = string.Empty;
                    var sentence = new Sentence();
                    while (true) {
                        // Read word
                        var word = ReadWord();
                        if (word.Length == 0) {
                            break;
                        }

                        // Process word into sentence
                        switch (word[0]) {
                            case '!':
                                sentence.Command = word.Substring(1);
                                break;
                            case '=':
                                pos = word.IndexOf("=", 1);
                                key = word.Substring(1, pos - 1);
                                value = word.Substring(pos + 1);

                                sentence.Attributes[key] = value;
                                break;
                            case '.':
                                pos = word.IndexOf("=");
                                key = word.Substring(1, pos - 1);

                                switch (key) {
                                    case "tag":
                                        tag = word.Substring(pos + 1);
                                        break;
                                    default:
                                        throw new ProtocolViolationException($"Unknown API attribute '{word}'.");
                                }
                                break;
                            default:
                                throw new ProtocolViolationException($"Unknown word type for '{word}'.");
                        }
                    }

                    if (sentence.Command == "fatal") {
                        LastFatal = sentence;
                        continue;
                    }

                    // Handle error condictions
                    if (IsDisposed) {
                        return;
                    }
                    if (tag == string.Empty) {
                        throw new ProtocolViolationException($"Missing tag.");
                    }

                    // Get add sentence to result
                    if (!PendingResults.TryGetValue(tag, out var result)) {
                        throw new ProtocolViolationException($"Unexpected tag '{tag}'.");
                    }
                    result.Sentences.Add(sentence);

                    // Process special commands
                    switch (sentence.Command) {
                        case "re":
                            continue;
                        case "trap":
                            result.IsError = true;
                            continue;
                        case "done":
                            result.IsDone = true;
                            break;
                        default:
                            throw new ProtocolViolationException($"Unexpected command '{sentence.Command}'.");
                    }

                    // Remove from pending results
                    PendingResults.TryRemove(tag, out result);

                    // Release any waiting threads
                    result.Block.Set();
                }
            } catch (IOException ex) {
                if (IsDisposed) {
                    return;
                }

                Dispose();
            }
        }

        private string ReadWord() {
            // Get length
            var length = WordLength.ReadLength(UnderlyingStream);
            if (length == 0) {
                return string.Empty; // End of sentence
            }

            // Get payload
            var payload = new byte[length];
            var pos = 0;
            do {
                var len = UnderlyingStream.Read(payload, pos, length - pos);
                if (len < 0) {
                    throw new IOException("End of stream");
                }
                pos += len;
            } while (pos < length);

            // Decode
            return Encoding.ASCII.GetString(payload);
        }

        private void WriteWord(string word) {
            // Encode payload
            var payload = Encoding.ASCII.GetBytes(word);

            // Write word
            WordLength.WriteLength(payload.Length, UnderlyingStream);
            UnderlyingStream.Write(payload, 0, payload.Length);
        }
    }
}
