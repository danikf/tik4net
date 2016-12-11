using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace tik4net.Api
{
    internal class ApiCommand: ITikCommand
    {
        private static volatile int _tagCounter = 0;
        private volatile bool _isRuning;
        private volatile int _runningTag;
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

        private TikCommandParameterFormat ResolveParameterFormat(TikCommandParameterFormat usecaseDefaultFormat, TikCommandParameterFormat commandDefaultFormat, TikCommandParameterFormat parameterFormat)
        {
            if (parameterFormat != TikCommandParameterFormat.Default)
                return parameterFormat;
            else if (commandDefaultFormat != TikCommandParameterFormat.Default)
                return commandDefaultFormat;
            else if (usecaseDefaultFormat != TikCommandParameterFormat.Default)
                return usecaseDefaultFormat;
            else
                return TikCommandParameterFormat.NameValue;
        }

        private string[] ConstructCommandText(TikCommandParameterFormat defaultParameterFormat)
        {
            EnsureCommandTextSet();

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
            result.AddRange(_parameters.Select(p =>
            {
                switch (ResolveParameterFormat(defaultParameterFormat, _defaultParameterFormat, p.ParameterFormat))
                {
                    case TikCommandParameterFormat.Filter:
                        return string.Format("?{0}={1}", p.Name, p.Value);
                    case TikCommandParameterFormat.NameValue:
                        return string.Format("={0}={1}", p.Name, p.Value);
                    //case TikCommandParameterFormat.NameOnly:
                    //      return string.Format("={0}", p.Name);
                    default:
                        throw new NotImplementedException();
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
                throw new TikConnectionException("Single response sentence expected.", this, response.Cast<ITikSentence>());

            return response.Single();
        }

        private void EnsureExcatNumberOfResponses(IEnumerable<ApiSentence> response, int nrOfResponses)
        {
            if (response.Count() != nrOfResponses)
                throw new TikConnectionException(string.Format("Command expected {0} sentences as response, but got {1} response sentences.", nrOfResponses, response.Count()), this, response.Cast<ITikSentence>());
        }

        private void ThrowPossibleResponseError(params ApiSentence[] responseSentences)
        {
            foreach (ApiSentence responseSentence in responseSentences)
            {
                ApiTrapSentence trapSentence = responseSentence as ApiTrapSentence;
                if (trapSentence != null)
                    throw new TikCommandException(this, trapSentence);
                ApiFatalSentence fatalSentence = responseSentence as ApiFatalSentence;
                if (fatalSentence != null)
                    throw new TikCommandException(this, fatalSentence.Message);
            }
        }

        private ApiDoneSentence EnsureDoneResponse(ApiSentence responseSentence)
        {
            ApiDoneSentence doneSentence = responseSentence as ApiDoneSentence;
            if (doneSentence == null)
                throw new TikConnectionException("!done sentence expected as result.", this, responseSentence);

            return doneSentence;
        }


        private void EnsureReReponse(params ApiSentence[] responseSentences)
        {
            foreach (ApiSentence responseSentence in responseSentences)
            {
                ApiReSentence reSentence = responseSentence as ApiReSentence;
                if (reSentence == null)
                    throw new TikConnectionException("!re sentence expected as result.", this, responseSentence);
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
            EnsureConnectionSet();
            EnsureNotRunning();

            _isRuning = true;
            try
            {
                string[] commandRows = ConstructCommandText(TikCommandParameterFormat.NameValue);
                IEnumerable<ApiSentence> response = EnsureApiSentences(_connection.CallCommandSync(commandRows));
                ThrowPossibleResponseError(response.ToArray());

                if (response.Count() ==1) //!done + =ret=result word
                {
                    ApiDoneSentence doneSentence = EnsureDoneResponse(response.Single());
                    return doneSentence.GetResponseWord();
                }
                else if (response.Count() >= 2)
                {
                    EnsureReReponse(response.First());
                    ApiReSentence reResponse = (ApiReSentence)response.First();
                    EnsureDoneResponse(response.Last());

                    return reResponse.Words.First().Value; //first word value from !re
                }
                else
                    throw new TikConnectionException("Single !done response or at least one !re sentences expected. (1x!done or Nx!re + 1x!done )", this, response.Cast<ITikSentence>());

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

                EnsureExcatNumberOfResponses(response, 2);
                EnsureReReponse(response.First());   //!re
                ApiReSentence result = (ApiReSentence)response.First();
                EnsureDoneResponse(response.Last()); //!done

                return result;
            }
            finally
            {
                _isRuning = false;
            }
        }

        public IEnumerable<ITikReSentence> ExecuteList()
        {
            EnsureConnectionSet();
            EnsureNotRunning();

            _isRuning = true;
            try
            {
                string[] commandRows = ConstructCommandText(TikCommandParameterFormat.Filter);
                IEnumerable<ApiSentence> response = EnsureApiSentences(_connection.CallCommandSync(commandRows));
                ThrowPossibleResponseError(response.ToArray());

                EnsureReReponse(response.Take(response.Count() -1 ).ToArray());   //!re  - reapeating 
                EnsureDoneResponse(response.Last()); //!done

                return response.Take(response.Count() - 1).Cast<ITikReSentence>().ToList();
            }
            finally
            {
                _isRuning = false;
            }
        }

        public void ExecuteAsync(Action<ITikReSentence> oneResponseCallback, Action<ITikTrapSentence> errorCallback = null)
        {
            EnsureConnectionSet();
            EnsureNotRunning();
            System.Diagnostics.Debug.Assert(_asyncLoadingThread == null);

            int tag = Interlocked.Increment(ref _tagCounter);
            _isRuning = true;
            _runningTag = tag;

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
                                                    _runningTag = -1;
                                                    _asyncLoadingThread = null;
                                                }
                                            }
                                        });
            }
            catch
            {
                _isRuning = false;
                _runningTag = -1;
                throw;
            }
            finally
            {
                //still running
            }
        }

        public IEnumerable<ITikReSentence> ExecuteListWithDuration(int durationSec)
        {
            Exception asyncException = null;
            List<ITikReSentence> result = new List<ITikReSentence>();

            //Async execute, responses are stored in result list
            ExecuteAsync(
                reSentence =>
                {
                    if (_isRuning)
                        result.Add(reSentence);
                },
                error =>
                {
                    asyncException = new TikCommandException(this, error);
                });

            //wait for results (in calling =UI? thread)
            for (int i = 0; i < durationSec * 10; i++) //step per 100ms
            {
                Thread.Sleep(100);
                if (asyncException != null) //ended with exception
                    throw asyncException;
                if (!_isRuning) //already ended (somehow)
                    break;
            }
            Cancel();
            
            //wait for real cancel
            while (_isRuning)//TODO loadingThread.Join();
            {
                Thread.Sleep(10);
            }

            return result;
        }

        private bool CancelInternal(bool joinLoadingThread, int milisecondsTimeout)
        {
            if (_isRuning && _runningTag >= 0)
            {
                 ApiCommand cancellCommand = new ApiCommand(_connection, "/cancel", new ApiCommandParameter(TikSpecialProperties.Tag, _runningTag.ToString())); //REMARKS: =tag=1234 and not =.tag=1234                
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
