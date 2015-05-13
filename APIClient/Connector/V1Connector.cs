﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using System.Xml.Linq;
using log4net;
using VersionOne.SDK.APIClient.Extensions;
using VersionOne.SDK.APIClient.Model.Interfaces;

namespace VersionOne.SDK.APIClient
{
    /// <summary>
    /// Used to establish a connection to a VersionOne instance.
    /// </summary>
    public class V1Connector
    {
        private const string META_API_ENDPOINT = "meta.v1/";
        private const string DATA_API_ENDPOINT = "rest-1.v1/Data/";
        private const string HISTORY_API_ENDPOINT = "rest-1.v1/Hist/";
        private const string NEW_API_ENDPOINT = "rest-1.v1/New";
        private const string QUERY_API_ENDPOINT = "query.v1/";
        private const string LOC_API_ENDPOINT = "loc.v1/";
        private const string LOC2_API_ENDPOINT = "loc-2.v1/";
        private const string CONFIG_API_ENDPOINT = "config.v1/";

        private readonly HttpClient _client;
        private readonly HttpClientHandler _handler;
        private readonly Dictionary<string, MemoryStream> _pendingStreams = new Dictionary<string, MemoryStream>();
        private readonly ILog _log = LogManager.GetLogger(typeof(V1Connector));
        private string _endpoint;
        private string _upstreamUserAgent;
        private bool _isRequestConfigured = false;

	    private Uri _baseAddress;

        private V1Connector(string instanceUrl)
        {
            if (string.IsNullOrWhiteSpace(instanceUrl))
                throw new ArgumentNullException("instanceUrl");
            if (!instanceUrl.EndsWith("/"))
                instanceUrl += "/";

			if (Uri.TryCreate(instanceUrl, UriKind.Absolute, out _baseAddress))
            {
                _handler = new HttpClientHandler();
				_client = new HttpClient(_handler) { BaseAddress = _baseAddress };
                _upstreamUserAgent = FormatAssemblyUserAgent(Assembly.GetEntryAssembly());
            }
            else
                throw new ConnectionException("Instance url is not valid.");
        }

        /// <summary>
        /// Required method for setting the URL of the VersionOne instance.
        /// </summary>
        /// <param name="versionOneInstanceUrl">The URL to the VersionOne instance. Format is "http(s)://server/instance".</param>
        /// <returns>ICanSetUserAgentHeader</returns>
        public static ICanSetUserAgentHeader WithInstanceUrl(string versionOneInstanceUrl)
        {
            return new Builder(versionOneInstanceUrl);
        }

        internal Stream BeginRequest(string apipath)
        {
            var stream = new MemoryStream();
            _pendingStreams[apipath] = stream;
            
            return stream;
        }

        internal Stream EndRequest(string apipath, string contentType)
        {
            var inputstream = _pendingStreams[apipath];
            _pendingStreams.Remove(apipath);
            var body = inputstream.ToArray();

            return SendData(apipath, body, contentType);
        }

        internal Stream GetData(string resource = null)
        {
            ConfigureRequestIfNeeded();
            var resourceUrl = GetResourceUrl(resource);
            var response = _client.GetAsync(resourceUrl).Result;
            ThrowWebExceptionIfNeeded(response);
            var result = response.Content.ReadAsStreamAsync().Result;
            LogResponse(response, result.ToString());

            return result;
        }

		public async Task<XDocument> Post(IVersionOneAsset asset, XDocument postPayload)
		{
			UseDataApi();

			using (var client = HttpInstance)
			{
				var endPoint = GetResourceUrl(asset.AssetType);
				if (!string.IsNullOrWhiteSpace(asset.ID))
					endPoint += "/" + asset.ID;

				var response = await client.PostAsync(endPoint, new StringContent(postPayload.ToString()));
				var value = await response.Content.ReadAsStringAsync();
				return XDocument.Parse(value);
			}

		}

	    private HttpClient HttpInstance
	    {
			get { return new HttpClient(_handler) { BaseAddress = _baseAddress }; }
	    }

		public async Task<List<T>> Query<T>(string asset, string[] properties, string[] wheres, Func<XElement, T> returnObject)
		{
			var result = new List<T>();
			UseDataApi();

			using (var client = HttpInstance)
			{
				var whereClause = string.Join(";", wheres);

				var endpoint = GetResourceUrl(asset) + "?sel=" + string.Join(",", properties) + "&" + whereClause;

				var xml = await client.GetStringAsync(endpoint);
				var doc = XDocument.Parse(xml);
				if (doc.HasAssets())
					result = doc.Root.Elements("Asset").ToList().Select(returnObject.Invoke).ToList();
			}

			return result;
		}

