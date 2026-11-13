using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Aether.Modding;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Aether.Testing;

/// <summary>
/// An MCP server over Streamable HTTP, stateless, on <see cref="HttpListener"/>: the MCP C# SDK's
/// transport and server (ModelContextProtocol.Core) without ASP.NET Core, so the game needs only the
/// .NET runtime. Each POST is served as the SDK's own ASP.NET handler serves a stateless request: a
/// fresh transport and server answer that one message, as a server-sent event stream.
/// </summary>
public sealed class McpHttpServer : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly McpServerOptions _options;
    private readonly CancellationTokenSource _stopping = new();

    public McpHttpServer(int port, Implementation info, string instructions, IEnumerable<Type> toolTypes)
    {
        var tools = new McpServerPrimitiveCollection<McpServerTool>();
        foreach (Type type in toolTypes)
        {
            foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if (method.GetCustomAttribute<McpServerToolAttribute>() != null)
                {
                    tools.Add(McpServerTool.Create(method, target: null));
                }
            }
        }
        _options = new McpServerOptions
        {
            ServerInfo = info,
            ServerInstructions = instructions,
            ToolCollection = tools,
            // Each request's server lives only as long as the request.
            ScopeRequests = false,
        };
        _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
    }

    public void Start()
    {
        _listener.Start();
        _ = AcceptAsync();
    }

    public void Dispose()
    {
        _stopping.Cancel();
        _listener.Close();
    }

    private async Task AcceptAsync()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (Exception) when (!_listener.IsListening)
            {
                return;
            }
            _ = Task.Run(() => ServeAsync(context));
        }
    }

    private async Task ServeAsync(HttpListenerContext context)
    {
        HttpListenerResponse response = context.Response;
        try
        {
            if (context.Request.Url?.AbsolutePath.TrimEnd('/') != "/mcp")
            {
                response.StatusCode = 404;
            }
            else if (context.Request.HttpMethod != "POST")
            {
                // Stateless: no server-initiated stream (GET) and no session to end (DELETE).
                response.StatusCode = 405;
                response.AddHeader("Allow", "POST");
            }
            else
            {
                await PostAsync(context);
            }
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
        {
            // The game is closing (game_quit's own request ends this way, after its answer).
        }
        catch (Exception exception)
        {
            AetherLog.For("Aether.Mcp").Warning(exception, "MCP request failed");
            try
            {
                response.StatusCode = 500;
            }
            catch (InvalidOperationException)
            {
                // The response had already started.
            }
        }
        finally
        {
            response.Close();
        }
    }

    private async Task PostAsync(HttpListenerContext context)
    {
        HttpListenerResponse response = context.Response;
        JsonRpcMessage message;
        try
        {
            message = await JsonSerializer.DeserializeAsync<JsonRpcMessage>(context.Request.InputStream, McpJsonUtilities.DefaultOptions);
        }
        catch (JsonException)
        {
            message = null;
        }
        if (message == null)
        {
            response.StatusCode = 400;
            return;
        }
        if (context.Request.Headers["MCP-Protocol-Version"] is { Length: > 0 } version)
        {
            message.Context ??= new();
            message.Context.ProtocolVersion = version;
        }

        var transport = new StreamableHttpServerTransport(NullLoggerFactory.Instance) { Stateless = true };
        McpServer server = McpServer.Create(transport, _options, NullLoggerFactory.Instance);
        using var serverStopped = CancellationTokenSource.CreateLinkedTokenSource(_stopping.Token);
        Task running = server.RunAsync(serverStopped.Token);
        try
        {
            response.ContentType = "text/event-stream";
            response.AddHeader("Cache-Control", "no-cache,no-store");
            response.SendChunked = true;
            bool wrote = await transport.HandlePostRequestAsync(message, response.OutputStream, _stopping.Token);
            if (!wrote)
            {
                // A notification or response from the client: nothing to answer.
                response.ContentType = null;
                response.SendChunked = false;
                response.StatusCode = 202;
            }
        }
        finally
        {
            // As the SDK's handler: the transport first, so the server's reader ends gracefully.
            await transport.DisposeAsync();
            await serverStopped.CancelAsync();
            try
            {
                await running;
            }
            catch (OperationCanceledException)
            {
            }
            await server.DisposeAsync();
        }
    }
}
