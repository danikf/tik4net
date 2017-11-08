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
using InvertedTomato.TikLink.Vanity;

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
        public static Link ConnectSecure(string host, string username, string password, int port = 8729, byte[] publicKey = null) {
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
                throw new IOException("Access denied - invalid username and/or password");
            }

            // Setup vanity interfaces
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
        public CallResult Call(Sentence tx) {
            if (null == tx) {
                throw new ArgumentNullException(nameof(tx));
            }
            if (string.IsNullOrWhiteSpace(tx.Command)) {
                throw new ArgumentException("Command must not be blank or whitespace", nameof(tx));
            }

            // Get next tag
            var tag = Interlocked.Increment(ref NextTag).ToString();

            // Allocate result
            var result = new CallResult(tag);
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

        /// <summary>
        /// Cancel a call that hasn't yet completed. You shouldn't normally need this, it's for advanced users.
        /// </summary>
        public void Cancel(CallResult result) { // TODO: Test
            if (null == result) {
                throw new ArgumentNullException(nameof(result));
            }

            var result2 = Call("/cancel", new Dictionary<string, string>() {
                {"tag", result.Tag }
            }).Wait();

            if (result2.IsError) {
                result2.TryGetTrapAttribute("message", out var message);
                throw new CallException(message);
            }
        }

        /// <summary>
        /// Retreive a list of all records of a given type.
        /// </summary>
        /// <remarks>
        /// While it's perfectly fine to use these methods directly, it's intended that you use the vanity methods instead (eg. link.Ip.Arp.List()).
        /// </remarks>
        /// <typeparam name="T"></typeparam>
        /// <param name="properties">Properties to include in listing. Reduces amount of data required for the call. NULL returns all properties.</param>
        /// <param name="filter">Only include records whos fields match this filter.</param>
        /// <returns></returns>
        public IList<T> List<T>(string[] properties = null, IDictionary<string, string> filter = null) where T : IRecord, new() {
            // Build sentence
            var sentence = new Sentence();
            sentence.Command = RecordReflection.GetPath<T>() + "/print";
            if (null != properties) {
                sentence.Attributes[".proplist"] = string.Join(",", properties.Select(a => RecordReflection.ResolveProperty<T>(a)));
            }
            if (null != filter) {
                foreach (var f in filter) {
                    string k;
                    try {
                        k = RecordReflection.ResolveProperty<T>(f.Key);
                    } catch (KeyNotFoundException) {
                        throw new ArgumentException($"Unknown filter field '{f.Key}'.", nameof(filter));
                    }
                    if (f.Value.Length < 1) {
                        throw new ArgumentException($"Filter value must be at least 1 character long.", nameof(filter));
                    }
                    var v = f.Value.Substring(1);
                    var op = f.Value.Substring(0, 1);
                    if (op != ">" && op != "<" && op != "=") {
                        throw new ArgumentException($"Unknown filter operation '{op}' on '{f.Key}'.", nameof(filter));
                    }
                    sentence.Queries.Add($"{op}{k}={v}");
                }
            }

            // Make call
            var result = Call(sentence).Wait();
            if (result.IsError) {
                result.TryGetTrapAttribute("message", out var message);
                throw new CallException(message);
            }

            // Convert record sentences records
            var output = new List<T>();
            foreach (var s in result.Sentences) {
                if (s.Command == "re") {
                    var record = new T();
                    RecordReflection.SetRosProperties(record, s.Attributes);
                    output.Add(record);
                }
            }

            return output;
        }

        /// <summary>
        /// Retrieve a single object with a specific ID.
        /// </summary>
        /// <remarks>
        /// While it's perfectly fine to use these methods directly, it's intended that you use the vanity methods instead (eg. link.Ip.Arp.Get()).
        /// </remarks>
        /// <typeparam name="T"></typeparam>
        /// <param name="id"></param>
        /// <param name="properties">Properties to include in listing. Reduces amount of data required for the call. NULL returns all properties.</param>
        /// <returns></returns>
        public T Get<T>(string id, string[] properties = null) where T : ISetRecord, new() {
            if (null == id) {
                throw new ArgumentNullException(nameof(id));
            }

            var scan = List<T>(properties, new Dictionary<string, string>() { { "Id", $"={id}" } });

            if (scan.Count != 1) {
                throw new CallException($"Record with ID '{id}' not found.");
            }

            return scan.Single();
        }

        /// <summary>
        /// Retrieve a single object where the router only provides one record (a singleton).
        /// </summary>
        /// <remarks>
        /// While it's perfectly fine to use these methods directly, it's intended that you use the vanity methods instead (eg. link.Ip.Arp.Get()).
        /// </remarks>
        /// <typeparam name="T"></typeparam>
        /// <param name="properties">Properties to include in listing. Reduces amount of data required for the call. NULL returns all properties.</param>
        /// <returns></returns>
        public T Get<T>(string[] properties = null) where T : ISingleRecord, new() {
            var scan = List<T>(properties, new Dictionary<string, string>());

            if (scan.Count != 1) {
                throw new CallException($"Record with not found.");
            }

            return scan.Single();
        }

        /// <summary>
        /// Add a new record to a set of records.
        /// </summary>
        /// <remarks>
        /// While it's perfectly fine to use these methods directly, it's intended that you use the vanity methods instead (eg. link.Ip.Arp.Get()).
        /// </remarks>
        /// <typeparam name="T"></typeparam>
        /// <param name="record"></param>
        /// <param name="properties"></param>
        public void Add<T>(T record, string[] properties = null) where T : ISetRecord, new() {
            if (null == record) {
                throw new ArgumentNullException(nameof(record));
            }

            // Get attributes
            var attributes = RecordReflection.GetRosProperties(record);

            // If filtering properties, remove attributes not wanted
            if (null != properties) {
                var remove = attributes.Keys.Where(a => !properties.Contains(RecordReflection.ResolveProperty<T>(a))).ToList();
                foreach (var k in remove) {
                    attributes.Remove(k);
                }
            }

            // Prepare sentence
            var sentence = new Sentence();
            sentence.Attributes = attributes;

            // Set command
            sentence.Command = RecordReflection.GetPath<T>() + "/add";

            // Remove (blank) id
            sentence.Attributes.Remove(".id");

            // Make call
            var result = Call(sentence).Wait();
            if (result.IsError) {
                result.TryGetTrapAttribute("message", out var message);
                throw new CallException(message);
            }
        }

        /// <summary>
        /// Update an existing record in a set.
        /// </summary>
        /// <remarks>
        /// While it's perfectly fine to use these methods directly, it's intended that you use the vanity methods instead (eg. link.Ip.Arp.Get()).
        /// </remarks>
        /// <typeparam name="T"></typeparam>
        /// <param name="record"></param>
        /// <param name="properties"></param>
        public void Update<T>(T record, string[] properties = null) where T : IRecord, new() {
            if (null == record) {
                throw new ArgumentNullException(nameof(record));
            }

            // Check ID is present for set records
            var r = record as ISetRecord;
            if (null != r && r.Id == null) {
                throw new CallException("Attempting to updated a record with no ID set.");
            }

            // Get attributes
            var attributes = RecordReflection.GetRosProperties(record);

            // If filtering properties, remove attributes not wanted
            if (null != properties) {
                var remove = attributes.Keys.Where(a => !properties.Contains(RecordReflection.ResolveProperty<T>(a))).ToList();
                foreach (var k in remove) {
                    attributes.Remove(k);
                }
            }

            // Prepare sentence
            var sentence = new Sentence();
            sentence.Attributes = attributes;

            // Set command
            sentence.Command = RecordReflection.GetPath<T>() + "/set";

            // Make call
            var result = Call(sentence).Wait();
            if (result.IsError) {
                result.TryGetTrapAttribute("message", out var message);
                throw new CallException(message);
            }
        }

        /// <summary>
        /// Delete a record from a set.
        /// </summary>
        /// <remarks>
        /// While it's perfectly fine to use these methods directly, it's intended that you use the vanity methods instead (eg. link.Ip.Arp.Get()).
        /// </remarks>
        /// <typeparam name="T"></typeparam>
        /// <param name="record"></param>
        public void Delete<T>(T record) where T : ISetRecord, new() {
            if (null == record) {
                throw new ArgumentNullException(nameof(record));
            }

            Delete<T>(record.Id);
        }

        /// <summary>
        /// Delete a record from a set.
        /// </summary>
        /// <remarks>
        /// While it's perfectly fine to use these methods directly, it's intended that you use the vanity methods instead (eg. link.Ip.Arp.Get()).
        /// </remarks>
        /// <typeparam name="T"></typeparam>
        /// <param name="id"></param>
        public void Delete<T>(string id) where T : ISetRecord, new() {
            if (null == id) {
                throw new ArgumentNullException(nameof(id));
            }

            // Build sentence
            var sentence = new Sentence();
            sentence.Command = RecordReflection.GetPath<T>() + "/remove";
            sentence.Attributes = new Dictionary<string, string>() {
                {".id", id }
            };

            // Make call
            var result = Call(sentence).Wait();
            if (result.IsError) {
                result.TryGetTrapAttribute("message", out var message);
                throw new CallException(message);
            }
        }

        /// <summary>
        /// Move a set of records before another record.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="beforeId">ID of record to move before. Set as NULL to make the item last.</param>
        /// <param name="ids">One or more IDs to move.</param>
        public void Move<T>(string beforeId, params string[] ids) where T : ISetRecord, new() {
            if (ids.Length == 0) {
                throw new ArgumentException("Must be at least one id provided.", nameof(ids));
            }

            // Build sentence
            var sentence = new Sentence();
            sentence.Command = RecordReflection.GetPath<T>() + "/move";
            sentence.Attributes["numbers"] = string.Join(",", ids);
            if (null != beforeId) {
                sentence.Attributes["destination"] = beforeId;
            };

            // Make call
            var result = Call(sentence).Wait();
            if (result.IsError) {
                result.TryGetTrapAttribute("message", out var message);
                throw new CallException(message);
            }
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
                if (!IsDisposed) {
                    throw ex;
                }
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
