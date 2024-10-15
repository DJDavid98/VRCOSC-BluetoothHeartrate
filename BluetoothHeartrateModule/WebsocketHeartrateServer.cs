using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;

namespace BluetoothHeartrateModule
{
    internal class WebsocketHeartrateServer
    {
        private readonly BluetoothHeartrateModule module;
        private readonly AsyncHelper ah;
        private static readonly ConcurrentDictionary<Guid, WebSocket> connectedClients = new();
        private static CancellationTokenSource? serverCancellation;
        private static HttpListener? httpListener;

        public WebsocketHeartrateServer(BluetoothHeartrateModule module)
        {
            this.module = module;
            this.ah = module.ah;
        }

        internal async Task Start()
        {
            module.LogDebug("Starting WebSocket server");

            module.LogDebug("Clearing connected clients");
            connectedClients.Clear();
            module.LogDebug("Resetting server cancellation token");
            ResetServerCancellation();

            if (!module.GetWebocketEnabledSetting())
            {
                module.LogDebug("WebSocket server is disabled, start aborted");
                return;
            }
            module.LogDebug("Creating new cancellation token");
            serverCancellation = new CancellationTokenSource();

            module.LogDebug("Determining listen addresses");
            var httpAddress = GetListenAddress("http");
            var wsAddress = GetListenAddress("ws");
            if (httpListener == null)
            {
                module.LogDebug("Creating HTTP listener");
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
                        module.LogDebug("Accepting WebSocket request");
                        var webSocketContext = await context.AcceptWebSocketAsync(null);
                        var clientId = Guid.NewGuid();
                        module.LogDebug("Storing connected client");
                        connectedClients.TryAdd(clientId, webSocketContext.WebSocket);
                        module.Log($"Websocket client {clientId} connected.");

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
                module.LogDebug("Caught exception while stopping WebSocket server");
            }
            finally
            {
                StopHttpListener();
                ResetServerCancellation();
                module.Log("Websocket server stopped");
            }
        }

        private void ResetServerCancellation()
        {
            if (serverCancellation != null)
            {
                module.LogDebug("Resetting server cancellation");
                serverCancellation.Cancel();
                serverCancellation.Dispose();
                serverCancellation = null;
            }
        }

        private void StopHttpListener()
        {
            if (httpListener?.IsListening == true)
            {
                module.LogDebug("Stopping HTTP listener");
                httpListener.Stop();
            }
        }

        // Public method to send an int message to all clients
        internal async Task SendIntMessage(int message)
        {
            module.LogDebug($"Sending message {message} to all clients");
            var messageBuffer = new ArraySegment<byte>(Converter.GetAsciiStringInt(message));
            foreach (var clientId in connectedClients.Keys)
            {
                await SendMessage(clientId, messageBuffer);
            }
        }
        // Public method to send an int message to specific client
        internal async Task SendIntMessage(int message, Guid clientId)
        {
            module.LogDebug($"Sending message {message} to client {clientId}");
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
            module.LogDebug($"Disconnecting client {clientId}");
            connectedClients.TryRemove(clientId, out WebSocket? removedClient);
            if (removedClient != null)
            {
                await ah.WaitAsyncVoid(removedClient.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None), AsyncTask.CloseWebsocketConnection);
                removedClient.Dispose();
            }
            module.Log($"WebSocket client {clientId} disconnected.");
        }

        // Public method to send a byte message to a specific client
        private async Task SendMessage(Guid clientId, ArraySegment<byte> buffer)
        {
            if (!module.GetWebocketEnabledSetting())
            {
                module.LogDebug("WebSocket server found to be disbled while sending message, stopping");
                Stop();
                return;
            }

            module.LogDebug($"Trying to find client {clientId} for messaging");
            if (connectedClients.TryGetValue(clientId, out WebSocket? webSocket))
            {
                if (webSocket == null || webSocket.State != WebSocketState.Open)
                {
                    module.LogDebug($"Cound not find open socket for client {clientId}, skipping");
                    return;
                }

                try
                {
                    module.LogDebug($"Sending message to client {clientId}");
                    await webSocket.SendAsync(buffer, WebSocketMessageType.Text, true, CancellationToken.None);
                }
                catch (WebSocketException ex)
                {
                    if (ex.InnerException is HttpListenerException iex)
                    {
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

