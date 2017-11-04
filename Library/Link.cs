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

namespace InvertedTomato.TikLink {

    // TODO: mandatory properties
    public class Link : IDisposable {
        /// <summary>
        /// Connect without SSL on the default port.
        /// </summary>
        public static Link Connect(string host, string username, string password) { return Connect(host, 8728, username, password); }

        /// <summary>
        /// Connect without SSL on a specified port.
        /// </summary>
        public static Link Connect(string host, int port, string username, string password) {
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
        /// Connect with SSL on the default port, using the host name to validate the remote CA-signed certificate. 
        /// </summary>
        /// <remarks>
        /// This will only work if the router has a properly CA-signed certificate installed.
        /// </remarks>
        public static Link ConnectSecure(string host, string username, string password) { return ConnectSecure(host, 8729, null, username, password); }

        /// <summary>
        /// Connect with SSL on the default port, using a public key to identify the remote router.
        /// </summary>
        /// <remarks>
        /// This is handy, so you don't need to install a CA-signed certificate on the router.
        /// </remarks>
        public static Link ConnectSecure(string host, byte[] publicKey, string username, string password) { return ConnectSecure(host, 8729, publicKey, username, password); }

        /// <summary>
        /// Connect with SSL on a specified port, using a public key to identify the remote router.
        /// </summary>
        /// <remarks>
        /// This can be handy, so you don't need to install a CA-signed certificate on the router.
        /// </remarks>
        public static Link ConnectSecure(string host, int port, byte[] publicKey, string username, string password) {
            if (null == host) {
                throw new ArgumentNullException(nameof(host));
            }
            if (null == username) {
                throw new ArgumentNullException(nameof(username));
            }
            if (null == password) {
                throw new ArgumentNullException(nameof(password));
            }

            // Connect
            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
            socket.Connect(host, port);
            var socketStream = new NetworkStream(socket, true);

            // Wrap stream in SSL
            var sslStream = new SslStream(socketStream, false, new RemoteCertificateValidationCallback((object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors) => {
                if (null == publicKey) { // TODO: May need refinement
                    return sslPolicyErrors == SslPolicyErrors.None;
                } else {
                    return publicKey.SequenceEqual(certificate.GetPublicKey());
                }
            }), null);
            sslStream.AuthenticateAsClientAsync(host).Wait();

            return new Link(sslStream, true, username, password);
        }

        /// <summary>
        /// If the link is disposed and no longer usable.
        /// </summary>
        public bool IsDisposed { get; private set; }

        /// <summary>
        /// The most recent "fatal" sentence received. NULL if none has been received.
        /// </summary>
        public Sentence LastFatal { get; private set; }

        private readonly Thread ReadThread;
        private readonly Stream UnderlyingStream;
        private readonly bool OwnsUnderlyingStream;
        private readonly object Sync = new object();
        private readonly ConcurrentDictionary<string, Result> PendingResults = new ConcurrentDictionary<string, Result>();

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
            var r1 = Call("/login").Wait();
            var challenge = r1.GetDoneAttribute("ret");

            // Compute response
            var hash = PasswordEncoding.Hash(password, challenge);

            // Attempt login
            var r2 = Call("/login", new Dictionary<string, string>() { { "name", username }, { "response", hash } }).Wait();
            if (r2.IsError) {
                r2.TryGetTrapAttribute("message", out var message);
                throw new AccessDeniedException(message);
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
                if (!IsDisposed) {
                    throw ex;
                }
            }
        }

        /// <summary>
        /// Call a command without any attributes.
        /// </summary>
        public Result Call(string command) {
            return Call(new Sentence() {
                Command = command
            });
        }

        /// <summary>
        /// Call a command with attributes.
        /// </summary>
        public Result Call(string command, Dictionary<string, string> attributes) {
            return Call(new Sentence() {
                Command = command,
                Attributes = attributes
            });
        }

        /// <summary>
        /// Call a command with attributes and a set of queries (filters).
        /// </summary>
        public Result Call(string command, Dictionary<string, string> attributes, List<string> queries) {
            return Call(new Sentence() {
                Command = command,
                Attributes = attributes,
                Queries = queries
            });
        }

        /// <summary>
        /// Call a command with a pre-rolled sentence.
        /// </summary>
        public Result Call(Sentence tx) {
            if (null == tx) {
                throw new ArgumentNullException(nameof(tx));
            }
            if (string.IsNullOrWhiteSpace(tx.Command)) {
                throw new ArgumentException("Command must not be blank or whitespace", nameof(tx));
            }

            // Get next tag
            var tag = Interlocked.Increment(ref NextTag).ToString();

            // Allocate result
            var result = new Result();
            PendingResults[tag] = result;

            lock (Sync) {
                // Write words
                WriteWord(tx.Command);
                foreach (var attribute in tx.Attributes) {
                    WriteWord("=" + attribute.Key + "=" + attribute.Value);
                }
                foreach (var query in tx.Queries) {
                    WriteWord("?" + query);
                }
                WriteWord(".tag=" + tag);

                // Write zero-length-word to finish sentence
                WriteWord(string.Empty);
            }

            return result;
        }

        public IList<T> Scan<T>(List<string> includeProperties = null, List<string> query = null) where T : IRecord, new() {
            // Build sentence
            var sentence = new Sentence();
            sentence.Command = RecordReflection.GetPath<T>() + "/print";
            if (null != includeProperties) {
                sentence.Attributes[".proplist"] = string.Join(",", includeProperties);
            }
            if (null != query) {
                sentence.Queries = query;
            }

            // Make call
            var result = Call(sentence).Wait();
            if (result.IsError) {
                throw new QueryFailedException();
            }

            // Convert record sentences records
            var output = new List<T>();
            foreach (var s in result.Sentences) {
                if (s.Command == "re") {
                    var record = new T();
                    RecordReflection.SetProperties(record, s.Attributes);
                    output.Add(record);
                }
            }
            return output;
        }

        public T Get<T>(string id, List<string> includeProperties = null) where T : IRecord, new() {
            if (null == id) {
                throw new ArgumentNullException(nameof(id));
            }

            var scan = Scan<T>(includeProperties, new List<string>() { "id=" + id });

            if (scan.Count == 0) {
                throw new KeyNotFoundException();
            }

            return scan.SingleOrDefault();
        }

        public void Set<T>(T record) where T : IRecord, new() {
            if (null == record) {
                throw new ArgumentNullException(nameof(record));
            }

            // Build sentence
            var sentence = new Sentence();
            sentence.Command = RecordReflection.GetPath<T>() + (record.Id == null ? "/add" : "/set");
            sentence.Attributes = RecordReflection.GetProperties(record);

            // Make call
            var result = Call(sentence).Wait();
            if (result.IsError) {
                throw new QueryFailedException();
            }
        }

        public IList<T> Delete<T>(string id) where T : IRecord, new() {
            throw new NotImplementedException();
        }

        public IList<T> Move<T>(string id, string afterId) where T : IRecord, new() {
            throw new NotImplementedException();
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


        private string ReadWord() {
            // Get length
            var length = LengthEncoding.ReadLength(UnderlyingStream);
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
            LengthEncoding.WriteLength(payload.Length, UnderlyingStream);
            UnderlyingStream.Write(payload, 0, payload.Length);
        }
    }
}
