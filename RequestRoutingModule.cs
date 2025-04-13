using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Xml.Linq;

namespace RequestRouting
{
    public enum RoutingMode
    {
        RO, // Route Old (Default)
        RN, // Route New
        RP  // Run Parallel
    }

    /// <summary>
    /// HTTP Module that provides request routing capabilities for splitting traffic between old and new services.
    /// </summary>
    public class RequestRoutingModule : IHttpModule
    {
        #region Private Fields
        
        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        
        // Configuration Keys
        private const string TargetPath = "/dctserver.aspx";
        private const string CfgNewService = "Routing.NewService";
        private const string CfgGetMode = "Routing.GET";
        private const string CfgPostMode = "Routing.POST";
        private const string ForwardedHeader = "X-RequestRouting-Forwarded";
        
        // Context Keys
        private const string CtxCtsKey = "RequestRoutingModule_Cts";
        private const string CtxParallelTaskKey = "RequestRoutingModule_ParallelTask";
        private const string CtxResponseFilterKey = "RequestRoutingModule_ResponseFilter";
        
        #endregion

        #region IHttpModule Implementation
        
        public void Init(HttpApplication context)
        {
            context.PostMapRequestHandler += HandleRequest;
            context.EndRequest += HandleEndRequest;
        }

        public void Dispose() { /* No explicit disposal needed for static HttpClient */ }
        
        #endregion

        #region Request Handling
        
        /// <summary>
        /// Main request handler executed after request mapping.
        /// Determines routing mode and executes the appropriate action.
        /// </summary>
        private void HandleRequest(object sender, EventArgs e)
        {
            var application = (HttpApplication)sender;
            var context = application.Context;

            // Skip if already forwarded or not targeting our path
            if (context.Request.Headers[ForwardedHeader] == "true" || 
                !context.Request.Path.Equals(TargetPath, StringComparison.OrdinalIgnoreCase))
                return;

            var config = ReadConfiguration();
            if (!config.IsValid)
            {
                LogError("Invalid configuration detected. Defaulting all requests to RO.");
                return;
            }

            var effectiveMode = DetermineEffectiveMode(context.Request, config);

            switch (effectiveMode)
            {
                case RoutingMode.RN:
                    HandleRouteNew(application, context, config);
                    break;
                case RoutingMode.RP:
                    HandleRunParallel(context, config);
                    break;
                case RoutingMode.RO:
                default:
                    LogInfo("RO mode: Request proceeding to original handler.");
                    break;
            }
        }

        /// <summary>
        /// Handles the Route New (RN) scenario: forwards the request synchronously and completes the response.
        /// </summary>
        private void HandleRouteNew(HttpApplication application, HttpContext context, RoutingConfig config)
        {
            LogInfo($"RN mode: Routing request synchronously to {config.NewServiceUrl}");
            try
            {
                var headers = GetRequestHeaders(context.Request);
                var requestBody = context.Request.HttpMethod == "POST" ? ReadRequestBody(context.Request) : null;
                
                using (var response = SendForwardedRequest(config.NewServiceUrl, context.Request.HttpMethod, 
                                                         headers, context.Request.ContentType, requestBody))
                {
                    if (response != null)
                    {
                        context.Response.StatusCode = (int)response.StatusCode;
                        context.Response.StatusDescription = response.ReasonPhrase;
                        CopyResponseHeadersAndBody(response, context.Response);
                    }
                    else
                    {
                        SetErrorResponse(context.Response, 503, "Error contacting backend service");
                    }
                }
            }
            catch (Exception ex)
            {
                LogError($"Error during RN request to {config.NewServiceUrl}: {ex.Message}");
                SetErrorResponse(context.Response, 500, "Error routing request");
            }
            application.CompleteRequest();
        }

