using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;

namespace BluetoothHeartrateModule
{
    internal class WebsocketHeartrateServer
    {

        BluetoothHeartrateModule module;
        static ConcurrentDictionary<Guid, WebSocket> connectedClients = new();
        static CancellationTokenSource? serverCancellation;
        static HttpListener? httpListener;

        public Action<Guid>? OnClientConnect;

        public WebsocketHeartrateServer(BluetoothHeartrateModule module)
        {
            this.module = module;
        }

        internal async Task Start()
        {
            connectedClients.Clear();
            ResetServerCancellation();

            if (!module.GetWebocketEnabledSetting())
            {
                return;
            }
            serverCancellation = new CancellationTokenSource();

            var httpAddress = GetListenAddress();
            var wsAddress = GetListenAddress("ws");
            if (httpListener == null)
            {
                httpListener = new HttpListener();
                httpListener.Prefixes.Add(httpAddress);
            }
            if (!httpListener.IsListening)
            {
                module.Log($"Starting Websocket server on {httpAddress}");
                try
                {
                    httpListener.Start();
                }
                catch (HttpListenerException ex)
                {
                    module.Log($"Failed to start WebSocket server: {ex.Message}");
                    return;
                }
            }

            module.Log($"Websocket server is running, connnect using {wsAddress}");

            try
            {
                _ = HandleServerCommands();
                while (true)
                {
                    var context = await httpListener.GetContextAsync();
                    if (context.Request.IsWebSocketRequest)
                    {
                        var webSocketContext = await context.AcceptWebSocketAsync(null);
                        var clientId = Guid.NewGuid();
                        connectedClients.TryAdd(clientId, webSocketContext.WebSocket);
                        module.Log($"Websocket client {clientId} connected.");
                        OnClientConnect?.Invoke(clientId);

                        _ = HandleWebSocketConnection(clientId, webSocketContext.WebSocket);
                    }
                    else
                    {
                        context.Response.StatusCode = 400;
                        context.Response.Close();
                    }
                }
            }
            catch (Exception ex) when (ex is HttpListenerException || ex is OperationCanceledException)
            {
                // Ignore exceptions caused by stopping the server
            }
            finally
            {
                StopHttpListener();
                serverCancellation?.Dispose();
                module.Log("Websocket server stopped");
            }
        }

        private void ResetServerCancellation()
        {
            if (serverCancellation != null)
            {
                serverCancellation.Cancel();
                serverCancellation.Dispose();
                serverCancellation = null;
            }
        }

        private void StopHttpListener()
        {
            if (httpListener?.IsListening == true)
            {
                httpListener.Stop();
            }
        }

        // Public method to send an int message to all clients
        internal async Task SendIntMessage(int message)
        {
            if (!module.GetWebocketEnabledSetting())
            {
                Stop();
                return;
            }

            var messageBuffer = new ArraySegment<byte>(Converter.GetAsciiStringInt(message));
            foreach (var clientId in connectedClients.Keys)
            {
                await SendMessage(clientId, messageBuffer);
            }
        }
        // Public method to send an int message to specific client
        internal async Task SendIntMessage(int message, Guid clientId)
        {
            var messageBuffer = new ArraySegment<byte>(Converter.GetAsciiStringInt(message));
            await SendMessage(clientId, messageBuffer);
        }

        // Public method to stop the server
        internal void Stop()
        {
            module.Log($"WebSocket server stopping");
            ResetServerCancellation();
            StopHttpListener();
        }

        private string GetListenAddress()
        {
            return GetListenAddress("http");
        }
        private string GetListenAddress(string protocol)
        {
            return $"{protocol}://{module.GetWebocketHostSetting()}:{module.GetWebocketPortSetting()}/";
        }
        private async Task HandleWebSocketConnection(Guid clientId, WebSocket webSocket)
        {
            try
            {
                while (webSocket.State == WebSocketState.Open)
                {
                    // Wait for incoming connections
                    await Task.Delay(-1);
                }
            }
            catch (WebSocketException ex)
            {
                module.Log($"WebSocket error for Client {clientId}: {ex}");
            }
            finally
            {
                DisconnectClient(clientId);
            }
        }

        private async void DisconnectClient(Guid clientId)
        {
            WebSocket? removedClient;
            connectedClients.TryRemove(clientId, out removedClient);
            if (removedClient != null)
            {
                await removedClient.CloseAsync(WebSocketCloseStatus.EndpointUnavailable, null, new CancellationToken());
                removedClient.Dispose();
            }
            module.Log($"WebSocket client {clientId} disconnected.");
        }

        // Public method to send a byte message to a specific client
        private async Task SendMessage(Guid clientId, ArraySegment<byte> buffer)
        {
            if (!module.GetWebocketEnabledSetting())
            {
                Stop();
                return;
            }

            if (connectedClients.TryGetValue(clientId, out WebSocket? webSocket))
            {
                if (webSocket == null || webSocket.State != WebSocketState.Open)
                {
                    return;
                }

                try
                {
                    await webSocket.SendAsync(buffer, WebSocketMessageType.Text, true, CancellationToken.None);
                }
                catch (WebSocketException ex)
                {
                    if (ex.InnerException is HttpListenerException)
                    {
                        HttpListenerException iex = (HttpListenerException)ex.InnerException;
                        // System.Net.HttpListenerException (1229): An operation was attempted on a nonexistent network connection.
                        if (iex.ErrorCode == 1229)
                        {
                            DisconnectClient(clientId);
                            return;
                        }
                    }
                    module.Log($"WebSocket send error for Client {clientId}: {ex}");
                }
            }
            else
            {
                module.Log($"WebSocket client {clientId} not found");
            }
        }

        private async Task HandleServerCommands()
        {
            if (serverCancellation == null) { return; }

            await Task.Delay(-1, serverCancellation.Token);
            Stop();
        }
    }
}