		public async Task<List<T>> Query<T>(string asset, string[] properties, Func<XElement, T> returnObject)
		{
			var result = new List<T>();
			UseDataApi();
			using (var client = HttpInstance)
			{
				var endpoint = GetResourceUrl(asset) + "?sel=" + string.Join(",", properties);

				var xml = await client.GetStringAsync(endpoint);
				var doc = XDocument.Parse(xml);
				if (doc.HasAssets())
					result = doc.Root.Elements("Asset").ToList().Select(returnObject.Invoke).ToList();
			}

			return result;
		}


        internal Stream SendData(string resource = null, object data = null, string contentType = "application/xml")
        {
            var response = Post(resource, data, contentType);
            ThrowWebExceptionIfNeeded(response);
            var result = response.Content.ReadAsStreamAsync().Result;
            LogResponse(response, result.ToString(), data != null ? data.ToString() : string.Empty);

            return result;
        }
        
        internal string StringSendData(string resource = null, object data = null, string contentType = "application/xml")
        {
            var response = Post(resource, data, contentType);
            var result = response.Content.ReadAsStringAsync().Result;
            LogResponse(response, result, data != null ? data.ToString() : string.Empty);

            return result;
        }

        internal void UseDataApi()
        {
            _endpoint = DATA_API_ENDPOINT;
        }

        internal void UseHistoryApi()
        {
            _endpoint = HISTORY_API_ENDPOINT;
        }

        internal void UseNewApi()
        {
            _endpoint = NEW_API_ENDPOINT;
        }

        internal void UseMetaApi()
        {
            _endpoint = META_API_ENDPOINT;
        }

        internal void UseQueryApi()
        {
            _endpoint = QUERY_API_ENDPOINT;
        }

        internal void UseLoc2Api()
        {
            _endpoint = LOC2_API_ENDPOINT;
        }

        internal void UseLocApi()
        {
            _endpoint = LOC_API_ENDPOINT;
        }

        internal void UseConfigApi()
        {
            _endpoint = CONFIG_API_ENDPOINT;
        }

        internal void SetUpstreamUserAgent(string userAgent)
        {
            _upstreamUserAgent = userAgent;
        }

        private HttpResponseMessage Post(string resource = null, object data = null, string contentType = "application/xml")
        {
            ConfigureRequestIfNeeded();
            _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(contentType));
            string stringData = data != null ? data.ToString() : string.Empty;
            HttpContent content;
            if (data is byte[])
            {
                content = new ByteArrayContent((byte[])data);
            }
            else
            {
                content = new StringContent(stringData);
            }
            var resourceUrl = GetResourceUrl(resource);
            var response = _client.PostAsync(resourceUrl, content).Result;
            ThrowWebExceptionIfNeeded(response);
            
            return response;
        }

        private string GetResourceUrl(string resource)
        {
            if (string.IsNullOrWhiteSpace(_endpoint))
                throw new ConnectionException("V1Connector is not properly configured. The API endpoint was not specified.");

            return _endpoint + ValidateResource(resource);
        }

