using System;
using System.Collections.Generic;
using System.Linq;
using Forens.Core.Collection.Run;

namespace Forens.Core.Collection
{
    public sealed class ProcessFilter
    {
        private static readonly StringComparer PathComparer = StringComparer.OrdinalIgnoreCase;

        public static readonly ProcessFilter Empty =
            new ProcessFilter(new ProcessFilterCriteria(null, null, null));

        private readonly HashSet<int> _pids;
        private readonly HashSet<string> _imagePaths;

        public ProcessFilter(ProcessFilterCriteria criteria)
        {
            if (criteria == null) throw new ArgumentNullException(nameof(criteria));
            Criteria = criteria;
            _pids = new HashSet<int>(criteria.Pids);
            _imagePaths = new HashSet<string>(criteria.ResolvedImagePaths, PathComparer);
        }

        public ProcessFilterCriteria Criteria { get; }

        public bool IsEmpty
        {
            get { return Criteria.IsEmpty; }
        }

        public bool Includes(int pid)
        {
            return IsEmpty || _pids.Contains(pid);
        }

        public bool IncludesImagePath(string imagePath)
        {
            if (string.IsNullOrEmpty(imagePath)) return IsEmpty;
            return IsEmpty || _imagePaths.Contains(imagePath);
        }
    }
}
