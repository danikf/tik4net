using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace tik4net.Api
{
    internal class ApiCommand: ITikCommand
    {
        private volatile bool _isRuning;
        private volatile int _asynchronouslyRunningTag;
        private volatile Thread _asyncLoadingThread;
        private readonly List<ITikCommandParameter> _parameters = new List<ITikCommandParameter>();
        private ApiConnection _connection;
        private string _commandText;
        private TikCommandParameterFormat _defaultParameterFormat;      

        public ITikConnection Connection
        {
            get { return _connection; }
            set
            {                
                Guard.ArgumentOfType<ApiConnection>(value, "Session");
                EnsureNotRunning();

                _connection = (ApiConnection)value;
            }
        }

        public string CommandText
        {
            get { return _commandText; }
            set
            {
                EnsureNotRunning();
                _commandText = value;
            }
        }

        public bool IsRunning
        {
            get { return _isRuning; }
        }

        public IList<ITikCommandParameter> Parameters
        {
            get { return _parameters; }
        }

        public TikCommandParameterFormat DefaultParameterFormat
        {
            get { return _defaultParameterFormat; }
            set { _defaultParameterFormat = value; }
        }

        public ApiCommand()
        {
            _defaultParameterFormat = TikCommandParameterFormat.Default;
        }

        public ApiCommand(TikCommandParameterFormat defaultParameterFormat)            
        {
            _defaultParameterFormat = defaultParameterFormat;
        }

        public ApiCommand(ITikConnection connection)
            : this()
        {
            Connection = connection;
        }

        public ApiCommand(ITikConnection connection, TikCommandParameterFormat defaultParameterFormat)
            : this(defaultParameterFormat)
        {
            Connection = connection;
        }

        public ApiCommand(ITikConnection connection, string commandText)
            :this(connection)
        {
            CommandText = commandText;
        }

        public ApiCommand(ITikConnection connection, string commandText, TikCommandParameterFormat defaultParameterFormat)
            : this(connection, defaultParameterFormat)
        {
            CommandText = commandText;
        }


        public ApiCommand(ITikConnection connection, string commandText, params ITikCommandParameter[] parameters)
            : this(connection, commandText)
        {
            _parameters.AddRange(parameters);            
        }

        public ApiCommand(ITikConnection connection, string commandText, TikCommandParameterFormat defaultParameterFormat, params ITikCommandParameter[] parameters)
            : this(connection, commandText, defaultParameterFormat)
        {
            _parameters.AddRange(parameters);
        }

        private void EnsureNotRunning()
        {
            if (_isRuning)
                throw new InvalidOperationException("Command is already running.");
        }

        private void EnsureConnectionSet()
        {
            if (_connection == null)
                throw new InvalidOperationException("Connection is not assigned.");
        }

        private void EnsureCommandTextSet()
       { 
            if (StringHelper.IsNullOrWhiteSpace(_commandText))
                throw new InvalidOperationException("CommandText is not set.");
        }

        private TikCommandParameterFormat ResolveParameterFormat(TikCommandParameterFormat usecaseDefaultFormat, TikCommandParameterFormat commandDefaultFormat, ITikCommandParameter parameter)
        {
            if (parameter.ParameterFormat != TikCommandParameterFormat.Default)
                return parameter.ParameterFormat;
            else if (parameter.Name == TikSpecialProperties.Tag)
                return TikCommandParameterFormat.Tag; //.tag=1231
            else if (commandDefaultFormat != TikCommandParameterFormat.Default)
                return commandDefaultFormat;
            else if (usecaseDefaultFormat != TikCommandParameterFormat.Default)
                return usecaseDefaultFormat;
            else
                return TikCommandParameterFormat.NameValue;
        }

        private string[] ConstructCommandText(TikCommandParameterFormat defaultParameterFormat, params ITikCommandParameter[] additionalParamemeters)
        {
            EnsureCommandTextSet();
            foreach (var additionalParameter in additionalParamemeters)
            {
                if (_parameters.Any(p => p.Name == additionalParameter.Name))
                    throw new ArgumentException($"Parameter {additionalParameter.Name} already defined (could not be additionalParameter / proplist / etc.).");
            }
        
            string commandText = CommandText;
            if (!StringHelper.IsNullOrWhiteSpace(commandText) && !commandText.Contains("\n") && !commandText.StartsWith("/"))
                commandText = "/" + commandText;

            List<string> result;
            if (commandText.Contains('\n'))
            {
                result = new List<string>(commandText.Split('\n').Select(row => row.Trim()));
            }
            else
            {
                result = new List<string> { commandText };
            }

            //parameters
            result.AddRange(_parameters.Concat(additionalParamemeters).Select(p =>
            {
                if (p.Name.StartsWith("=")) //NameValue format in parameter name
                    return string.Format("{0}={1}", p.Name, p.Value);
                else if (p.Name.StartsWith("?")) //Filter format in parameter name
                    return string.Format("{0}={1}", p.Name, p.Value);
                else
                {
                    switch (ResolveParameterFormat(defaultParameterFormat, _defaultParameterFormat, p))
                    {
                        case TikCommandParameterFormat.Filter:
                            return string.Format("?{0}={1}", p.Name, p.Value);
                        case TikCommandParameterFormat.NameValue:
                            return string.Format("={0}={1}", p.Name, p.Value);
                        case TikCommandParameterFormat.Tag:
                            return string.Format("{0}={1}", p.Name, p.Value);
                        //case TikCommandParameterFormat.NameOnly:
                        //      return string.Format("={0}", p.Name);
                        default:
                            throw new NotImplementedException();
                    }
                }
            }));
            return result.ToArray();
        }

        private IEnumerable<ApiSentence> EnsureApiSentences(IEnumerable<ITikSentence> sentences)
        {
            if (sentences.Any(sentence => !(sentence is ApiSentence)))
                throw new InvalidOperationException("ApiCommand expects ApiSentence as result from ApiConnection.");

            return sentences.Cast<ApiSentence>();
        }

        private ApiSentence EnsureSingleResponse(IEnumerable<ApiSentence> response)
        {
            if (response.Count() != 1)
                throw new TikCommandUnexpectedResponseException("Single response sentence expected.", this, response.Cast<ITikSentence>());

            return response.Single();
        }

        private void EnsureOneReAndDone(IEnumerable<ApiSentence> response)
        {
            if (response.Count() != 2)
            {
                if (response.Count() == 1 && response.Single() is ITikDoneSentence)
                    throw new TikNoSuchItemException(this);
                else
                    throw new TikCommandUnexpectedResponseException($"Command expected 1x !re and 1x !done sentences as response, but got {response.Count()} response sentences.", this, response.Cast<ITikSentence>());
            }
            EnsureReReponse(response.First());
            EnsureDoneResponse(response.Last());
        }

        private static Regex AlreadyWithSuchRegex = new Regex(@"^(failure:)?.*already have.+such");
        private void ThrowPossibleResponseError(params ApiSentence[] responseSentences)
        {
            foreach (ApiSentence responseSentence in responseSentences)
            {
                ApiTrapSentence trapSentence = responseSentence as ApiTrapSentence;
                if (trapSentence != null)
                { //detect well known error responses and convert them to special exceptions
                    if (trapSentence.Message.StartsWith("no such command"))
                        throw new TikNoSuchCommandException(this, trapSentence);
                    else if (trapSentence.Message.StartsWith("no such item"))
                        throw new TikNoSuchItemException(this, trapSentence);
                    else if (AlreadyWithSuchRegex.IsMatch(trapSentence.Message))
                        throw new TikAlreadyHaveSuchItemException(this, trapSentence);
                    else
                        throw new TikCommandTrapException(this, trapSentence);
                }
                ApiFatalSentence fatalSentence = responseSentence as ApiFatalSentence;
                if (fatalSentence != null)
                    throw new TikCommandFatalException(this, fatalSentence.Message);
            }
        }

        private ApiDoneSentence EnsureDoneResponse(ApiSentence responseSentence)
        {
            ApiDoneSentence doneSentence = responseSentence as ApiDoneSentence;
            if (doneSentence == null)
                throw new TikCommandUnexpectedResponseException("!done sentence expected as result.", this, responseSentence);

            return doneSentence;
        }

        private void EnsureReReponse(params ApiSentence[] responseSentences)
        {
            foreach (ApiSentence responseSentence in responseSentences)
            {
                ApiReSentence reSentence = responseSentence as ApiReSentence;
                if (reSentence == null)
                    throw new TikCommandUnexpectedResponseException("!re sentence expected as result.", this, responseSentence);
            }
        }


        public void ExecuteNonQuery()
        {
            EnsureConnectionSet();
            EnsureNotRunning();

            _isRuning = true;
            try
            {
                string[] commandRows = ConstructCommandText(TikCommandParameterFormat.NameValue);
                IEnumerable<ApiSentence> response = EnsureApiSentences(_connection.CallCommandSync(commandRows));
                ThrowPossibleResponseError(response.ToArray());

                ApiSentence responseSentence = EnsureSingleResponse(response);
                EnsureDoneResponse(responseSentence);
            }
            finally
            {
                _isRuning = false;
            }
        }

        public string ExecuteScalar()
        {
            return ExecuteScalarInternal(null, false);
        }

        public string ExecuteScalar(string target)
        {
            return ExecuteScalarInternal(target, false);
        }

        public string ExecuteScalarOrDefault()
        {
            return ExecuteScalarInternal(null, true, null);
        }

        public string ExecuteScalarOrDefault(string defaultValue)
        {
            return ExecuteScalarInternal(null, true, defaultValue);
        }

        public string ExecuteScalarOrDefault(string defaultValue, string target)
        {
            return ExecuteScalarInternal(target, true, defaultValue);
        }

        private string ExecuteScalarInternal(string target, bool allowReturnDefault, string defaultValue = null)
        {
            EnsureConnectionSet();
            EnsureNotRunning();

            _isRuning = true;
            try
            {
                var targetParameterInArray = target != null ? new ITikCommandParameter[] { new ApiCommandParameter(TikSpecialProperties.Proplist, target, TikCommandParameterFormat.NameValue) } : new ITikCommandParameter[] { };
                string[] commandRows = ConstructCommandText(TikCommandParameterFormat.NameValue, targetParameterInArray);
                IEnumerable<ApiSentence> response = EnsureApiSentences(_connection.CallCommandSync(commandRows));
                ThrowPossibleResponseError(response.ToArray());

                if (response.Count() == 1) //!done + =ret=result word
                {
                    ApiDoneSentence doneSentence = EnsureDoneResponse(response.Single());
                    if (doneSentence.Words.ContainsKey(TikSpecialProperties.Ret))
                        return doneSentence.GetResponseWord();
                    else if (allowReturnDefault)
                        return defaultValue;
                    else
                        throw new TikNoSuchItemException(this);
                }
                else if (response.Count() == 2) //!re + !done
                {
                    EnsureOneReAndDone(response);
                    ApiReSentence reResponse = (ApiReSentence)response.First();

                    return reResponse.Words.Single(v => v.Key != TikSpecialProperties.Tag).Value; //single word value from !re  //NOTE - .tag could be added when Connection.SendTagWithSyncCommand=true
                }
                else
                    throw new TikCommandUnexpectedResponseException("Single !done response or exactly one !re sentences expected. (1x!done or 1x!re + 1x!done )", this, response.Cast<ITikSentence>());
            }
            finally
            {
                _isRuning = false;
            }
        }

        public ITikReSentence ExecuteSingleRow()
        {
            EnsureConnectionSet();
            EnsureNotRunning();

            _isRuning = true;
            try
            {
                string[] commandRows = ConstructCommandText(TikCommandParameterFormat.Filter);
                IEnumerable<ApiSentence> response = EnsureApiSentences(_connection.CallCommandSync(commandRows));
                ThrowPossibleResponseError(response.ToArray());

                if (response.OfType<ApiReSentence>().Count() > 1)
                    throw new TikCommandAmbiguousResultException(this);
                EnsureOneReAndDone(response);
                ApiReSentence result = (ApiReSentence)response.First();

                return result;
            }
            finally
            {
                _isRuning = false;
            }
        }

        public ITikReSentence ExecuteSingleRowOrDefault()
        {
            var sentences = ExecuteList();
            
            if (sentences.Count() > 1)
                throw new TikCommandAmbiguousResultException(this);
            return sentences.SingleOrDefault();
        }

        public IEnumerable<ITikReSentence> ExecuteList()
        {
            return ExecuteListInternal(null);
        }

        public IEnumerable<ITikReSentence> ExecuteList(params string[] proplist)
        {
            Guard.ArgumentNotNull(proplist, nameof(proplist));

            return ExecuteListInternal(proplist);
        }

        private IEnumerable<ITikReSentence> ExecuteListInternal(params string[] proplist)
        {
            EnsureConnectionSet();
            EnsureNotRunning();

            _isRuning = true;
            try
            {
                var proplistParameters = proplist == null ? new ITikCommandParameter[] { } : proplist.Select(p => new ApiCommandParameter(TikSpecialProperties.Proplist, p, TikCommandParameterFormat.NameValue)).ToArray();
                string[] commandRows = ConstructCommandText(TikCommandParameterFormat.Filter, proplistParameters);
                IEnumerable<ApiSentence> response = EnsureApiSentences(_connection.CallCommandSync(commandRows));
                ThrowPossibleResponseError(response.ToArray());

                EnsureReReponse(response.Take(response.Count() - 1).ToArray());   //!re  - reapeating 
                EnsureDoneResponse(response.Last()); //!done

                return response.Take(response.Count() - 1).Cast<ITikReSentence>().ToList();
            }
            finally
            {
                _isRuning = false;
            }
        }

        public void ExecuteAsync(Action<ITikReSentence> oneResponseCallback, 
            Action<ITikTrapSentence> errorCallback = null,
            Action onDoneCallback = null)
        {
            EnsureConnectionSet();
            EnsureNotRunning();
            System.Diagnostics.Debug.Assert(_asyncLoadingThread == null);

            int tag = TagSequence.Next();
            _isRuning = true;
            _asynchronouslyRunningTag = tag;

            try
            {
                string[] commandRows = ConstructCommandText(TikCommandParameterFormat.NameValue);
                _asyncLoadingThread = _connection.CallCommandAsync(commandRows, tag.ToString(),
                                        response =>
                                        {
                                            ApiReSentence reResponse = response as ApiReSentence;
                                            if (reResponse != null)
                                            {
                                                if (oneResponseCallback != null)
                                                    oneResponseCallback(reResponse);
                                            }
                                            else
                                            {
                                                ApiTrapSentence trapResponse = response as ApiTrapSentence;
                                                if (trapResponse != null)
                                                {
                                                    if (trapResponse.CategoryCode == "2" && trapResponse.Message == "interrupted")
                                                    {
                                                        //correct state - async operation has been Cancelled.
                                                    }
                                                    else
                                                    {
                                                        //incorrect - any error occurs
                                                        if (errorCallback != null)
                                                            errorCallback(trapResponse);
                                                    }
                                                }
                                                else if (response is ApiDoneSentence || response is ApiFatalSentence)
                                                {
                                                    //REMARKS: we are expecting !trap + !done sentences when any error occurs
                                                    _isRuning = false;
                                                    _asynchronouslyRunningTag = -1;
                                                    _asyncLoadingThread = null;

                                                    if (response is ApiDoneSentence && onDoneCallback != null)
                                                        onDoneCallback();
                                                }
                                            }
                                        });
            }
            catch
            {
                _isRuning = false;
                _asynchronouslyRunningTag = -1;
                throw;
            }
            finally
            {
                //still running
            }
        }

        public IEnumerable<ITikReSentence> ExecuteListWithDuration(int durationSec)
        {
            bool wasAborted;
            string abortReason;
            var result = ExecuteListWithDuration(durationSec, out wasAborted, out abortReason);

            if (wasAborted)
                throw new TikCommandAbortException(this, abortReason);
            else
                return result;
        }

        public IEnumerable<ITikReSentence> ExecuteListWithDuration(int durationSec, out bool wasAborted, out string abortReason)
        {
            string asyncExceptionMessage = null;
            List<ITikReSentence> result = new List<ITikReSentence>();
            wasAborted = false;
            abortReason = null;

            //Async execute, responses are stored in result list
            ExecuteAsync(
                reSentence =>
                {
                    if (_isRuning)
                        result.Add(reSentence);
                },
                error =>
                {
                    asyncExceptionMessage = error.Message;
                });

            //wait for results (in calling =UI? thread)
            for (int i = 0; i < durationSec * 10; i++) //step per 100ms
            {
                Thread.Sleep(100);
                if (asyncExceptionMessage != null) //ended with exception
                {                    
                    _isRuning = false;
                    wasAborted = true;
                    abortReason = asyncExceptionMessage;
                }
                if (!_connection.IsOpened) 
                {
                    _isRuning = false;
                    wasAborted = true;
                    abortReason = "Connection has been closed";
                    return result;
                }
                if (!_isRuning) //already ended (cancelled froum outside?)
                {                    
                    wasAborted = true;
                    abortReason = "Cancelled";
                    return result;
                }

            }
            CancelInternal(true, -1); //Join loading thread

            return result;
        }

        private bool CancelInternal(bool joinLoadingThread, int milisecondsTimeout)
        {
            if (_isRuning && _asynchronouslyRunningTag >= 0)
            {
                 ApiCommand cancellCommand = new ApiCommand(_connection, "/cancel", 
                     new ApiCommandParameter("tag", _asynchronouslyRunningTag.ToString(), TikCommandParameterFormat.NameValue), // tag we are cancelling: REMARKS: =tag=1234 and not =.tag=1234 
                     new ApiCommandParameter(TikSpecialProperties.Tag, "c_"+_asynchronouslyRunningTag.ToString(), TikCommandParameterFormat.Tag) //tag of cancell command itself
                     );                
                 cancellCommand.ExecuteNonQuery();
                if (joinLoadingThread)
                {
                    Thread loadingThread = _asyncLoadingThread;
                    if (loadingThread != null)
                    {
                        if (milisecondsTimeout > 0)
                            return loadingThread.Join(milisecondsTimeout);
                        else
                        {
                            loadingThread.Join();
                            return true;
                        }
                    }
                }                
            }
            return true;
        }

        public void Cancel()
        {
            CancelInternal(false, 0);
        }

        public void CancelAndJoin()
        {
            CancelInternal(true, -1);
        }

        public bool CancelAndJoin(int milisecondsTimeout)
        {
            return CancelInternal(true, milisecondsTimeout);
        }

        public ITikCommandParameter AddParameter(string name, string value)
        {
            ApiCommandParameter result = new ApiCommandParameter(name, value);
            _parameters.Add(result);

            return result;
        }

        public ITikCommandParameter AddParameter(string name, string value, TikCommandParameterFormat parameterFormat)
        {
            ITikCommandParameter result = AddParameter(name, value);
            result.ParameterFormat = parameterFormat;

            return result;
        }

        public ITikCommand WithParameter(string name, string value)
        {
            AddParameter(name, value);

            return this;
        }

        public ITikCommand WithParameter(string name, string value, TikCommandParameterFormat parameterFormat)
        {
            AddParameter(name, value, parameterFormat);

            return this;
        }

        public override string ToString()
        {
            return string.Join("\n", new string[] { CommandText }
                                                        .Concat(Parameters.Select(p => p.ToString())).ToArray());
        }

        private IEnumerable<ITikCommandParameter> CreateParameters(string[] parameterNamesAndValues)
        {
            List<ApiCommandParameter> parameters = new List<ApiCommandParameter>();
            for (int idx = 0; idx < parameterNamesAndValues.Length / 2; idx++)   // name, value, name, value, ... sequence
            {
                parameters.Add(new ApiCommandParameter(parameterNamesAndValues[idx * 2], parameterNamesAndValues[idx * 2 + 1]));
            }

            return parameters.Cast<ITikCommandParameter>();
        }

        public IEnumerable<ITikCommandParameter> AddParameterAndValues(params string[] parameterNamesAndValues)
        {
            var parameters = CreateParameters(parameterNamesAndValues);
            _parameters.AddRange(parameters);

            return parameters;
        }
    }
}