        private void ThrowWebExceptionIfNeeded(HttpResponseMessage response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var statusCode = Convert.ToInt32(response.StatusCode);
                var message = string.Format("The remote server returned an error: ({0}) {1}.", statusCode, HttpWorkerRequest.GetStatusDescription(statusCode));
                var webException = new WebException(message, (WebExceptionStatus) statusCode);
                throw webException;
            }
        }

        private string ValidateResource(string resource)
        {
            var result = string.Empty;
            if (resource != null && !resource.StartsWith("/"))
            {
                result = "/" + resource;
            }

            return result;
        }

        private void ConfigureRequestIfNeeded()
        {
            if (!_isRequestConfigured)
            {
                _handler.PreAuthenticate = true;
                _handler.AllowAutoRedirect = true;
                _client.DefaultRequestHeaders.Add("Accept-Language", CultureInfo.CurrentCulture.Name);
                _client.DefaultRequestHeaders.Add("User-Agent", UserAgent);
                _isRequestConfigured = true;
            }
        }

        private string UserAgent
        {
            get
            {
                var assembly = Assembly.GetAssembly(typeof(V1Connector));

                return FormatAssemblyUserAgent(assembly, _upstreamUserAgent);
            }
        }

        private string FormatAssemblyUserAgent(Assembly a, string upstream = null)
        {
            if (a == null) return null;
            var n = a.GetName();
            var s = String.Format("{0}/{1} ({2})", n.Name, n.Version, n.FullName);
            if (!String.IsNullOrEmpty(upstream))
                s = s + " " + upstream;
            return s;
        }
        
        private void LogRequest(HttpRequestMessage rm, string requestBody)
        {
            var stringBuilder = new StringBuilder();
            stringBuilder.AppendLine("REQUEST");
            stringBuilder.AppendLine("\tMethod: " + rm.Method);
            stringBuilder.AppendLine("\tRequest URL: " + rm.RequestUri);
            stringBuilder.AppendLine("\tHeaders: ");
            foreach (var header in rm.Headers)
            {
                stringBuilder.AppendLine("\t\t" + header.Key + "=" + header.Value);
            }
            stringBuilder.AppendLine("\tBody: ");
            stringBuilder.AppendLine("\t\t" + requestBody);

            _log.Info(stringBuilder.ToString());
        }

        private void LogResponse(HttpResponseMessage resp, string responseBody, string requestBody = "")
        {
            LogRequest(resp.RequestMessage, requestBody);
            var stringBuilder = new StringBuilder();
            stringBuilder.AppendLine("RESPONSE");
            stringBuilder.AppendLine("\tStatus code: " + resp.StatusCode);
            stringBuilder.AppendLine("\tHeaders: ");
            foreach (var header in resp.Headers)
            {
                stringBuilder.AppendLine("\t\t" + header.Key + "=" + header.Value);
            }
            stringBuilder.AppendLine("\tBody: ");
            stringBuilder.AppendLine("\t\t" + responseBody);

            _log.Info(stringBuilder.ToString());
        }

        #region Fluent Builder

        private class Builder : ICanSetUserAgentHeader, ICanSetAuthMethod, ICanSetProxyOrEndpointOrGetConnector, ICanSetEndpointOrGetConnector, ICanSetProxyOrGetConnector
        {
            private readonly V1Connector _instance;

            public Builder(string versionOneInstanceUrl)
            {
                _instance = new V1Connector(versionOneInstanceUrl);
            }

            public ICanSetAuthMethod WithUserAgentHeader(string name, string version)
            {
                if (string.IsNullOrWhiteSpace(name))
                    throw new ArgumentNullException("name");
                if (string.IsNullOrWhiteSpace(version))
                    throw new ArgumentNullException("version");

                _instance._client.DefaultRequestHeaders.Add(name, version);

                return this;
            }
            
            public ICanSetProxyOrEndpointOrGetConnector WithUsernameAndPassword(string username, string password)
            {
                if (string.IsNullOrWhiteSpace(username))
                    throw new ArgumentNullException("username");
                if (string.IsNullOrWhiteSpace(password))
                    throw new ArgumentNullException("password");

                _instance._handler.Credentials = new NetworkCredential(username, password);

                return this;
            }

            public ICanSetProxyOrEndpointOrGetConnector WithWindowsIntegrated()
            {
                var credentialCache = new CredentialCache
                {
                    {_instance._client.BaseAddress, "NTLM", CredentialCache.DefaultNetworkCredentials},
                    {_instance._client.BaseAddress, "Negotiate", CredentialCache.DefaultNetworkCredentials}
                };
                _instance._handler.Credentials = credentialCache;

                return this;
            }

            public ICanSetProxyOrEndpointOrGetConnector WithWindowsIntegrated(string fullyQualifiedDomainUsername, string password)
            {
                if (string.IsNullOrWhiteSpace(fullyQualifiedDomainUsername))
                    throw new ArgumentNullException("fullyQualifiedDomainUsername");
                if (string.IsNullOrWhiteSpace(password))
                    throw new ArgumentNullException("password");

                _instance._handler.Credentials = new NetworkCredential(fullyQualifiedDomainUsername, password);

                return this;
            }

            public ICanSetProxyOrEndpointOrGetConnector WithAccessToken(string accessToken)
            {
                if (string.IsNullOrWhiteSpace(accessToken))
                    throw new ArgumentNullException("accessToken");

                _instance._client.DefaultRequestHeaders.Add("Authorization", "Bearer " + accessToken);

                return this;
            }

            public ICanSetProxyOrEndpointOrGetConnector WithOAuth2Token(string accessToken)
            {
                if (string.IsNullOrWhiteSpace(accessToken))
                    throw new ArgumentNullException("accessToken");

                _instance._client.DefaultRequestHeaders.Add("Authorization", "Bearer " + accessToken);

                return this;
            }

            public ICanSetProxyOrGetConnector UseEndpoint(string endpoint)
            {
                if (string.IsNullOrWhiteSpace(endpoint))
                    throw new ArgumentNullException("endpoint");

                _instance._endpoint = endpoint;

                return this;
            }

            public ICanSetEndpointOrGetConnector WithProxy(ProxyProvider proxyProvider)
            {
                if (proxyProvider == null)
                    throw new ArgumentNullException("proxyProvider");

                _instance._handler.Proxy = proxyProvider.CreateWebProxy();

                return this;
            }

            public V1Connector Build()
            {
                return _instance;
            }

            ICanGetConnector ICanSetProxyOrGetConnector.WithProxy(ProxyProvider proxyProvider)
            {
                if (proxyProvider == null)
                    throw new ArgumentNullException("proxyProvider");

                _instance._handler.Proxy = proxyProvider.CreateWebProxy();

                return this;
            }

            ICanGetConnector ICanSetEndpointOrGetConnector.UseEndpoint(string endpoint)
            {
                if (string.IsNullOrWhiteSpace(endpoint))
                    throw new ArgumentNullException("endpoint");

                _instance._endpoint = endpoint;

                return this;
            }
        }

        #endregion
    }

    #region Interfaces

    public interface ICanSetUserAgentHeader
    {
        /// <summary>
        /// Required method for setting a custom user agent header for all HTTP requests made to the VersionOne API.
        /// </summary>
        /// <param name="name">The name of the application.</param>
        /// <param name="version">The version number of the application.</param>
        /// <returns></returns>
        ICanSetAuthMethod WithUserAgentHeader(string name, string version);
    }

    public interface ICanSetAuthMethod
    {
        /// <summary>
        /// Optional method for setting the username and password for authentication.
        /// </summary>
        /// <param name="username">The username of a valid VersionOne member account.</param>
        /// <param name="password">The password of a valid VersionOne member account.</param>
        /// <returns>ICanSetProxyOrEndpointOrGetConnector</returns>
        ICanSetProxyOrEndpointOrGetConnector WithUsernameAndPassword(string username, string password);

        /// <summary>
        /// Optional method for setting the Windows Integrated Authentication credentials for authentication based on the currently logged in user.
        /// </summary>
        /// <returns>ICanSetProxyOrEndpointOrGetConnector</returns>
        ICanSetProxyOrEndpointOrGetConnector WithWindowsIntegrated();

        /// <summary>
        /// Optional method for setting the Windows Integrated Authentication credentials for authentication based on specified user credentials.
        /// </summary>
        /// <param name="fullyQualifiedDomainUsername">The fully qualified domain name in form "DOMAIN\username".</param>
        /// <param name="password">The password of a valid VersionOne member account.</param>
        /// <returns>ICanSetProxyOrEndpointOrGetConnector</returns>
        ICanSetProxyOrEndpointOrGetConnector WithWindowsIntegrated(string fullyQualifiedDomainUsername, string password);

        /// <summary>
        /// Optional method for setting the access token for authentication.
        /// </summary>
        /// <param name="accessToken">The access token.</param>
        /// <returns>ICanSetProxyOrEndpointOrGetConnector</returns>
        ICanSetProxyOrEndpointOrGetConnector WithAccessToken(string accessToken);

        /// <summary>
        /// Optional method for setting the OAuth2 access token for authentication.
        /// </summary>
        /// <param name="accessToken">The OAuth2 access token.</param>
        /// <returns>ICanSetProxyOrEndpointOrGetConnector</returns>
        ICanSetProxyOrEndpointOrGetConnector WithOAuth2Token(string accessToken);
    }

    public interface ICanGetConnector
    {
        /// <summary>
        /// Required terminating method that returns the V1Connector object.
        /// </summary>
        /// <returns>V1Connector</returns>
        V1Connector Build();
    }

    public interface ICanSetProxyOrEndpointOrGetConnector : ICanSetEndpoint, ICanGetConnector
    {
        /// <summary>
        /// Optional method for setting the proxy credentials.
        /// </summary>
        /// <param name="proxyProvider">The ProxyProvider containing the proxy URI, username, and password.</param>
        /// <returns>ICanSetEndpointOrGetConnector</returns>
        ICanSetEndpointOrGetConnector WithProxy(ProxyProvider proxyProvider);
    }

    public interface ICanSetEndpointOrGetConnector : ICanGetConnector
    {
        /// <summary>
        /// Optional method for specifying an API endpoint to connect to.
        /// </summary>
        /// <param name="endpoint">The API endpoint.</param>
        /// <returns>ICanGetConnector</returns>
        ICanGetConnector UseEndpoint(string endpoint);
    }

    public interface ICanSetProxyOrGetConnector : ICanGetConnector
    {
        /// <summary>
        /// Optional method for setting the proxy credentials.
        /// </summary>
        /// <param name="proxyProvider">The ProxyProvider containing the proxy URI, username, and password.</param>
        /// <returns>ICanGetConnector</returns>
        ICanGetConnector WithProxy(ProxyProvider proxyProvider);
    }

    public interface ICanSetEndpoint
    {
        /// <summary>
        /// Optional method for specifying an API endpoint to connect to.
        /// </summary>
        /// <param name="endpoint">The API endpoint.</param>
        /// <returns>ICanSetProxyOrGetConnector</returns>
        ICanSetProxyOrGetConnector UseEndpoint(string endpoint);
    }

    #endregion
}
