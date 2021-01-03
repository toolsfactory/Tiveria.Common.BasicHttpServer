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
        private readonly HttpListener _listener = new HttpListener();
        private CancellationTokenSource _tokenSource;

        public string ListenerPrefix { get; }
        public byte MaxHttpConnectionCount { get; }
        public bool IsStarted { get; private set; }

        public HttpServer(int port, bool useHttps = false, byte maxHttpConnectionCount = 32)
        {
            Ensure.That(port).IsInRange(0, UInt16.MaxValue);
            Ensure.That(MaxHttpConnectionCount).IsInRange(0, 32);

            var s = useHttps ? "s" : String.Empty;
            ListenerPrefix = $"http{s}://+:{port}/";
            
            try 
            { 
                _listener.Prefixes.Add(ListenerPrefix); 
            }
            catch (Exception ex) 
            { 
                throw new ArgumentException("Invalid Prefix. Format must be 'http(s)://+:(port)/'", ex); 
            }
            MaxHttpConnectionCount = maxHttpConnectionCount;
        }

        /// <summary>
        /// Creates and starts a new instance of the http(s) server.
        /// </summary>
        /// <param name="port">The http/https URI listening port.</param>
        /// <param name="token">Cancellation token.</param>
        /// <param name="onHttpRequestAsync">Action executed on HTTP request.</param>
        /// <param name="useHttps">True to add 'https://' prefix insteaad of 'http://'.</param>
        /// <param name="maxHttpConnectionCount">Maximum HTTP connection count, after which the incoming requests will wait (sockets are not included).</param>
        /// <returns>Server listening task.</returns>
        public async Task StartAsync(Func<HttpListenerRequest, HttpListenerResponse, Task> onHttpRequestAsync)
        {
            Ensure.That(IsStarted).IsFalse().WithExtraMessageOf(() => "Server already started");

            _tokenSource = new CancellationTokenSource();
            Ensure.That(onHttpRequestAsync).IsNotNull();

            try 
            { 
                _listener.Start(); 
            }
            catch (Exception ex) when ((ex as HttpListenerException)?.ErrorCode == 5)
            {
                var msg = getNamespaceReservationExceptionMessage(ListenerPrefix);
                throw new UnauthorizedAccessException(msg, ex);
            }

            using (var s = new SemaphoreSlim(MaxHttpConnectionCount))
            using (var r = _tokenSource.Token.Register(() => _listener.Close()))
            {
                bool cancel = false;
                while (!cancel)
                {
                    try
                    {
                        var ctx = await _listener.GetContextAsync();

                        if (ctx.Request.IsWebSocketRequest)
                        {
                            ctx.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                            ctx.Response.Close();
                        }
                        else
                        {
                            await s.WaitAsync();
                            Task.Factory.StartNew(() => onHttpRequestAsync(ctx.Request, ctx.Response), TaskCreationOptions.None)
                                        .ContinueWith(t => s.Release())
                                        .Wait(0);
                        }
                    }
                    catch (Exception)
                    {
                        if (!_tokenSource.Token.IsCancellationRequested)
                            throw;
                    }
                    finally
                    {
                        if (_tokenSource.Token.IsCancellationRequested)
                            cancel = true;
                    }
                }
                _listener.Stop();
            }
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
                        $"On Windows run: 'netsh http add urlacl url={httpListenerPrefix} user=\"Everyone\"'.";
            }
        }
    }
}