using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;

namespace IMLCGui
{
    namespace GitHub
    {
        internal class GitHubClient
        {
            private HttpClient _httpClient;

            public GitHubClient(string clientName, string clientVersion)
            {
                this._httpClient = new HttpClient();
                this._httpClient.BaseAddress = new Uri("https://api.github.com");

                ProductInfoHeaderValue userAgent = new ProductInfoHeaderValue(clientName, clientVersion);
                this._httpClient.DefaultRequestHeaders.UserAgent.Add(userAgent);
            }

            public void SetRequestTimeout(TimeSpan timeout)
            {
                this._httpClient.Timeout = timeout;
            }

            public async Task<List<Release>> GetReleases(string author, string repositoryName)
            {
                string responseString;
                using (HttpResponseMessage apiResponse = await this._httpClient.GetAsync($"/repos/{author}/{repositoryName}/releases"))
                {
                    apiResponse.EnsureSuccessStatusCode();
                    responseString = await apiResponse.Content.ReadAsStringAsync();
                }
                return JsonConvert.DeserializeObject<List<Release>>(responseString);
            }
        }

        internal class Release
        {
            public string name;
            public string tag_name;
            public bool prerelease = false;
            public List<Asset> assets = null;

            public override string ToString()
            {
                return "{" +
                    "\"name\": \"" + name + "\", " +
                    "\"tag_name\": \"" + tag_name + "\", " +
                    "\"prerelease\": " + prerelease + "\", " +
                    "\"assets\": " + (assets != null ? "[" + String.Join(",\n", assets) + "]" : "null") +
                    "}";
            }

            internal class Asset
            {
                public string name;
                public string browser_download_url;

                public override string ToString()
                {
                    return "{" +
                        "\"name\": \"" + name + "\", " +
                        "\"browser_download_url\": \"" + browser_download_url + "\"" +
                        "}";
                }
            }
        }
    }
}
