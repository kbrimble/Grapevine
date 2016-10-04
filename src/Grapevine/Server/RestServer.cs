﻿using System;
using System.Collections.Concurrent;
using System.Net;
using System.Security.Authentication.ExtendedProtection;
using System.Threading;
using Grapevine.Exceptions.Server;
using Grapevine.Interfaces.Server;
using Grapevine.Interfaces.Shared;
using Grapevine.Shared;
using Grapevine.Shared.Loggers;
using HttpStatusCode = Grapevine.Shared.HttpStatusCode;
using ExtendedProtectionSelector = System.Net.HttpListener.ExtendedProtectionSelector;
using HttpListener = Grapevine.Interfaces.Server.HttpListener;

namespace Grapevine.Server
{
    /// <summary>
    /// Provides a programmatically controlled REST implementation for a single Prefix using HttpListener
    /// </summary>
    public interface IRestServer : IServerSettings, IDynamicProperties, IDisposable
    {
        /// <summary>
        /// Gets a value that indicates whether HttpListener has been started
        /// </summary>
        bool IsListening { get; }

        /// <summary>
        /// Gets the prefix created by combining the Protocol, Host and Port properties into a scheme and authority
        /// </summary>
        string ListenerPrefix { get; }

        /// <summary>
        /// Starts the server: executes OnBeforeStart, starts the HttpListener, then executes OnAfterStart if the HttpListener is listening
        /// </summary>
        void Start();

        /// <summary>
        /// Stops the server; executes OnBeforeStop, stops the HttpListener, then executes OnAfterStop is the HttpListener is not listening
        /// </summary>
        void Stop();
    }

    public class RestServer : DynamicProperties, IRestServer
    {
        private string _host;
        private string _port;
        private string _protocol = "http";
        private int _connections;
        private IGrapevineLogger _logger;
        protected bool IsStopping;
        protected bool IsStarting;
        protected readonly IHttpListener Listener;
        protected readonly Thread Listening;
        protected readonly ConcurrentQueue<HttpListenerContext> Queue;
        protected readonly ManualResetEvent ReadyEvent, StopEvent;
        protected Thread[] Workers;

        protected internal bool TestingMode = false;

        public bool EnableThrowingExceptions { get; set; }
        public Action OnBeforeStart { get; set; }
        public Action OnAfterStart { get; set; }
        public Action OnBeforeStop { get; set; }
        public Action OnAfterStop { get; set; }
        public IRouter Router { get; set; }

        public RestServer() : this(new ServerSettings()) { }

        protected internal RestServer(IHttpListener listener) : this(new ServerSettings())
        {
            TestingMode = true;
            Listener = listener;
        }

        public RestServer(IServerSettings options)
        {
            Listener = new HttpListener(new System.Net.HttpListener());
            Listening = new Thread(HandleRequests);
            Queue = new ConcurrentQueue<HttpListenerContext>();
            ReadyEvent = new ManualResetEvent(false);
            StopEvent = new ManualResetEvent(false);

            Connections = options.Connections;
            Host = options.Host;
            Logger = options.Logger;
            OnBeforeStart = options.OnBeforeStart;
            OnAfterStart = options.OnAfterStart;
            OnBeforeStop = options.OnBeforeStop;
            OnAfterStop = options.OnAfterStop;
            Port = options.Port;
            PublicFolder = options.PublicFolder;
            Router = options.Router;
            UseHttps = options.UseHttps;

            Advanced = new AdvancedRestServer(Listener);
            Listener.IgnoreWriteExceptions = true;
        }

        public static RestServer For(Action<ServerSettings> configure)
        {
            var options = new ServerSettings();
            configure(options);
            return new RestServer(options);
        }

        public static RestServer For<T>() where T : ServerSettings, new()
        {
            return new RestServer(new T());
        }

        /// <summary>
        /// Provides direct access to selected methods and properties on the internal HttpListener instance in use; do not used unless you are fully aware of what you are doing and the consequences involved.
        /// </summary>
        public AdvancedRestServer Advanced { get; }

        public int Connections
        {
            get { return _connections; }
            set
            {
                if (IsListening) throw new ServerStateException();
                _connections = value;
            }
        }

        public string Host
        {
            get { return _host; }
            set
            {
                if (IsListening) throw new ServerStateException();
                _host = value == "0.0.0.0" ? "+" : value.ToLower();
            }
        }

        public bool IsListening => Listener?.IsListening ?? false;

        public IGrapevineLogger Logger
        {
            get { return _logger; }
            set
            {
                _logger = value ?? NullLogger.GetInstance();
                if (Router != null) Router.Logger = _logger;
            }
        }

