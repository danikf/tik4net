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
    internal sealed class ApiConnection : ITikConnection
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

        private readonly object _writeLockObj = new object();
        private readonly object _readLockObj = new object();
        private volatile bool _isOpened = false;
        private bool _isSsl = false;
        private Encoding _encoding = Encoding.ASCII;
        private bool _sendTagWithSyncCommand = false;
        private int _sendTimeout;
        private int _receiveTimeout;
        private TcpClient _tcpConnection;
        private /*NetworkStream*/System.IO.Stream _tcpConnectionStream;
        private SentenceList _readSentences = new SentenceList();

        public event EventHandler<TikConnectionCommCallbackEventArgs> OnReadRow;
        public event EventHandler<TikConnectionCommCallbackEventArgs> OnWriteRow;

        public bool DebugEnabled { get; set; }

        public bool IsOpened
        {
            get { return _isOpened; }
        }

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

        public bool IsSsl
        {
            get { return _isSsl; }
        }

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

            _tcpConnectionStream.Dispose();
            _tcpConnection.Dispose();
            _isOpened = false;        
        }

        public void Open(string host, string user, string password)
        {
            Open(host, _isSsl ? APISSL_DEFAULT_PORT : API_DEFAULT_PORT, user, password);
        }

        public void Open(string host, int port, string user, string password)
        {
            //open connection
            _tcpConnection = new TcpClient();
            if (_sendTimeout > 0)
                _tcpConnection.SendTimeout = _sendTimeout;
            if (_receiveTimeout > 0)
                _tcpConnection.ReceiveTimeout = _receiveTimeout;
            _tcpConnection.ConnectAsync(host, port).GetAwaiter().GetResult();

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
                    sslStream.AuthenticateAsClientAsync(host, null, SslProtocols.None, false).GetAwaiter().GetResult();
                }
                catch(AuthenticationException ex)
                {
                    throw new TikConnectionSSLErrorException(ex);
                }
                _tcpConnectionStream = sslStream;
            }

            _isOpened = true;
            Login_v3(user, password);  //LoginInternal(user, password);            
        }

        public async System.Threading.Tasks.Task OpenAsync(string host, string user, string password)
        {
            await OpenAsync(host, _isSsl ? APISSL_DEFAULT_PORT : API_DEFAULT_PORT, user, password);
        }

        public async System.Threading.Tasks.Task OpenAsync(string host, int port, string user, string password)
        {
            //open connection
            _tcpConnection = new TcpClient();
            if (_sendTimeout > 0)
                _tcpConnection.SendTimeout = _sendTimeout;
            if (_receiveTimeout > 0)
                _tcpConnection.ReceiveTimeout = _receiveTimeout;

            await _tcpConnection.ConnectAsync(host, port);

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
                await sslStream.AuthenticateAsClientAsync(host);
                _tcpConnectionStream = sslStream;
            }

            _isOpened = true;
            Login_v3(user, password); // LoginInternal(user, password);           
        }

        private void Login_v3(string user, string password)
        {
            try
            {
                ApiCommand loginCommand = new ApiCommand(this, "/login", TikCommandParameterFormat.NameValue,
                    new ApiCommandParameter("name", user), new ApiCommandParameter("password", password)); //parameters will be ignored with old login protocol

                var responseHashOrNull = loginCommand.ExecuteScalarOrDefault();

                //old login protocol
                if (!string.IsNullOrEmpty(responseHashOrNull))
                {
                    //login connection
                    string hashedPass = ApiConnectionHelper.EncodePassword(password, responseHashOrNull);
                    ApiCommand loginCommand2 = new ApiCommand(this, "/login", TikCommandParameterFormat.NameValue,
                        new ApiCommandParameter("name", user), new ApiCommandParameter("response", hashedPass));
                    loginCommand2.ExecuteNonQuery();
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

        private static bool ValidateServerCertificate(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {            
            return true; // Accept all certificates
        }

        public void Dispose()
        {
            if (_isOpened)
                Close();
        }

        private byte ReadByteChecked()
        {
            int b = _tcpConnectionStream.ReadByte();
            if (b < 0)
                throw new IOException("Connection closed unexpectedly while reading.");
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
                    byte[] buffer = new byte[(int)wordLength];
                    int totalRead = 0;
                    while (totalRead < (int)wordLength)
                    {
                        int n = _tcpConnectionStream.Read(buffer, totalRead, (int)wordLength - totalRead);
                        if (n == 0)
                            throw new IOException("Connection closed while reading word body.");
                        totalRead += n;
                    }
                    result = Encoding.GetString(buffer, 0, (int)wordLength);
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
            catch(IOException)
            {
                _isOpened = _tcpConnection.Connected;
                throw;
            }
        }

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

        private ITikSentence GetOne(string tag)
        {
            do
            {
                if (!_tcpConnection.Connected)
                    _isOpened = false;

                if (!_isOpened)
                    return new ApiTrapSentence(new string[] { "=category=-1", "=message=connection closed" });

                ITikSentence result;
                // try to find in in _readSentences
                if (_readSentences.TryDequeue(tag, out result)) // found => removed from _readSentences and return as result
                    return result;

                lock (_readLockObj)
                {
                    // again - try to find in in _readSentences (could be added between last try and lock)  (see double check lock pattern)
                    if (_readSentences.TryDequeue(tag, out result)) // found => removed from _readSentences and return as result
                        return result;

                    ITikSentence sentenceFromTcp = ReadSentence();
                    if (sentenceFromTcp.Tag == tag)
                    {
                        return sentenceFromTcp;
                    }
                    else // another tag => add to _readSentences for another reading thread
                    {
                        _readSentences.Enqueue(sentenceFromTcp);
                    }
                }
            } while (true); //TODO max attempts???  //repeat until get any response for specific tag
        }
        
        private IEnumerable<ITikSentence> GetAll(string tag)
        {
            ITikSentence sentence;
            do
            {
                sentence = GetOne(tag);
                yield return sentence;
            } while (!(sentence is ApiDoneSentence || sentence is ApiFatalSentence /*|| sentence is ApiTrapSentence */)); // read until !done or !fatal
            // NOTE: !trap is always followed by !done (keep reading). !fatal closes the connection immediately — no !done follows.
        }

        private static readonly Regex tagRegex = new Regex($"^\\{TikSpecialProperties.Tag}=(?<TAG>.+)$"); // .tag=1234
        public IEnumerable<ITikSentence> CallCommandSync(params string[] commandRows)
        {
            EnsureOpened();

            //read .tag from commandRows - if present
            var tagOrEmptyString = string.Empty;            
            foreach(var row in commandRows)
            {
                var match = tagRegex.Match(row);
                if (match.Success)
                {
                    tagOrEmptyString = match.Groups["TAG"].Value;
                    break;
                }
            }

            if (_sendTagWithSyncCommand && string.IsNullOrEmpty(tagOrEmptyString))
            {
                tagOrEmptyString = TagSequence.Next().ToString();
                commandRows = commandRows.Concat(new string[] { string.Format("{0}={1}", TikSpecialProperties.Tag, tagOrEmptyString) }).ToArray();
            }

            lock (_writeLockObj)
            {
                WriteCommand(commandRows);
            }
            return GetAll(tagOrEmptyString).ToList();
        }

        public IEnumerable<ITikSentence> CallCommandSync(IEnumerable<string> commandRows)
        {
            return CallCommandSync(commandRows.ToArray());
        }

        public Thread CallCommandAsync(IEnumerable<string> commandRows, string tag, 
            Action<ITikSentence> oneResponseCallback)
        {            
            Guard.ArgumentNotNullOrEmptyString(tag, "tag");
            EnsureOpened();

            commandRows = commandRows.Concat(new string[] { string.Format("{0}={1}", TikSpecialProperties.Tag, tag) }); // .tag=1234
            lock (_writeLockObj)
            {
                WriteCommand(commandRows);
            }

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
                    } while (_isOpened && !(sentence is ApiDoneSentence /*|| sentence is ApiTrapSentence*/ || sentence is ApiFatalSentence)); // read sentences via TryGetOne(wait) for TAG until !done or !fatal is returned
                    //NOTE: Should be ended via !done or !trap+!done (called via Cancel() command for specific tag)
                }
                catch
                {
                    //catch all exceptions from GetOne -> thread should end via !done read sentence
                    //TODO: implement "timeoutException" for GetOne and cancell gracefully thraed if this exception happens
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
