﻿using System;
using System.Collections.Generic;
using Common.Logging;
using Common.Logging.Simple;
using Illallangi.Extensions;
using Newtonsoft.Json;

namespace Illallangi
{
    using System.Linq;
    using System.Net;

    public class RestClient : IRestClient
    {
        
        #region Fields

        private readonly string currentBaseUrl;

        private readonly IEnumerable<KeyValuePair<string, string>> currentDefaultParameters;
        
        private readonly ILog currentLog;

        private readonly IRestCache currentRestCache;

        private Uri currentBaseUri;

        private RestSharp.IRestClient currentRestSharpClient;

        private readonly JsonSerializerSettings currentJsonSerializerSettings;

        #endregion

        #region Constructor

        public RestClient(
            string baseUrl, 
            IEnumerable<KeyValuePair<string, string>> defaultParameters = null, 
            IRestCache restCache = null, 
            ILog log = null, 
            JsonSerializerSettings jsonSerializerSettings = null)
        {
            this.currentBaseUrl = baseUrl;
            this.currentDefaultParameters = defaultParameters;
            this.currentRestCache = restCache;
            this.currentLog = log ?? new NoOpLogger();
            this.currentJsonSerializerSettings = jsonSerializerSettings ?? new JsonSerializerSettings();

            this.Log.DebugFormat(
                    @"RestClient(baseUrl=""{0}"", defaultParameters=""{1}"", log = ""{2}"")",
                    this.BaseUrl,
                    this.DefaultParameters,
                    this.Log);
        }

        #endregion

        #region Properties

        private string BaseUrl
        {
            get
            {
                return this.currentBaseUrl;
            }
        }

        private IEnumerable<KeyValuePair<string, string>> DefaultParameters
        {
            get
            {
                return this.currentDefaultParameters;
            }
        }

        private IRestCache RestCache
        {
            get
            {
                return this.currentRestCache;
            }
        }

        private ILog Log
        {
            get
            {
                return this.currentLog;
            }
        }

        private Uri BaseUri
        {
            get
            {
                return this.currentBaseUri ?? (this.currentBaseUri = new Uri(this.BaseUrl));
            }
        }

        private RestSharp.IRestClient RestSharpClient
        {
            get
            {
                return this.currentRestSharpClient ?? (this.currentRestSharpClient = new RestSharp.RestClient(this.BaseUrl));
            }
        }

        private JsonSerializerSettings JsonSerializerSettings
        {
            get
            {
                return this.currentJsonSerializerSettings;
            }
        }

        #endregion

        #region Methods

        public string GetContent(string uri, IEnumerable<KeyValuePair<string, string>> parameters = null, CacheMode cacheMode = CacheMode.Enabled)
        {
            this.Log.DebugFormat(
                    @"RestClient.GetContent(uri=""{0}"", parameters=""{1}"")",
                    uri,
                    parameters);
            
            return this.GetContent(uri.TemplateWith(parameters, this.DefaultParameters), cacheMode);
        }

        public string GetContent(Uri uri, CacheMode cacheMode = CacheMode.Enabled)
        {
            this.Log.DebugFormat(
                    @"RestClient.GetContent(uri=""{0}"")",
                    uri);

            return this.Execute(uri, cacheMode);
        }

        public T GetObject<T>(string uri, IEnumerable<KeyValuePair<string, string>> parameters = null, CacheMode cacheMode = CacheMode.Enabled) where T : new()
        {
            this.Log.DebugFormat(
                    @"RestClient.GetObject<T>(uri=""{0}"", parameters=""{1}"")",
                    uri,
                    parameters); 

            return this.GetObject<T>(uri.TemplateWith(parameters, this.DefaultParameters), cacheMode);
        }

        public T GetObject<T>(Uri uri, CacheMode cacheMode = CacheMode.Enabled) where T : new()
        {
            this.Log.DebugFormat(
                    @"RestClient.GetObject<T>(uri=""{0}"")",
                    uri);
            
            return JsonConvert.DeserializeObject<T>(this.Execute(uri, cacheMode), this.JsonSerializerSettings);
        }

        private string Execute(Uri uri, CacheMode cacheMode = CacheMode.Enabled)
        {
            this.Log.DebugFormat(@"RestClient.Execute(uri=""{0}"")", uri);

            // Create Request
            var resource = uri.IsAbsoluteUri ? this.BaseUri.MakeRelativeUri(uri) : uri;
            this.Log.DebugFormat(@"Creating request for ""{0}""", resource);
            var request = new RestSharp.RestRequest(resource, RestSharp.Method.GET);

            // Checking Cache for hit
            this.Log.DebugFormat(@"Checking cache for ""{0}""", resource.ToString());
            var cache = (null == this.RestCache) ? 
                        null :
                        this.RestCache.Retrieve(this.BaseUrl, resource.ToString());

            if (null != cache && (CacheMode.ReturnOnHit == cacheMode || CacheMode.ForceCache == cacheMode))
            {
                this.Log.DebugFormat(@"Cache Hit and ReturnOnHit or ForceCache mode - returning cached value");
                return cache.Content;
            }

            // Cache Hit - add If-None-Match header to request
            if (null != cache && CacheMode.ForceCache.ToString() != cache.ETag)
            {
                this.Log.DebugFormat(@"Cache Hit - adding If-None-Match {0}", cache.ETag);
                request.AddHeader("If-None-Match", cache.ETag);
            }

            // Execute Request
            this.Log.DebugFormat(@"Executing request");
            var restResponse = this.RestSharpClient.Execute(request);

            // Cache Hit and Not Modified response - return cached value
            if (null != cache && HttpStatusCode.NotModified == restResponse.StatusCode)
            {
                this.Log.DebugFormat(@"Not modified - returning cached value");
                return cache.Content;
            }

            // Delete stale cache
            if (null != cache)
            {
                this.Log.DebugFormat(@"Cache is stale - deleting entry");
                this.RestCache.Delete(cache);
            }

            if (null != this.RestCache)
            { 
                // Add cache entry
                var header = restResponse.Headers.SingleOrDefault(response => response.Name.Equals("etag", StringComparison.InvariantCultureIgnoreCase));
                if (null != header)
                {
                    this.Log.DebugFormat(@"Adding result to cache with etag of {0}", header.Value.ToString());
                    this.RestCache.Create(this.BaseUrl, resource.ToString(), header.Value.ToString(), restResponse.Content);
                }
                else if (CacheMode.ForceCache == cacheMode)
                {
                    this.Log.DebugFormat(@"Adding result to cache with etag of {0}", CacheMode.ForceCache.ToString());
                    this.RestCache.Create(this.BaseUrl, resource.ToString(), CacheMode.ForceCache.ToString(), restResponse.Content);
                }
            }

            // Return
            return restResponse.Content;
        }

        #endregion
    }
}