        public Action OnStart
        {
            get { return OnAfterStart; }
            set { OnAfterStart = value; }
        }

        public Action OnStop
        {
            get { return OnAfterStop; }
            set { OnAfterStop = value; }
        }

        public string ListenerPrefix => $"{_protocol}://{Host}:{Port}/";

        public string Port
        {
            get { return _port; }
            set
            {
                if (IsListening) throw new ServerStateException();
                _port = value;
            }
        }

        public IPublicFolder PublicFolder { get; }

        public bool UseHttps
        {
            get { return _protocol == "https"; }
            set
            {
                if (IsListening) throw new ServerStateException();
                _protocol = value ? "https" : "http";
            }
        }

        public void Start()
        {
            if (IsListening || IsStarting) return;
            if (IsStopping) throw new UnableToStartHostException("Cannot start server until server has finished stopping");
            IsStarting = true;

            try
            {
                OnBeforeStart?.Invoke();
                if (Router.RoutingTable.Count == 0) Router.ScanAssemblies();

                Listener.Prefixes?.Add(ListenerPrefix);
                Listener.Start();
                if (!TestingMode)
                {
                    Listening.Start();

                    Workers = new Thread[_connections*Environment.ProcessorCount];
                    for (var i = 0; i < Workers.Length; i++)
                    {
                        Workers[i] = new Thread(Worker);
                        Workers[i].Start();
                    }
                }

                Logger.Trace($"Listening: {ListenerPrefix}");
                if (IsListening) OnAfterStart?.Invoke();
            }
            catch (Exception e)
            {
                throw new UnableToStartHostException($"An error occured when trying to start the {GetType().FullName}", e);
            }
            finally
            {
                IsStarting = false;
            }
        }

        public void Stop()
        {
            if (!IsListening || IsStopping) return;
            if (IsStarting) throw new UnableToStartHostException("Cannot stop server until server has finished starting");
            IsStopping = true;

            try
            {
                OnBeforeStop?.Invoke();

                StopEvent.Set();
                if (!TestingMode)
                {
                    Listening.Join();
                    foreach (var worker in Workers) worker.Join();
                }
                Listener.Stop();

                if (!IsListening) OnAfterStop?.Invoke();
            }
            catch (Exception e)
            {
                throw new UnableToStopHostException($"An error occured while trying to stop {GetType().FullName}", e);
            }
            finally
            {
                IsStopping = false;
            }
        }

        public void Dispose()
        {
            Stop();
            Listener?.Close();
        }

        public IRestServer LogToConsole()
        {
            Logger = new ConsoleLogger();
            return this;
        }

        /// <summary>
        /// For use in routes that want to stop the server; starts a new thread and then calls Stop on the server
        /// </summary>
        public void ThreadSafeStop()
        {
            new Thread(Stop).Start();
        }

        private void HandleRequests()
        {
            while (Listener.IsListening)
            {
                var context = Listener.BeginGetContext(ContextReady, null);
                if (0 == WaitHandle.WaitAny(new[] { StopEvent, context.AsyncWaitHandle })) return;
            }
        }

        private void ContextReady(IAsyncResult result)
        {
            try
            {
                lock (Queue)
                {
                    Queue.Enqueue(Listener.EndGetContext(result));
                    ReadyEvent.Set();
                }
            }
            catch (ObjectDisposedException) { /* Intentionally not doing anything with this */ }
            catch (Exception e)
            {
                /* Ignore exceptions thrown by incomplete async methods listening for incoming requests */
                if (IsStopping && e is HttpListenerException && ((HttpListenerException)e).NativeErrorCode == 995) return;
                Logger.Debug(e);
            }
        }

        private void Worker()
        {
            WaitHandle[] wait = { ReadyEvent, StopEvent };
            while (0 == WaitHandle.WaitAny(wait))
            {
                IHttpContext context;

                lock (Queue)
                {
                    if (Queue.Count > 0)
                    {
                        HttpListenerContext ctx;
                        Queue.TryDequeue(out ctx);
                        if (ctx == null) continue;
                        context = new HttpContext(ctx, this);
                    }
                    else { ReadyEvent.Reset(); continue; }
                }

                SafeRouteContext(context);
            }
        }

