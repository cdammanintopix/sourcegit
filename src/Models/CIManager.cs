using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Avalonia.Threading;

namespace SourceGit.Models
{
    public interface ICIHost
    {
        void OnCIResourceChanged(string req, string status);
    }

    public partial class CIManager
    {
        public static CIManager Instance
        {
            get
            {
                return _instance ??= new CIManager();
            }
        }

        private static string _GITLAB_TOKEN = null;
        public static string GITLAB_TOKEN
        {
            get
            {
                if (string.IsNullOrEmpty(_GITLAB_TOKEN)) {
                    string token = Environment.GetEnvironmentVariable("SOURCEGIT_GITLAB_TOKEN");
                    if (!string.IsNullOrEmpty(token)) {
                        try {
                            using var client = new HttpClient();
                            client.DefaultRequestHeaders.Add("PRIVATE-TOKEN", token);
                            client.Timeout = TimeSpan.FromSeconds(2);
                            var rsp = client.GetAsync("https://gitlab.intopix.com/api/v4/user").Result;
                            if (rsp.IsSuccessStatusCode) {
                                _GITLAB_TOKEN = token;
                            }
                        } catch { }
                    }
                    if (string.IsNullOrEmpty(_GITLAB_TOKEN)) {
                        _GITLAB_TOKEN = "glpat-apjxosLzdl36O1qdCtVyrW86MQp1OmkH.01.0w03wsnf8";
                    }
                }
                return _GITLAB_TOKEN;
            }
        }

        private static CIManager _instance = null;

        private readonly Lock _synclock = new();
        private List<ICIHost> _CIs = [];
        private Dictionary<string, KeyValuePair<string, bool>> _resources = [];
        private HashSet<string> _requesting = [];

        [GeneratedRegex(@"^\[\{""id"":(\d+).*""status"":""([a-z]+)")]
        private static partial Regex REG_PIPELINE();

        private static bool StatusNeedsRefresh(string status)
        {
            return new List<string> { 
                "created",
                "pending",
                "canceling",
                "running"
            }.Contains(status);
        }

        public void Start()
        {
            Task.Run(async () =>
            {
                var lastRefresh = DateTime.Now;
                while (true)
                {
                    string req = null;

                    lock (_synclock)
                    {
                        if (DateTime.Now - lastRefresh >= TimeSpan.FromSeconds(5))
                        {
                            _resources.Where(res => res.Value.Value).ToList().ForEach(res => _requesting.Add(res.Key));
                        }
                        foreach (var one in _requesting)
                        {
                            req = one;
                            break;
                        }
                    }

                    if (req == null)
                    {
                        Thread.Sleep(100);
                        continue;
                    }

                    string route = req[..req.LastIndexOf("/")];
                    string sha = req[(req.LastIndexOf("/") + 1)..];

                    (bool failed, bool found, int id, string status) = await GetPipeline(route, sha);

                    bool needsRefresh = _resources.TryGetValue(req, out var value) ? value.Value : false;
                    if (found)
                    {
                        needsRefresh = StatusNeedsRefresh(status);
                    }

                    lock (_synclock)
                    {
                        _requesting.Remove(req);
                    }

                    lastRefresh = DateTime.Now;

                    Dispatcher.UIThread.Post(() =>
                    {
                        if (!failed)
                        {
                            _resources[req] = new KeyValuePair<string, bool>(status, needsRefresh);
                            NotifyResourceChanged(req, status);
                        }
                    });
                }

                // ReSharper disable once FunctionNeverReturns
            });
        }

        public async Task<(bool, bool, int, string)> GetPipeline(string route, string sha)
        {
            bool failed = false;
            bool found = false;
            int id = 0;
            string status = null;
            try
            {
                Dns.GetHostEntry("gitlab.intopix.com"); // This raise an early exception if not connected to the VPN

                using var client = new HttpClient();
                client.DefaultRequestHeaders.Add("PRIVATE-TOKEN", GITLAB_TOKEN);
                client.Timeout = TimeSpan.FromSeconds(2);
                var rsp = await client.GetAsync($"https://gitlab.intopix.com/api/v4/projects/{HttpUtility.UrlEncode(route)}/pipelines?sha={sha}");
                if (rsp.IsSuccessStatusCode)
                {
                    var pipelines = await rsp.Content.ReadAsStringAsync();
                    int firstEnd = pipelines.IndexOf("},{");
                    if (firstEnd > 0)
                    {
                        pipelines = pipelines[..firstEnd];
                    }
                    var matchPipeline = REG_PIPELINE().Match(pipelines);
                    if (matchPipeline.Success)
                    {
                        id = Int32.Parse(matchPipeline.Groups[1].Value);
                        status = matchPipeline.Groups[2].Value;
                        found = true;
                    }
                }
            }
            catch
            {
                failed = true;
            }

            return (failed, found, id, status);
        }

        public async Task<bool> CancelPipeline(string req)
        {
            bool success = false;
            string route = req[..req.LastIndexOf("/")];
            string sha = req[(req.LastIndexOf("/") + 1)..];

            (bool failed, bool found, int id, string status) = await GetPipeline(route, sha);

            if (found)
            {
                try
                {
                    using var client = new HttpClient();
                    client.DefaultRequestHeaders.Add("PRIVATE-TOKEN", GITLAB_TOKEN);
                    client.Timeout = TimeSpan.FromSeconds(2);
                    var rsp = await client.PostAsync($"https://gitlab.intopix.com/api/v4/projects/{HttpUtility.UrlEncode(route)}/pipelines/{id}/cancel", null);
                    success = rsp.IsSuccessStatusCode;
                }
                catch
                {
                    success = false;
                }
            }

            if (success)
            {
                Request(req, true);
            }

            return success;
        }

        public void Subscribe(ICIHost host)
        {
            _CIs.Add(host);
        }

        public void Unsubscribe(ICIHost host)
        {
            _CIs.Remove(host);
        }

        public void QueueForNextRefresh(string req)
        {
            _resources[req] = new KeyValuePair<string, bool>(null, true);
        }

        public string Request(string req, bool forceRefetch)
        {
            string status = null;

            if (forceRefetch)
            {
                _resources.Remove(req);
                NotifyResourceChanged(req, null);
            }
            else
            {
                if (_resources.TryGetValue(req, out var value))
                {
                    status = value.Key;
                    if (!value.Value)
                    {
                        return status;
                    }
                }
            }

            lock (_synclock)
            {
                _requesting.Add(req);
            }

            return status;
        }

        public void Clear(string req_prefix = "")
        {
            if (!string.IsNullOrEmpty(req_prefix))
            {
                foreach (var req in _resources.Keys.Where(k => k.StartsWith(req_prefix, StringComparison.OrdinalIgnoreCase)).ToList())
                {
                    _resources.Remove(req);
                }
            }
            else
            {
                _resources.Clear();
            }
        }

        private void NotifyResourceChanged(string req, string status)
        {
            foreach (var ci in _CIs)
                ci.OnCIResourceChanged(req, status);
        }
    }
}
