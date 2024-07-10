using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace multiscrape {
    public class WaybackAvailable {
        public string url { get; set; }
        public Archived_Snapshots archived_snapshots { get; set; }
    }

    public class Archived_Snapshots {
        public Closest closest { get; set; }
    }

    public class Closest {
        public string status { get; set; }
        public bool available { get; set; }
        public string url { get; set; }
        public string timestamp { get; set; }
    }
}