        /// <summary>
        /// Sets up the Run Parallel (RP) scenario: starts background task and injects filter.
        /// </summary>
        private void HandleRunParallel(HttpContext context, RoutingConfig config)
        {
            LogInfo($"RP mode: Starting parallel request to {config.NewServiceUrl}");
            var cts = new CancellationTokenSource();

            // Prepare request data
            var httpMethod = context.Request.HttpMethod;
            var contentType = context.Request.ContentType;
            var requestBody = ReadRequestBody(context.Request);
            var headers = GetRequestHeaders(context.Request);

            // Start parallel request
            var parallelTask = Task.Run(() => 
                RunParallelRequestAsync(config.NewServiceUrl, httpMethod, contentType, headers, requestBody, cts.Token), 
                cts.Token);

            // Inject response filter
            var captureFilter = new CaptureFilterStream(context.Response.Filter);
            context.Response.Filter = captureFilter;

            // Store context for EndRequest
            context.Items[CtxCtsKey] = cts;
            context.Items[CtxParallelTaskKey] = parallelTask;
            context.Items[CtxResponseFilterKey] = captureFilter;

            LogInfo("RP mode: Parallel task started, original request proceeding.");
        }

        /// <summary>
        /// Event handler for EndRequest, performs cleanup and comparison for RP mode.
        /// </summary>
        private void HandleEndRequest(object sender, EventArgs e)
        {
            var context = ((HttpApplication)sender).Context;

            if (!HasParallelTaskContext(context))
                return;
                
            var cts = context.Items[CtxCtsKey] as CancellationTokenSource;
            var parallelTask = context.Items[CtxParallelTaskKey] as Task<string>;
            var filter = context.Items[CtxResponseFilterKey] as CaptureFilterStream;

            try
            {
                // Signal cancellation
                cts?.Cancel();
                
                // Compare if task completed
                if (parallelTask != null && filter != null && 
                    parallelTask.IsCompleted && !parallelTask.IsFaulted && !parallelTask.IsCanceled)
                {
                    var newResponseBody = parallelTask.Result;
                    var originalResponseBody = filter.GetCapturedData();
                    
                    LogInfo("Parallel task completed. Comparing responses.");
                    CompareResponses(originalResponseBody, newResponseBody);
                }
            }
            catch (Exception ex)
            {
                LogError($"Error in EndRequest: {ex.Message}");
            }
            finally
            {
                // Clean up resources
                context.Items.Remove(CtxCtsKey);
                context.Items.Remove(CtxParallelTaskKey);
                context.Items.Remove(CtxResponseFilterKey);
                cts?.Dispose();
            }
        }
        
        #endregion

        #region Configuration
        
        /// <summary>
        /// Reads and validates configuration settings.
        /// </summary>
        private RoutingConfig ReadConfiguration()
        {
            var config = new RoutingConfig
            {
                NewServiceUrl = ConfigurationManager.AppSettings[CfgNewService]
            };
            
            bool success = true;
            config.GetMode = ParseRoutingMode(ConfigurationManager.AppSettings[CfgGetMode], CfgGetMode, ref success);
            config.PostModes = ParsePostModes(ConfigurationManager.AppSettings[CfgPostMode], CfgPostMode, ref success);

            // Validate URL is present when needed
            bool requiresUrl = (config.GetMode != RoutingMode.RO || config.PostModes.Any(pm => pm.Value != RoutingMode.RO));
            if (requiresUrl && string.IsNullOrWhiteSpace(config.NewServiceUrl))
            {
                LogError("Routing.NewService URL is required for RN/RP modes but is not configured.");
                success = false;
            }
            
            config.IsValid = success;
            return config;
        }

