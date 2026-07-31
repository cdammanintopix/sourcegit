using System.Collections.Generic;

using Avalonia.Collections;

using CommunityToolkit.Mvvm.ComponentModel;

namespace SourceGit.ViewModels
{
    public class DependencyTreeNode : ObservableObject
    {
        public string FullPath { get; private set; } = string.Empty;
        public int Depth { get; private set; } = 0;
        public Models.Dependency Dependency { get; private set; } = null;
        public List<DependencyTreeNode> Children { get; private set; } = [];
        public int Counter = 0;

        public bool IsFolder
        {
            get => Dependency == null;
        }

        public bool IsExpanded
        {
            get => _isExpanded;
            set => SetProperty(ref _isExpanded, value);
        }

        public string ChildCounter
        {
            get => Counter > 0 ? $"({Counter})" : string.Empty;
        }

        public bool IsDirty
        {
            get => Dependency?.IsDirty ?? false;
        }

        public DependencyTreeNode(Models.Dependency dep, int depth)
        {
            FullPath = dep.Name;
            Depth = depth;
            Dependency = dep;
            IsExpanded = false;
        }

        public DependencyTreeNode(string path, int depth, bool isExpanded)
        {
            FullPath = path;
            Depth = depth;
            IsExpanded = isExpanded;
            Counter = 1;
        }

        public static List<DependencyTreeNode> Build(IList<Models.Dependency> deps, HashSet<string> expanded)
        {
            var nodes = new List<DependencyTreeNode>();
            var folders = new Dictionary<string, DependencyTreeNode>();

            foreach (var dep in deps)
            {
                var sepIdx = dep.Name.IndexOf('/');
                if (sepIdx == -1)
                {
                    nodes.Add(new DependencyTreeNode(dep, 0));
                }
                else
                {
                    DependencyTreeNode lastFolder = null;
                    int depth = 0;

                    while (sepIdx != -1)
                    {
                        var folder = dep.Name.Substring(0, sepIdx);
                        if (folders.TryGetValue(folder, out var value))
                        {
                            lastFolder = value;
                            lastFolder.Counter++;
                        }
                        else if (lastFolder == null)
                        {
                            lastFolder = new DependencyTreeNode(folder, depth, expanded.Contains(folder));
                            folders.Add(folder, lastFolder);
                            InsertFolder(nodes, lastFolder);
                        }
                        else
                        {
                            var cur = new DependencyTreeNode(folder, depth, expanded.Contains(folder));
                            folders.Add(folder, cur);
                            InsertFolder(lastFolder.Children, cur);
                            lastFolder = cur;
                        }

                        depth++;
                        sepIdx = dep.Name.IndexOf('/', sepIdx + 1);
                    }

                    lastFolder?.Children.Add(new DependencyTreeNode(dep, depth));
                }
            }

            folders.Clear();
            return nodes;
        }

        private static void InsertFolder(List<DependencyTreeNode> collection, DependencyTreeNode subFolder)
        {
            for (int i = 0; i < collection.Count; i++)
            {
                if (!collection[i].IsFolder)
                {
                    collection.Insert(i, subFolder);
                    return;
                }
            }

            collection.Add(subFolder);
        }

        private bool _isExpanded = false;
    }

    public class DependencyCollectionAsTree
    {
        public List<DependencyTreeNode> Tree
        {
            get;
            set;
        } = [];

        public AvaloniaList<DependencyTreeNode> Rows
        {
            get;
            set;
        } = [];

        public static DependencyCollectionAsTree Build(List<Models.Dependency> deps, DependencyCollectionAsTree old)
        {
            var oldExpanded = new HashSet<string>();
            if (old != null)
            {
                foreach (var row in old.Rows)
                {
                    if (row.IsFolder && row.IsExpanded)
                        oldExpanded.Add(row.FullPath);
                }
            }

            var collection = new DependencyCollectionAsTree();
            collection.Tree = DependencyTreeNode.Build(deps, oldExpanded);

            var rows = new List<DependencyTreeNode>();
            MakeTreeRows(rows, collection.Tree);
            collection.Rows.AddRange(rows);

            return collection;
        }

        public void ToggleExpand(DependencyTreeNode node)
        {
            node.IsExpanded = !node.IsExpanded;

            var rows = Rows;
            var depth = node.Depth;
            var idx = rows.IndexOf(node);
            if (idx == -1)
                return;

            if (node.IsExpanded)
            {
                var subrows = new List<DependencyTreeNode>();
                MakeTreeRows(subrows, node.Children);
                rows.InsertRange(idx + 1, subrows);
            }
            else
            {
                var removeCount = 0;
                for (int i = idx + 1; i < rows.Count; i++)
                {
                    var row = rows[i];
                    if (row.Depth <= depth)
                        break;

                    removeCount++;
                }
                rows.RemoveRange(idx + 1, removeCount);
            }
        }

        private static void MakeTreeRows(List<DependencyTreeNode> rows, List<DependencyTreeNode> nodes)
        {
            foreach (var node in nodes)
            {
                rows.Add(node);

                if (!node.IsExpanded || !node.IsFolder)
                    continue;

                MakeTreeRows(rows, node.Children);
            }
        }
    }

    public class DependencyCollectionAsList
    {
        public List<Models.Dependency> Dependencies
        {
            get;
            set;
        } = [];
    }
}