        private static void SafeRouteContext(IHttpContext context)
        {
            var server = context.Server;

            try
            {
                UnsafeRouteContext(context);
            }
            catch (RouteNotFoundException)
            {
                if (server.EnableThrowingExceptions) throw;
                context.Response.SendResponse(HttpStatusCode.NotFound);
            }
            catch (NotImplementedException)
            {
                if (server.EnableThrowingExceptions) throw;
                context.Response.SendResponse(HttpStatusCode.NotImplemented);
            }
            catch (Exception e)
            {
                server.Logger.Error(e);
                if (server.EnableThrowingExceptions) throw;
                context.Response.SendResponse(HttpStatusCode.InternalServerError, e);
            }
        }

        private static void UnsafeRouteContext(IHttpContext context)
        {
            var server = context.Server;

            var shouldRespondWithFile = !string.IsNullOrWhiteSpace(server.PublicFolder.Prefix) &&
                                        context.Request.PathInfo.StartsWith(server.PublicFolder.Prefix);

            context = server.PublicFolder.SendPublicFile(context);

            if (shouldRespondWithFile)
            {
                if (context.WasRespondedTo)
                {
                    server.Logger.Trace($"Returned file {context.Request.PathInfo}");
                }
                else
                {
                    context.Response.SendResponse(HttpStatusCode.NotFound);
                }
                return;
            }

            if (context.WasRespondedTo) return;
            if (!server.Router.Route(context)) throw new RouteNotFoundException(context);
        }
    }

    /// <summary>
    /// Provides direct access to selected methods and properties on the internal HttpListener instance in use. This class cannot be inherited.
    /// </summary>
    public sealed class AdvancedRestServer
    {
        private readonly IHttpListener _listener;

        internal AdvancedRestServer(IHttpListener listener)
        {
            _listener = listener;
        }

        /// <summary>
        /// Gets or sets the delegate called to determine the protocol used to authenticate clients
        /// </summary>
        public AuthenticationSchemeSelector AuthenticationSchemeSelectorDelegate
        {
            get { return _listener.AuthenticationSchemeSelectorDelegate; }
            set { _listener.AuthenticationSchemeSelectorDelegate = value; }
        }

        /// <summary>
        /// Gets or sets the scheme used to authenticate clients
        /// </summary>
        public AuthenticationSchemes AuthenticationSchemes
        {
            get { return _listener.AuthenticationSchemes; }
            set { _listener.AuthenticationSchemes = value; }
        }

        /// <summary>
        /// Get or set the ExtendedProtectionPolicy to use for extended protection for a session
        /// </summary>
        public ExtendedProtectionPolicy ExtendedProtectionPolicy
        {
            get { return _listener.ExtendedProtectionPolicy; }
            set { _listener.ExtendedProtectionPolicy = value; }
        }

        /// <summary>
        /// Get or set the delegate called to determine the ExtendedProtectionPolicy to use for each request
        /// </summary>
        public ExtendedProtectionSelector ExtendedProtectionSelectorDelegate
        {
            get { return _listener.ExtendedProtectionSelectorDelegate; }
            set { _listener.ExtendedProtectionSelectorDelegate = value; }
        }

        /// <summary>
        /// Gets or sets a Boolean value that specifies whether your application receives exceptions that occur when an HttpListener sends the response to the client
        /// </summary>
        public bool IgnoreWriteExceptions
        {
            get { return _listener.IgnoreWriteExceptions; }
            set { _listener.IgnoreWriteExceptions = value; }
        }

        /// <summary>
        /// Gets or sets the realm, or resource partition, associated with this HttpListener object
        /// </summary>
        public string Realm
        {
            get { return _listener.Realm; }
            set { _listener.Realm = value; }
        }

        /// <summary>
        /// Gets a value that indicates whether HttpListener can be used with the current operating system
        /// </summary>
        public bool IsSupported => System.Net.HttpListener.IsSupported;

        /// <summary>
        /// Gets or sets a Boolean value that controls whether, when NTLM is used, additional requests using the same Transmission Control Protocol (TCP) connection are required to authenticate
        /// </summary>
        public bool UnsafeConnectionNtlmAuthentication
        {
            get { return _listener.UnsafeConnectionNtlmAuthentication; }
            set { _listener.UnsafeConnectionNtlmAuthentication = value; }
        }

        /// <summary>
        /// Shuts down the HttpListener object immediately, discarding all currently queued requests
        /// </summary>
        public void Abort()
        {
            _listener.Abort();
        }

        /// <summary>
        /// Shuts down the HttpListener
        /// </summary>
        public void Close()
        {
            _listener.Close();
        }

        /// <summary>
        /// Allows this instance to receive incoming requests
        /// </summary>
        public void Start()
        {
            _listener.Start();
        }

        /// <summary>
        /// Causes this instance to stop receiving incoming requests
        /// </summary>
        public void Stop()
        {
            _listener.Stop();
        }
    }
}