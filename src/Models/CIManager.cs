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
using SourceGit.Views;

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
        private Dictionary<string, (bool, List<(int, string)>)> _resources = [];
        private HashSet<string> _requesting = [];

        [GeneratedRegex(@"\{""id"":(\d+).*""status"":""([a-z]+)")]
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

        private static bool StatusIsCancellable(string status)
        {
            return new List<string> {
                "created",
                "pending",
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
                            _resources.Where(res => res.Value.Item1).ToList().ForEach(res => _requesting.Add(res.Key));
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

                    (string route, string sha) = CI.GetRouteSha(req);

                    (bool failed, bool found, List<(int, string)> pipelines) = await GetPipelinesForSha(route, sha);
                    bool needsRefresh = _resources.TryGetValue(req, out var value) ? value.Item1 : false;

                    lock (_synclock)
                    {
                        _requesting.Remove(req);
                    }

                    lastRefresh = DateTime.Now;

                    Dispatcher.UIThread.Post(() =>
                    {
                        if (found)
                        {
                            needsRefresh = pipelines.Any(x => StatusNeedsRefresh(x.Item2));
                            _resources[req] = (needsRefresh, pipelines);
                            NotifyResourceChanged(req, pipelines[0].Item2);
                        }
                    });
                }

                // ReSharper disable once FunctionNeverReturns
            });
        }

        private static (bool, List<(int, string)>) ParsePipelinesJson(string pipelinesJson)
        {
            bool found = false;
            List<(int, string)> pipelines = [];
            for (int nextBegin = 0; pipelinesJson.Length > 0; pipelinesJson = pipelinesJson[nextBegin..])
            {
                int firstEnd = pipelinesJson.IndexOf("},{");
                nextBegin = firstEnd + 2;
                if (firstEnd < 0)
                {
                    firstEnd = nextBegin = pipelinesJson.Length;
                }

                var matchPipeline = REG_PIPELINE().Match(pipelinesJson[..firstEnd]);
                if (matchPipeline.Success)
                {
                    int id = Int32.Parse(matchPipeline.Groups[1].Value);
                    string status = matchPipeline.Groups[2].Value;
                    pipelines.Add((id, status));
                    found = true;
                }
            }
            return (found, pipelines);
        }

        public static async Task<(bool, bool, List<(int, string)>)> GetPipelinesForSha(string route, string sha)
        {
            bool failed = false;
            bool found = false;
            List<(int, string)> pipelines = [];
            try
            {
                Dns.GetHostEntry("gitlab.intopix.com"); // This raise an early exception if not connected to the VPN

                using var client = new HttpClient();
                client.DefaultRequestHeaders.Add("PRIVATE-TOKEN", GITLAB_TOKEN);
                client.Timeout = TimeSpan.FromSeconds(2);
                var rsp = await client.GetAsync($"https://gitlab.intopix.com/api/v4/projects/{HttpUtility.UrlEncode(route)}/pipelines?sha={sha}");
                if (rsp.IsSuccessStatusCode)
                {
                    (found, pipelines) = ParsePipelinesJson(await rsp.Content.ReadAsStringAsync());
                }
            }
            catch
            {
                failed = true;
            }

            return (failed, found, pipelines);
        }

        public static async Task<(bool, bool, List<(int, string)>)> GetPipelinesForBranch(string route, string branch)
        {
            bool failed = false;
            bool found = false;
            List<(int, string)> pipelines = [];
            try
            {
                Dns.GetHostEntry("gitlab.intopix.com"); // This raise an early exception if not connected to the VPN

                using var client = new HttpClient();
                client.DefaultRequestHeaders.Add("PRIVATE-TOKEN", GITLAB_TOKEN);
                client.Timeout = TimeSpan.FromSeconds(2);
                var rsp = await client.GetAsync($"https://gitlab.intopix.com/api/v4/projects/{HttpUtility.UrlEncode(route)}/pipelines?ref={HttpUtility.UrlEncode(branch)}&status=created,waiting_for_resource,preparing,pending,running");
                if (rsp.IsSuccessStatusCode)
                {
                    (found, pipelines) = ParsePipelinesJson(await rsp.Content.ReadAsStringAsync());
                }
            }
            catch
            {
                failed = true;
            }

            return (failed, found, pipelines);
        }

        public async Task<bool> CancelPipelinesForSha(string route, string sha)
        {
            string req = CI.GetReq(route, sha);
            List<(int, string)> pipelines = [];
            if (_resources.TryGetValue(req, out var value))
            {
                pipelines = value.Item2;
            }
            else
            {
                (_, _, pipelines) = await GetPipelinesForSha(route, sha);
            }

            if (pipelines.Count == 0)
            {
                return false;
            }

            bool updateNeeded = false;
            foreach ((int id, string status) in pipelines)
            {
                if (!StatusIsCancellable(status))
                {
                    continue;
                }

                try {
                    using var client = new HttpClient();
                    client.DefaultRequestHeaders.Add("PRIVATE-TOKEN", GITLAB_TOKEN);
                    client.Timeout = TimeSpan.FromSeconds(2);
                    await client.PostAsync($"https://gitlab.intopix.com/api/v4/projects/{HttpUtility.UrlEncode(route)}/pipelines/{id}/cancel", null);
                    updateNeeded = true;
                } catch { }
            }

            if (updateNeeded)
            {
                Request(req, true);
            }

            return true;
        }

        public static async Task<bool> RunPipelineForBranch(string route, string branch, string ciArgs="")
        {
            if (string.IsNullOrEmpty(route) || string.IsNullOrEmpty(branch))
            {
                return false;
            }

            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.Add("PRIVATE-TOKEN", GITLAB_TOKEN);
                client.Timeout = TimeSpan.FromSeconds(2);
                var content = new
                {
                    inputs = new Dictionary<string, object> {
                        ["ci-args"] = ciArgs
                    }
                };
                var rsp = await client.PostAsync($"https://gitlab.intopix.com/api/v4/projects/{HttpUtility.UrlEncode(route)}/pipeline?ref={HttpUtility.UrlEncode(branch)}", null);
                return rsp.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
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
            _resources[req] = (true, []);
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
                if (_resources.TryGetValue(req, out var value) && value.Item2.Count > 0)
                {
                    status = value.Item2[0].Item2; // status of the last pipeline (first in list)
                    if (!value.Item1) // !needsRefresh
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
