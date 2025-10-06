using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

namespace SourceGit.Models
{
    public interface ICIHost
    {
        void OnCIResourceChanged(string req, Bitmap image);
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

        private static CIManager _instance = null;

        private readonly Lock _synclock = new();
        private List<ICIHost> _CIs = new List<ICIHost>();
        private Dictionary<string, Bitmap> _resources = new Dictionary<string, Bitmap>();
        private HashSet<string> _requesting = new HashSet<string>();

        [GeneratedRegex(@"^\[\{.*""status"":""([a-z]+)")]
        private static partial Regex REG_PIPELINE();

        public void Start()
        {
            Task.Run(async () =>
            {
                while (true)
                {
                    string req = null;

                    lock (_synclock)
                    {
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

                    string route = req.Substring(0, req.LastIndexOf("/"));
                    string sha = req.Substring(req.LastIndexOf("/") + 1);
                    Bitmap img = null;
                    try
                    {
                        using var client = new HttpClient();
                        client.DefaultRequestHeaders.Add("PRIVATE-TOKEN", Environment.GetEnvironmentVariable("SOURCEGIT_GITLAB_TOKEN"));
                        client.Timeout = TimeSpan.FromSeconds(2);
                        var rsp = await client.GetAsync($"https://gitlab.intopix.com/api/v4/projects/{HttpUtility.UrlEncode(route)}/pipelines?sha={sha}");
                        if (rsp.IsSuccessStatusCode)
                        {
                            var pipelines = await rsp.Content.ReadAsStringAsync();
                            int firstEnd = pipelines.IndexOf("},{");
                            if (firstEnd > 0) {
                                pipelines = pipelines.Substring(0, firstEnd);
                            }
                            var matchPipeline = REG_PIPELINE().Match(pipelines);
                            if (matchPipeline.Success)
                            {
                                img = new Bitmap(AssetLoader.Open(new Uri($"avares://SourceGit/Resources/Images/{matchPipeline.Groups[1].Value}.png", UriKind.RelativeOrAbsolute)));
                            }
                        }
                    }
                    catch
                    {
                        // ignored
                    }

                    lock (_synclock)
                    {
                        _requesting.Remove(req);
                    }

                    Dispatcher.UIThread.Post(() =>
                    {
                        _resources[req] = img;
                        NotifyResourceChanged(req, img);
                    });
                }

                // ReSharper disable once FunctionNeverReturns
            });
        }

        public void Subscribe(ICIHost host)
        {
            _CIs.Add(host);
        }

        public void Unsubscribe(ICIHost host)
        {
            _CIs.Remove(host);
        }

        public Bitmap Request(string req, bool forceRefetch)
        {
            if (forceRefetch)
            {
                _resources.Remove(req);
                NotifyResourceChanged(req, null);
            }
            else
            {
                if (_resources.TryGetValue(req, out var value))
                    return value;
            }

            lock (_synclock)
            {
                _requesting.Add(req);
            }

            return null;
        }

        private void NotifyResourceChanged(string req, Bitmap image)
        {
            foreach (var ci in _CIs)
                ci.OnCIResourceChanged(req, image);
        }
    }
}
