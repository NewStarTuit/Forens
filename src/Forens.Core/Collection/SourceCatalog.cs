using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Forens.Core.Collection
{
    public sealed class SourceCatalog
    {
        private readonly Dictionary<string, IArtifactSource> _sources;

        private SourceCatalog(Dictionary<string, IArtifactSource> sources)
        {
            _sources = sources;
        }

        public IReadOnlyList<IArtifactSource> Sources
        {
            get { return _sources.Values.OrderBy(s => s.Metadata.Id, StringComparer.Ordinal).ToArray(); }
        }

        public bool TryGet(string id, out IArtifactSource source)
        {
            return _sources.TryGetValue(id, out source);
        }

        public IArtifactSource Get(string id)
        {
            IArtifactSource s;
            if (!_sources.TryGetValue(id, out s))
                throw new KeyNotFoundException("Unknown source id: " + id);
            return s;
        }

        public bool Contains(string id) { return _sources.ContainsKey(id); }

        public static SourceCatalog Discover()
        {
            return DiscoverFromAssemblies(new[] { Assembly.GetExecutingAssembly() }, scanProgramDir: true);
        }

        public static SourceCatalog DiscoverFromAssemblies(IEnumerable<Assembly> assemblies)
        {
            return DiscoverFromAssemblies(assemblies, scanProgramDir: false);
        }

        public static SourceCatalog DiscoverFromTypes(IEnumerable<Type> types)
        {
            if (types == null) throw new ArgumentNullException(nameof(types));
            return Build(types);
        }

        private static SourceCatalog DiscoverFromAssemblies(IEnumerable<Assembly> seedAssemblies, bool scanProgramDir)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var asms = new List<Assembly>();
            foreach (var a in seedAssemblies)
            {
                if (a != null && seen.Add(a.FullName)) asms.Add(a);
            }

            if (scanProgramDir)
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                if (!string.IsNullOrEmpty(baseDir) && Directory.Exists(baseDir))
                {
                    foreach (var dll in Directory.EnumerateFiles(baseDir, "Forens.Collectors.*.dll", SearchOption.TopDirectoryOnly))
                    {
                        try
                        {
                            var loaded = Assembly.LoadFrom(dll);
                            if (seen.Add(loaded.FullName)) asms.Add(loaded);
                        }
                        catch (Exception ex)
                        {
                            throw new InvalidOperationException(
                                "Failed to load collector assembly: " + dll, ex);
                        }
                    }
                }
            }

            var allTypes = new List<Type>();
            foreach (var asm in asms)
            {
                Type[] types;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null).ToArray(); }
                allTypes.AddRange(types);
            }
            return Build(allTypes);
        }

        private static SourceCatalog Build(IEnumerable<Type> candidateTypes)
        {
            var byId = new Dictionary<string, IArtifactSource>(StringComparer.Ordinal);
            var conflicts = new Dictionary<string, List<string>>();

            foreach (var t in candidateTypes)
            {
                if (t == null) continue;
                if (t.IsAbstract || t.IsInterface) continue;
                if (!typeof(IArtifactSource).IsAssignableFrom(t)) continue;
                var ctor = t.GetConstructor(Type.EmptyTypes);
                if (ctor == null) continue;

                IArtifactSource instance;
                try { instance = (IArtifactSource)ctor.Invoke(null); }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        "Failed to instantiate artifact source " + t.FullName, ex);
                }

                string id = instance.Metadata.Id;
                if (byId.ContainsKey(id))
                {
                    if (!conflicts.TryGetValue(id, out var list))
                    {
                        list = new List<string> { byId[id].GetType().FullName };
                        conflicts[id] = list;
                    }
                    list.Add(t.FullName);
                }
                else
                {
                    byId[id] = instance;
                }
            }

            if (conflicts.Count > 0)
            {
                var msg = new System.Text.StringBuilder();
                msg.Append("Duplicate artifact source ids:");
                foreach (var c in conflicts)
                {
                    msg.AppendLine();
                    msg.Append("  '").Append(c.Key).Append("': ");
                    msg.Append(string.Join(", ", c.Value));
                }
                throw new InvalidOperationException(msg.ToString());
            }

            return new SourceCatalog(byId);
        }
    }
}