        /// <summary>
        /// Determines the effective routing mode based on request type and configuration.
        /// </summary>
        private RoutingMode DetermineEffectiveMode(HttpRequest request, RoutingConfig config)
        {
            if (request.HttpMethod.Equals("GET", StringComparison.OrdinalIgnoreCase))
                return config.GetMode;
                
            if (!request.HttpMethod.Equals("POST", StringComparison.OrdinalIgnoreCase))
                return RoutingMode.RO;
                
            if (!config.PostModes.Any())
                return RoutingMode.RO;

            var requestBody = ReadRequestBody(request);
            if (requestBody == null || requestBody.Length == 0)
            {
                LogInfo("Empty POST body received. Using RO mode.");
                return RoutingMode.RO;
            }

            try
            {
                var content = Encoding.UTF8.GetString(requestBody);
                var doc = XDocument.Parse(content);
                var requestsElement = doc.Root?.Element("requests");

                if (requestsElement == null)
                {
                    LogInfo("POST XML missing <requests> element. Using RO mode.");
                    return RoutingMode.RO;
                }

                // Find matching request type
                var matchedRule = requestsElement.Elements()
                    .Select(el => new { 
                        Name = el.Name.LocalName, 
                        Mode = config.PostModes.TryGetValue(el.Name.LocalName, out var mode) ? mode : (RoutingMode?)null 
                    })
                    .FirstOrDefault(rule => rule.Mode.HasValue);

                if (matchedRule != null)
                {
                    LogInfo($"Found matching request '{matchedRule.Name}'. Using mode: {matchedRule.Mode.Value}");
                    return matchedRule.Mode.Value;
                }

                LogInfo("No configured request type found in POST body. Using RO mode.");
            }
            catch (Exception ex)
            {
                LogError($"Error parsing POST XML body: {ex.Message}. Using RO mode.");
            }
            
            return RoutingMode.RO;
        }
        
        private RoutingMode ParseRoutingMode(string configValue, string configKey, ref bool successFlag)
        {
            if (string.IsNullOrWhiteSpace(configValue)) 
                return RoutingMode.RO;
                
            if (Enum.TryParse<RoutingMode>(configValue.Trim(), true, out RoutingMode mode)) 
                return mode;
                
            LogError($"Invalid value '{configValue}' for config '{configKey}'. Expected RO/RN/RP. Using RO.");
            successFlag = false;
            return RoutingMode.RO;
        }
        
