using System;
using System.Collections.Generic;

namespace Forens.Core.Collection.Run
{
    public sealed class ProcessFilterCriteria
    {
        public ProcessFilterCriteria(
            IReadOnlyList<int> pids,
            IReadOnlyList<string> processNames,
            IReadOnlyList<string> resolvedImagePaths)
        {
            Pids = pids ?? Array.Empty<int>();
            ProcessNames = processNames ?? Array.Empty<string>();
            ResolvedImagePaths = resolvedImagePaths ?? Array.Empty<string>();
        }

        public IReadOnlyList<int> Pids { get; }
        public IReadOnlyList<string> ProcessNames { get; }
        public IReadOnlyList<string> ResolvedImagePaths { get; }

        public bool IsEmpty
        {
            get { return Pids.Count == 0 && ProcessNames.Count == 0 && ResolvedImagePaths.Count == 0; }
        }
    }
}
