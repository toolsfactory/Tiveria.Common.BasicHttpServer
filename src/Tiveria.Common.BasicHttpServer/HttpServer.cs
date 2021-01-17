using Microsoft.Extensions.Logging;
using System;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Tiveria.Common;

/// <summary>
/// Based on https://github.com/dajuric/simple-http
/// </summary>
namespace Tiveria.Common.BasicHttpServer
{
    public class HttpServer
    {
        #region public properties
        public string ListenerPrefix { get; }
        public byte MaxHttpConnectionCount { get; }
        public bool IsStarted { get; private set; }
        public bool HttpsEnabled { get; private set; }
        #endregion

        #region private fields
        private readonly HttpListener _listener = new HttpListener();
        private readonly ILogger<HttpServer> _logger;
        private CancellationTokenSource _tokenSource = new CancellationTokenSource();
        #endregion

        #region constructor
        public HttpServer(ILogger<HttpServer> logger, int port, bool useHttps = false, byte maxHttpConnectionCount = 32)
        {
            Ensure.That(port).IsInRange(0, UInt16.MaxValue);
            Ensure.That(MaxHttpConnectionCount).IsInRange(0, 32);

            var s = useHttps ? "s" : String.Empty;
            ListenerPrefix = $"http{s}://+:{port}/";
            _logger = logger;
            MaxHttpConnectionCount = maxHttpConnectionCount;

            TryAddPrefix();
        }

        private void TryAddPrefix()
        {
            try
            {
                _listener.Prefixes.Add(ListenerPrefix);
            }
            catch (Exception ex)
            {
                throw new ArgumentException("Invalid Prefix. Format must be 'http(s)://+:(port)/'", ex);
            }
        }
        #endregion

        /// <summary>
        /// Creates and starts a new instance of the http(s) server.
        /// </summary>
        /// <param name="port">The http/https URI listening port.</param>
        /// <param name="token">Cancellation token.</param>
        /// <param name="onHttpRequestAsync">Action executed on HTTP request.</param>
        /// <param name="useHttps">True to add 'https://' prefix insteaad of 'http://'.</param>
        /// <param name="maxHttpConnectionCount">Maximum HTTP connection count, after which the incoming requests will wait (sockets are not included).</param>
        /// <returns>Server listening task.</returns>
        public async Task StartAsync(Func<HttpListenerRequest, HttpListenerResponse, Task> onHttpRequestAsync, CancellationToken ct)
        {
            Ensure.That(IsStarted).IsFalse().WithExtraMessageOf(() => "Server already started");
            Ensure.That(onHttpRequestAsync).IsNotNull();

            var linkedCTS = CancellationTokenSource.CreateLinkedTokenSource(ct, _tokenSource.Token);
            TryStartListener();

            using (var s = new SemaphoreSlim(MaxHttpConnectionCount))
            {
                while (!linkedCTS.IsCancellationRequested)
                {
                    try
                    {
                        _logger.LogDebug("Waiting for requests");
                        var ctx = await _listener.GetContextAsync();
                        _logger.LogDebug("Request received: {context}", ctx);

                        if (ctx.Request.IsWebSocketRequest)
                        {
                            _logger.LogDebug("WebSocket request. Closing.");
                            ctx.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                            ctx.Response.Close();
                        }
                        else
                        {
                            _logger.LogDebug("Waiting for Requesthandler");
                            await s.WaitAsync();
                            _logger.LogDebug("Calling Requesthandler");
                            Task.Factory.StartNew(() => onHttpRequestAsync(ctx.Request, ctx.Response), TaskCreationOptions.None)
                                        .ContinueWith(t => s.Release())
                                        .Wait(0);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        _listener.Close();
                    }
                    _logger.LogDebug("RequestLoop finished");
                }
                _listener.Stop();
            }
        }

        private void TryStartListener()
        {
            try
            {
                _logger.LogInformation("Starting listener");
                _listener.Start();
            }
            catch (Exception ex) when ((ex as HttpListenerException)?.ErrorCode == 5)
            {
                var msg = getNamespaceReservationExceptionMessage(ListenerPrefix);
                _logger.LogError(ex, msg);
                throw new UnauthorizedAccessException(msg, ex);
            }
        }

        public async Task StartAsync(Func<HttpListenerRequest, HttpListenerResponse, Task> onHttpRequestAsync)
        {
            await StartAsync(onHttpRequestAsync, _tokenSource.Token);
        }

        public void Stop()
        {
            if (_tokenSource != null)
                _tokenSource.Cancel();
        }

        static string getNamespaceReservationExceptionMessage(string httpListenerPrefix)
        {
            var m = Regex.Match(httpListenerPrefix, @"(?<protocol>\w+)://localhost:?(?<port>\d*)");

            if (m.Success)
            {
                var protocol = m.Groups["protocol"].Value;
                var port = m.Groups["port"].Value; if (String.IsNullOrEmpty(port)) port = 80.ToString();

                return  $"HTTP server can not start. Namespace reservation already exists.{Environment.NewLine}" +
                        $"On Windows run: 'netsh http delete urlacl url={protocol}://+:{port}/'.";
            }
            else
            {
                return $"HTTP server can not start. Namespace reservation does not exist.{Environment.NewLine}" +
                        $"On Windows run: 'netsh http add urlacl url={httpListenerPrefix} user=\"Everyone\"'. (Or 'Jeder' in german...')";
            }
        }
    }
}