        private Dictionary<string, RoutingMode> ParsePostModes(string configValue, string configKey, ref bool successFlag)
        {
            var modes = new Dictionary<string, RoutingMode>(StringComparer.OrdinalIgnoreCase);
            
            if (string.IsNullOrWhiteSpace(configValue)) 
                return modes;
                
            foreach (var pair in configValue.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] parts = pair.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2)
                {
                    string requestName = parts[0].Trim();
                    string modeStr = parts[1].Trim();
                    
                    if (!string.IsNullOrWhiteSpace(requestName) && 
                        Enum.TryParse<RoutingMode>(modeStr, true, out RoutingMode mode))
                    {
                        modes[requestName] = mode;
                    }
                    else
                    {
                        LogError($"Invalid format/mode in '{configKey}': Pair '{pair}'. Ignoring.");
                        successFlag = false;
                    }
                }
                else
                {
                    LogError($"Invalid format in '{configKey}': Expected 'RqName | Mode' in pair '{pair}'. Ignoring.");
                    successFlag = false;
                }
            }
            return modes;
        }
        
        #endregion

        #region HTTP Operations
        
        /// <summary>
        /// Sends a forwarded HTTP request synchronously.
        /// </summary>
        private HttpResponseMessage SendForwardedRequest(string targetUrl, string httpMethod, 
            Dictionary<string, string> headers, string contentType, byte[] requestBody)
        {
            try
            {
                using (var requestMessage = new HttpRequestMessage(new HttpMethod(httpMethod), targetUrl))
                {
                    // Mark as forwarded to prevent loops
                    requestMessage.Headers.TryAddWithoutValidation(ForwardedHeader, "true");
                    
                    // Copy headers and set body
                    CopyRequestHeaders(headers, requestMessage);
                    SetRequestBody(contentType, requestBody, requestMessage);
                    
                    LogInfo($"Sending synchronous request: {httpMethod} {targetUrl}");
                    return _httpClient.SendAsync(requestMessage, HttpCompletionOption.ResponseContentRead)
                        .ConfigureAwait(false).GetAwaiter().GetResult();
                }
            }
            catch (Exception ex)
            {
                LogError($"Error sending request to {targetUrl}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Runs a parallel request asynchronously and returns the response body.
        /// </summary>
        private async Task<string> RunParallelRequestAsync(string targetUrl, string httpMethod, 
            string contentType, Dictionary<string, string> headers, byte[] requestBody, CancellationToken cancellationToken)
        {
            DateTime startTime = DateTime.UtcNow;
            HttpResponseMessage response = null;
            
            try
            {
                response = await SendForwardedRequestAsync(targetUrl, httpMethod, headers, contentType, requestBody, cancellationToken);
                if (response == null)
                    return null;
                    
                // Check for cancellation before reading content
                cancellationToken.ThrowIfCancellationRequested();

                var duration = DateTime.UtcNow - startTime;
                if (response.IsSuccessStatusCode && response.Content != null)
                {
                    var responseBody = await response.Content.ReadAsStringAsync();
                    LogInfo($"Parallel request completed in {duration.TotalMilliseconds:F0}ms. Status: {response.StatusCode}");
                    return responseBody;
                }
                
                LogError($"Parallel request failed in {duration.TotalMilliseconds:F0}ms. Status: {response.StatusCode}");
                return null;
            }
            catch (OperationCanceledException)
            {
                LogInfo($"Parallel request to {targetUrl} was cancelled.");
                return null;
            }
            catch (Exception ex)
            {
                var duration = DateTime.UtcNow - startTime;
                LogError($"Error in parallel request to {targetUrl} after {duration.TotalMilliseconds:F0}ms: {ex.Message}");
                return null;
            }
            finally
            {
                response?.Dispose();
            }
        }

        /// <summary>
        /// Sends a forwarded HTTP request asynchronously.
        /// </summary>
        private async Task<HttpResponseMessage> SendForwardedRequestAsync(string targetUrl, string httpMethod, 
            Dictionary<string, string> headers, string contentType, byte[] requestBody, CancellationToken cancellationToken)
        {
            try
            {
                using (var requestMessage = new HttpRequestMessage(new HttpMethod(httpMethod), targetUrl))
                {
                    requestMessage.Headers.TryAddWithoutValidation(ForwardedHeader, "true");
                    CopyRequestHeaders(headers, requestMessage);
                    SetRequestBody(contentType, requestBody, requestMessage);
                    
                    LogInfo($"Sending async request: {httpMethod} {targetUrl}");
                    return await _httpClient.SendAsync(requestMessage, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            { 
                LogInfo($"Async request to {targetUrl} cancelled."); 
                return null; 
            }
            catch (Exception ex)
            { 
                LogError($"Error sending async request to {targetUrl}: {ex.Message}"); 
                return null; 
            }
        }
        
        #endregion

        #region Helper Methods
        
        private Dictionary<string, string> GetRequestHeaders(HttpRequest request)
        {
            return request.Headers.AllKeys.ToDictionary(k => k, k => request.Headers[k]);
        }
        
        private byte[] ReadRequestBody(HttpRequest request)
        {
            if (!request.InputStream.CanRead || request.InputStream.Length == 0) 
                return null;
                
            try
            {
                request.InputStream.Position = 0;
                using (var ms = new MemoryStream((int)request.InputStream.Length))
                {
                    request.InputStream.CopyTo(ms);
                    request.InputStream.Position = 0; // Reset for other handlers
                    return ms.ToArray();
                }
            }
            catch (Exception ex)
            {
                LogError($"Error reading request body: {ex.Message}");
                return null;
            }
        }

        private void CopyRequestHeaders(Dictionary<string, string> sourceHeaders, HttpRequestMessage targetRequest)
        {
            var headersToExclude = new HashSet<string>(StringComparer.OrdinalIgnoreCase) 
                { "Host", "Connection", "Content-Length", "Expect", "Transfer-Encoding", "Content-Type" };
                
            foreach (var header in sourceHeaders.Where(h => !string.IsNullOrWhiteSpace(h.Value) && 
                                                           !headersToExclude.Contains(h.Key)))
            {
                targetRequest.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        private void SetRequestBody(string contentType, byte[] requestBody, HttpRequestMessage targetRequest)
        {
            if (requestBody == null || requestBody.Length == 0 || 
                (targetRequest.Method != HttpMethod.Post && targetRequest.Method != HttpMethod.Put))
                return;
                
            targetRequest.Content = new ByteArrayContent(requestBody);
            
            if (!string.IsNullOrEmpty(contentType))
            {
                try 
                { 
                    targetRequest.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType); 
                }
                catch (FormatException ex) 
                { 
                    LogInfo($"Could not parse Content-Type header '{contentType}': {ex.Message}"); 
                }
            }
        }

        private void CopyResponseHeadersAndBody(HttpResponseMessage sourceResponse, HttpResponse destinationResponse)
        {
            if (sourceResponse == null) 
                return;

            destinationResponse.ContentType = sourceResponse.Content?.Headers?.ContentType?.ToString();
            var headersToExclude = new HashSet<string>(StringComparer.OrdinalIgnoreCase) 
                { "Transfer-Encoding", "Server", "X-Powered-By" };

            // Copy response headers
            if (sourceResponse.Headers != null)
            {
                foreach (var header in sourceResponse.Headers.Where(h => !headersToExclude.Contains(h.Key)))
                {
                    destinationResponse.Headers.Add(header.Key, string.Join(",", header.Value));
                }
            }

            // Copy content headers
            if (sourceResponse.Content?.Headers != null)
            {
                foreach (var header in sourceResponse.Content.Headers.Where(h => !headersToExclude.Contains(h.Key)))
                {
                    destinationResponse.Headers.Add(header.Key, string.Join(",", header.Value));
                }
            }

            // Copy response body
            if (sourceResponse.Content != null)
            {
                try
                {
                    byte[] responseBody = sourceResponse.Content.ReadAsByteArrayAsync()
                        .ConfigureAwait(false).GetAwaiter().GetResult();
                        
                    if (responseBody.Length > 0)
                    {
                        destinationResponse.OutputStream.Write(responseBody, 0, responseBody.Length);
                    }
                }
                catch (Exception ex)
                {
                    LogError($"Error copying response body: {ex.Message}");
                }
            }
        }

        private void SetErrorResponse(HttpResponse response, int statusCode, string message)
        {
            response.StatusCode = statusCode;
            response.StatusDescription = message;
            response.Write(message);
        }

        private bool HasParallelTaskContext(HttpContext context)
        {
            return context.Items.Contains(CtxCtsKey) && 
                   context.Items.Contains(CtxParallelTaskKey) && 
                   context.Items.Contains(CtxResponseFilterKey);
        }

        private void CompareResponses(byte[] originalResponseBytes, string newResponseString)
        {
            string originalResponseString = null;
            
            if (originalResponseBytes != null && originalResponseBytes.Length > 0)
            {
                try
                {
                    originalResponseString = Encoding.UTF8.GetString(originalResponseBytes);
                }
                catch (Exception ex)
                {
                    LogError($"Failed to decode original response: {ex.Message}");
                    return;
                }
            }

            LogInfo($"Comparing responses: Original({originalResponseBytes?.Length ?? 0} bytes), New({newResponseString?.Length ?? 0} bytes)");

            if (originalResponseString == null && newResponseString == null)
            {
                LogInfo("Both responses are null/empty.");
                return;
            }
            
            if (originalResponseString == null || newResponseString == null)
            {
                LogInfo("One response is null/empty, the other is not.");
                return;
            }

            LogInfo(originalResponseString == newResponseString ? 
                    "Responses match." : 
                    "Responses DO NOT match.");
        }

        private void LogError(string message) => System.Diagnostics.Trace.TraceError($"RequestRoutingModule: {message}");
        private void LogInfo(string message) => System.Diagnostics.Trace.TraceInformation($"RequestRoutingModule: {message}");
        
        #endregion

        #region Supporting Types
        
        /// <summary>
        /// Contains the routing configuration.
        /// </summary>
        private class RoutingConfig
        {
            public string NewServiceUrl { get; set; }
            public RoutingMode GetMode { get; set; }
            public Dictionary<string, RoutingMode> PostModes { get; set; } = new Dictionary<string, RoutingMode>();
            public bool IsValid { get; set; }
        }
        
        #endregion
    }
}